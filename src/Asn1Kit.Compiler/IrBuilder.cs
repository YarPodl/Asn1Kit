using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

internal sealed class IrBuilder
{
    private readonly List<ModuleAst> _modules;
    private int _synthetic;

    public IrBuilder(List<ModuleAst> modules)
    {
        _modules = modules;
    }

    public IReadOnlyList<IrModule> Build()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in _modules)
        {
            foreach (var assignment in module.Assignments)
            {
                known.Add(assignment.Name);
            }
        }

        foreach (var module in _modules)
        {
            foreach (var import in module.Imports)
            {
                foreach (var type in import.Types)
                {
                    if (!known.Contains(type) && !BuiltinTypes.IsBuiltin(type))
                    {
                        throw new CompileException(
                            $"Imported type '{type}' from '{import.Module}' was not found among compiled modules.",
                            1,
                            1);
                    }
                }
            }
        }

        return _modules.Select(BuildModule).ToList();
    }

    private IrModule BuildModule(ModuleAst ast)
    {
        var ir = new IrModule
        {
            IrVersion = 1,
            Module = ast.Name,
            Oid = ast.Oid,
            Source = ast.Source,
            TagDefault = ast.TagDefault switch
            {
                TagDefaultKind.Implicit => TagDefaults.Implicit,
                TagDefaultKind.Automatic => TagDefaults.Automatic,
                _ => TagDefaults.Explicit
            }
        };

        foreach (var import in ast.Imports)
        {
            ir.Imports.Add(new IrImport { Module = import.Module, Types = import.Types.ToList() });
        }

        ir.Options[OptionKeys.CSharpNamespace] = SanitizeNamespace(ast.Name);

        foreach (var assignment in ast.Assignments)
        {
            FlattenAssignment(ir, assignment.Name, assignment.Type, typeTag: null);
        }

        return ir;
    }

    private void FlattenAssignment(IrModule ir, string name, TypeAst type, IrTag? typeTag)
    {
        if (type is TaggedTypeAst tagged)
        {
            var resolved = ResolveTag(tagged.Tag, ir.TagDefault, innerIsChoice: tagged.Inner is ChoiceTypeAst);
            FlattenAssignment(ir, name, tagged.Inner, resolved);
            if (ir.Types.Last().Tag is null)
            {
                ir.Types.Last().Tag = resolved;
            }

            return;
        }

        IrTypeDef def;
        switch (type)
        {
            case BuiltinTypeAst builtin:
                def = new IrTypeDef
                {
                    Name = name,
                    Kind = TypeKinds.Alias,
                    Type = builtin.Name,
                    Tag = typeTag,
                    NamedNumbers = builtin.NamedNumbers?
                        .Select(n => new IrNamedNumber { Name = n.Name, Value = n.Value })
                        .ToList()
                };
                break;
            case TypeReferenceAst reference:
                def = new IrTypeDef
                {
                    Name = name,
                    Kind = TypeKinds.Alias,
                    Type = reference.Name,
                    Tag = typeTag
                };
                break;
            case SequenceOfTypeAst sequenceOf:
                var elementName = EnsureNamed(ir, $"{name}Element", sequenceOf.Element);
                def = new IrTypeDef
                {
                    Name = name,
                    Kind = TypeKinds.SequenceOf,
                    ElementType = elementName,
                    Tag = typeTag
                };
                break;
            case SequenceTypeAst sequence:
                def = new IrTypeDef
                {
                    Name = name,
                    Kind = TypeKinds.Sequence,
                    Tag = typeTag,
                    Fields = BuildFields(ir, name, sequence.Fields, ir.TagDefault, allowOptional: true)
                };
                break;
            case ChoiceTypeAst choice:
                def = new IrTypeDef
                {
                    Name = name,
                    Kind = TypeKinds.Choice,
                    Tag = typeTag,
                    Fields = BuildFields(ir, name, choice.Fields, ir.TagDefault, allowOptional: false)
                };
                break;
            default:
                throw new CompileException($"Unsupported type form for '{name}'.", 1, 1);
        }

        def.Options[OptionKeys.CSharpTypeName] = SanitizeTypeName(name);
        ir.Types.Add(def);
    }

    private List<IrField> BuildFields(
        IrModule ir,
        string owner,
        List<FieldAst> fields,
        string tagDefault,
        bool allowOptional)
    {
        var result = new List<IrField>();
        var autoIndex = 0;
        foreach (var field in fields)
        {
            var (typeName, explicitTag, innerIsChoice) = UnwrapFieldType(ir, owner, field);
            IrTag? tag = explicitTag;
            if (tag is null && tagDefault == TagDefaults.Automatic)
            {
                var mode = innerIsChoice ? TagModes.Explicit : TagModes.Implicit;
                tag = new IrTag { Class = TagClasses.Context, Number = autoIndex, Mode = mode };
            }

            if (tagDefault == TagDefaults.Automatic)
            {
                autoIndex++;
            }

            result.Add(new IrField
            {
                Name = field.Name,
                Type = typeName,
                Optional = allowOptional && field.Optional,
                Tag = tag
            });
        }

        return result;
    }

    private (string TypeName, IrTag? Tag, bool InnerIsChoice) UnwrapFieldType(IrModule ir, string owner, FieldAst field)
    {
        var type = field.Type;
        IrTag? tag = null;
        var innerIsChoice = false;

        if (type is TaggedTypeAst tagged)
        {
            innerIsChoice = tagged.Inner is ChoiceTypeAst;
            tag = ResolveTag(tagged.Tag, ir.TagDefault, innerIsChoice);
            type = tagged.Inner;
        }

        innerIsChoice = type is ChoiceTypeAst;
        var typeName = EnsureNamed(ir, $"{owner}_{field.Name}", type);
        return (typeName, tag, innerIsChoice);
    }

    private string EnsureNamed(IrModule ir, string hint, TypeAst type)
    {
        switch (type)
        {
            case BuiltinTypeAst builtin:
                return builtin.Name;
            case TypeReferenceAst reference:
                return reference.Name;
            case TaggedTypeAst tagged:
                var synthetic = Unique(hint);
                FlattenAssignment(ir, synthetic, tagged, typeTag: null);
                return synthetic;
            default:
                var name = Unique(hint);
                FlattenAssignment(ir, name, type, typeTag: null);
                return name;
        }
    }

    private string Unique(string hint)
    {
        var cleaned = SanitizeTypeName(hint);
        _synthetic++;
        return $"{cleaned}_{_synthetic}";
    }

    private static IrTag ResolveTag(TagAst tag, string tagDefault, bool innerIsChoice)
    {
        var mode = tag.Mode;
        if (string.IsNullOrEmpty(mode))
        {
            if (tagDefault == TagDefaults.Implicit && !innerIsChoice)
            {
                mode = TagModes.Implicit;
            }
            else if (tagDefault == TagDefaults.Automatic && !innerIsChoice)
            {
                mode = TagModes.Implicit;
            }
            else
            {
                mode = TagModes.Explicit;
            }
        }

        return new IrTag
        {
            Class = tag.Class,
            Number = tag.Number,
            Mode = mode
        };
    }

    internal static string SanitizeNamespace(string moduleName)
    {
        var parts = moduleName.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(".", parts.Select(SanitizeTypeName));
    }

    internal static string SanitizeTypeName(string name)
    {
        var parts = name.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : "")));
    }
}

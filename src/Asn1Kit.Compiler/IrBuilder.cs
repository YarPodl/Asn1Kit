using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

internal sealed class IrBuilder
{
    private readonly List<ModuleAst> _modules;

    public IrBuilder(List<ModuleAst> modules)
    {
        _modules = modules;
    }

    public IrDocument Build()
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
                    if (!known.Contains(type))
                    {
                        throw new CompileException(
                            $"Imported type '{type}' from '{import.Module}' was not found among compiled modules.",
                            1,
                            1);
                    }
                }
            }
        }

        var document = new IrDocument { IrVersion = 1 };
        var sources = _modules.Select(m => m.Source).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
        if (sources.Count > 0)
        {
            document.SourceFiles = sources;
        }

        foreach (var module in _modules)
        {
            document.Modules.Add(BuildModule(module));
        }

        return document;
    }

    private static IrModule BuildModule(ModuleAst ast)
    {
        var ir = new IrModule
        {
            Name = ast.Name,
            Oid = ast.Oid,
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

        ir.Options = IrOptions.SetCSharp(ir.Options, "namespace", SanitizeNamespace(ast.Name));

        foreach (var assignment in ast.Assignments)
        {
            var def = new IrTypeDef
            {
                Name = assignment.Name,
                Type = ConvertType(assignment.Type, ir.TagDefault, assignedName: assignment.Name)
            };
            def.Options = IrOptions.SetCSharp(def.Options, "typeName", SanitizeTypeName(assignment.Name));
            ir.Types.Add(def);
        }

        return ir;
    }

    private static TypeExpr ConvertType(TypeAst type, string tagDefault, string? assignedName)
    {
        if (type is TaggedTypeAst tagged)
        {
            var innerIsChoice = IsChoice(tagged.Inner);
            var converted = ConvertType(tagged.Inner, tagDefault, assignedName);
            converted.Tag = ResolveTag(tagged.Tag, tagDefault, innerIsChoice);
            return converted;
        }

        return type switch
        {
            BuiltinTypeAst builtin => ConvertBuiltin(builtin),
            EnumeratedTypeAst enumerated => new EnumeratedType
            {
                Values = enumerated.Values.Select(n => new IrNamedNumber { Name = n.Name, Value = n.Value }).ToList()
            },
            TypeReferenceAst reference => new RefType { Name = reference.Name },
            SequenceOfTypeAst sequenceOf => new SequenceOfType
            {
                Element = ConvertType(sequenceOf.Element, tagDefault, assignedName: null)
            },
            SequenceTypeAst sequence => new SequenceType
            {
                Components = ConvertComponents(sequence.Fields, tagDefault, allowOptional: true)
            },
            ChoiceTypeAst choice => new ChoiceType
            {
                Components = ConvertComponents(choice.Fields, tagDefault, allowOptional: false)
            },
            _ => throw new CompileException($"Unsupported type form{(assignedName is null ? "" : $" for '{assignedName}'")}.", 1, 1)
        };
    }

    private static TypeExpr ConvertBuiltin(BuiltinTypeAst builtin) => builtin.Name switch
    {
        "BOOLEAN" => new BooleanType(),
        "NULL" => new NullType(),
        "OCTET STRING" => new OctetStringType(),
        "OBJECT IDENTIFIER" => new OidType(),
        "INTEGER" => new IntegerType
        {
            NamedValues = builtin.NamedNumbers is { Count: > 0 }
                ? builtin.NamedNumbers.Select(n => new IrNamedNumber { Name = n.Name, Value = n.Value }).ToList()
                : null
        },
        _ => throw new CompileException($"Unsupported builtin '{builtin.Name}'.", 1, 1)
    };

    private static List<IrComponent> ConvertComponents(List<FieldAst> fields, string tagDefault, bool allowOptional)
    {
        var result = new List<IrComponent>();
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var innerIsChoice = IsChoice(field.Type);
            var expr = ConvertType(field.Type, tagDefault, assignedName: null);
            if (expr.Tag is null && tagDefault == TagDefaults.Automatic)
            {
                expr.Tag = new IrTag
                {
                    Class = TagClasses.Context,
                    Number = i,
                    Mode = innerIsChoice ? TagModes.Explicit : TagModes.Implicit
                };
            }

            result.Add(new IrComponent
            {
                Name = field.Name,
                Type = expr,
                Optional = allowOptional && field.Optional
            });
        }

        return result;
    }

    private static bool IsChoice(TypeAst type) => type switch
    {
        ChoiceTypeAst => true,
        TaggedTypeAst tagged => IsChoice(tagged.Inner),
        _ => false
    };

    private static IrTag ResolveTag(TagAst tag, string tagDefault, bool innerIsChoice)
    {
        var mode = tag.Mode;
        if (string.IsNullOrEmpty(mode))
        {
            if ((tagDefault == TagDefaults.Implicit || tagDefault == TagDefaults.Automatic) && !innerIsChoice)
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

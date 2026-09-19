using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

internal sealed class IrBuilder
{
    private readonly List<ModuleAst> _modules;
    private readonly Dictionary<string, ValueAssignmentAst> _valuesByName;
    private readonly Dictionary<string, TypeAssignmentAst> _typesByName;
    private readonly HashSet<string> _resolvingOids = new(StringComparer.Ordinal);

    public IrBuilder(List<ModuleAst> modules)
    {
        _modules = modules;
        _valuesByName = new Dictionary<string, ValueAssignmentAst>(StringComparer.Ordinal);
        _typesByName = new Dictionary<string, TypeAssignmentAst>(StringComparer.Ordinal);

        foreach (var module in modules)
        {
            foreach (var type in module.TypeAssignments)
            {
                if (!_typesByName.TryAdd(type.Name, type))
                {
                    throw new CompileException(
                        $"Duplicate type '{type.Name}'.",
                        type.Line,
                        type.Column);
                }
            }

            foreach (var value in module.ValueAssignments)
            {
                if (!_valuesByName.TryAdd(value.Name, value))
                {
                    throw new CompileException(
                        $"Duplicate value '{value.Name}'.",
                        value.Line,
                        value.Column);
                }
            }
        }
    }

    public IrDocument Build()
    {
        foreach (var module in _modules)
        {
            foreach (var import in module.Imports)
            {
                foreach (var type in import.Types)
                {
                    if (!_typesByName.ContainsKey(type))
                    {
                        throw new CompileException(
                            $"Imported type '{type}' from '{import.Module}' was not found among compiled modules.",
                            import.Line,
                            import.Column);
                    }
                }

                foreach (var value in import.Values)
                {
                    if (!_valuesByName.ContainsKey(value))
                    {
                        throw new CompileException(
                            $"Imported value '{value}' from '{import.Module}' was not found among compiled modules.",
                            import.Line,
                            import.Column);
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

    private IrModule BuildModule(ModuleAst ast)
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
            ir.Imports.Add(new IrImport
            {
                Module = import.Module,
                Types = import.Types.ToList(),
                Values = import.Values.Count > 0 ? import.Values.ToList() : null
            });
        }

        ir.Options = IrOptions.SetCSharp(ir.Options, "namespace", SanitizeNamespace(ast.Name));

        foreach (var assignment in ast.TypeAssignments)
        {
            var def = new IrTypeDef
            {
                Name = assignment.Name,
                Type = ConvertType(assignment.Type, ir.TagDefault, assignedName: assignment.Name, ownerFields: null)
            };
            def.Options = IrOptions.SetCSharp(def.Options, "typeName", SanitizeTypeName(assignment.Name));
            ir.Types.Add(def);
        }

        foreach (var assignment in ast.ValueAssignments)
        {
            ir.Values.Add(new IrValueDef
            {
                Name = assignment.Name,
                Type = ConvertType(assignment.Type, ir.TagDefault, assignedName: null, ownerFields: null),
                Value = ResolveValue(assignment.Value, assignment.Type)
            });
        }

        return ir;
    }

    private TypeExpr ConvertType(TypeAst type, string tagDefault, string? assignedName, List<FieldAst>? ownerFields)
    {
        TypeExpr converted;
        if (type is TaggedTypeAst tagged)
        {
            var innerIsChoice = IsChoice(tagged.Inner);
            converted = ConvertType(tagged.Inner, tagDefault, assignedName, ownerFields);
            converted.Tag = ResolveTag(tagged.Tag, tagDefault, innerIsChoice);
        }
        else
        {
            converted = type switch
            {
                BuiltinTypeAst builtin => ConvertBuiltin(builtin),
                BitStringTypeAst bitString => new BitStringType
                {
                    NamedBits = bitString.NamedBits is { Count: > 0 }
                        ? bitString.NamedBits.Select(ToNamedNumber).ToList()
                        : null
                },
                StringTypeAst stringType => new StringType { Form = stringType.StringType },
                TimeTypeAst timeType => new TimeType { Form = timeType.TimeType },
                AnyTypeAst any => ConvertAny(any, ownerFields),
                EnumeratedTypeAst enumerated => new EnumeratedType
                {
                    Values = enumerated.Values.Select(ToNamedNumber).ToList()
                },
                TypeReferenceAst reference => new RefType { Name = reference.Name, Module = reference.Module },
                SequenceOfTypeAst sequenceOf => new SequenceOfType
                {
                    Element = ConvertType(sequenceOf.Element, tagDefault, assignedName: null, ownerFields: null)
                },
                SetOfTypeAst setOf => new SetOfType
                {
                    Element = ConvertType(setOf.Element, tagDefault, assignedName: null, ownerFields: null)
                },
                SequenceTypeAst sequence => new SequenceType
                {
                    Components = ConvertComponents(sequence.Fields, tagDefault, allowOptional: true),
                    Extensible = sequence.Extensible
                },
                SetTypeAst set => new SetType
                {
                    Components = ConvertComponents(set.Fields, tagDefault, allowOptional: true),
                    Extensible = set.Extensible
                },
                ChoiceTypeAst choice => new ChoiceType
                {
                    Components = ConvertComponents(choice.Fields, tagDefault, allowOptional: false),
                    Extensible = choice.Extensible
                },
                _ => throw new CompileException(
                    $"Unsupported type form{(assignedName is null ? "" : $" for '{assignedName}'")}.",
                    type.Line,
                    type.Column)
            };
        }

        if (type.Constraint is not null)
        {
            converted.Constraint = ConvertConstraint(type.Constraint);
        }

        return converted;
    }

    private static AnyType ConvertAny(AnyTypeAst any, List<FieldAst>? ownerFields)
    {
        if (any.DefinedBy is not null && ownerFields is not null)
        {
            if (ownerFields.All(f => f.Name != any.DefinedBy))
            {
                throw new CompileException(
                    $"ANY DEFINED BY '{any.DefinedBy}' does not name a sibling component.",
                    any.Line,
                    any.Column);
            }
        }

        return new AnyType { DefinedBy = any.DefinedBy };
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
                ? builtin.NamedNumbers.Select(ToNamedNumber).ToList()
                : null
        },
        _ => throw new CompileException($"Unsupported builtin '{builtin.Name}'.", builtin.Line, builtin.Column)
    };

    private List<IrComponent> ConvertComponents(List<FieldAst> fields, string tagDefault, bool allowOptional)
    {
        var result = new List<IrComponent>();
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var innerIsChoice = IsChoice(field.Type);
            var expr = ConvertType(field.Type, tagDefault, assignedName: null, ownerFields: fields);
            if (expr.Tag is null && tagDefault == TagDefaults.Automatic)
            {
                expr.Tag = new IrTag
                {
                    Class = TagClasses.Context,
                    Number = i,
                    Mode = innerIsChoice ? TagModes.Explicit : TagModes.Implicit
                };
            }

            IrValue? defaultValue = null;
            var optional = allowOptional && (field.Optional || field.Default is not null);
            if (field.Default is not null)
            {
                defaultValue = ResolveDefault(field.Default, field.Type);
            }

            result.Add(new IrComponent
            {
                Name = field.Name,
                Type = expr,
                Optional = optional,
                Default = defaultValue
            });
        }

        return result;
    }

    private IrConstraint ConvertConstraint(ConstraintAst constraint)
    {
        var ir = new IrConstraint { Unsupported = constraint.Unsupported };
        if (constraint.HasSize)
        {
            ir.Size = ConvertBound(constraint.SizeMin!, constraint.SizeMax!);
        }

        if (constraint.HasValue)
        {
            ir.Value = ConvertBound(constraint.ValueMin!, constraint.ValueMax!);
        }

        return ir;
    }

    private IrBound ConvertBound(BoundAst min, BoundAst max)
    {
        return new IrBound
        {
            Min = ResolveBoundNumber(min),
            Max = max.IsMax ? null : ResolveBoundNumber(max)
        };
    }

    private long ResolveBoundNumber(BoundAst bound)
    {
        if (bound.IsMin)
        {
            throw new CompileException("MIN is not supported as a concrete bound in IR.", bound.Line, bound.Column);
        }

        if (bound.IsMax)
        {
            throw new CompileException("MAX must be the upper end of a range.", bound.Line, bound.Column);
        }

        if (bound.Number is not null)
        {
            return bound.Number.Value;
        }

        if (bound.Reference is null)
        {
            throw new CompileException("Constraint bound is empty.", bound.Line, bound.Column);
        }

        if (!_valuesByName.TryGetValue(bound.Reference, out var assignment))
        {
            throw new CompileException(
                $"Unknown value reference '{bound.Reference}' in constraint.",
                bound.Line,
                bound.Column);
        }

        var resolved = ResolveValue(assignment.Value, assignment.Type);
        if (resolved is IrIntegerValue integer)
        {
            return integer.Value;
        }

        throw new CompileException(
            $"Value '{bound.Reference}' used in a constraint must resolve to an INTEGER.",
            bound.Line,
            bound.Column);
    }

    private IrValue ResolveDefault(ValueAst value, TypeAst fieldType)
    {
        if (value is ValueReferenceAst reference)
        {
            var named = FindNamedNumber(fieldType, reference.Name);
            if (named is not null)
            {
                return new IrIntegerValue { Value = named.Value };
            }

            if (_valuesByName.TryGetValue(reference.Name, out var assignment))
            {
                return ResolveValue(assignment.Value, assignment.Type);
            }

            throw new CompileException(
                $"Unknown DEFAULT value '{reference.Name}'.",
                reference.Line,
                reference.Column);
        }

        return ResolveValue(value, fieldType);
    }

    private NamedNumberAst? FindNamedNumber(TypeAst type, string name)
    {
        type = UnwrapTagged(type);
        return type switch
        {
            BuiltinTypeAst { Name: "INTEGER", NamedNumbers: { } named } =>
                named.FirstOrDefault(n => n.Name == name),
            EnumeratedTypeAst enumerated =>
                enumerated.Values.FirstOrDefault(n => n.Name == name),
            TypeReferenceAst reference when _typesByName.TryGetValue(reference.Name, out var assignment) =>
                FindNamedNumber(assignment.Type, name),
            _ => null
        };
    }

    private static TypeAst UnwrapTagged(TypeAst type) =>
        type is TaggedTypeAst tagged ? UnwrapTagged(tagged.Inner) : type;

    private IrValue ResolveValue(ValueAst value, TypeAst declaredType)
    {
        return value switch
        {
            IntegerValueAst integer => new IrIntegerValue { Value = integer.Value },
            BooleanValueAst boolean => new IrBooleanValue { Value = boolean.Value },
            NullValueAst => new IrNullValue(),
            CStringValueAst cstring => new IrStringValue { Value = cstring.Value },
            BStringValueAst bstring => new IrBitStringValue { Bits = bstring.Bits },
            HStringValueAst hstring => new IrBitStringValue { Hex = hstring.Hex },
            OidValueAst oid => new IrOidValue { Arcs = ResolveOidArcs(oid) },
            ValueReferenceAst reference => ResolveValueReference(reference, declaredType),
            _ => throw new CompileException("Unsupported value form.", value.Line, value.Column)
        };
    }

    private IrValue ResolveValueReference(ValueReferenceAst reference, TypeAst declaredType)
    {
        var named = FindNamedNumber(declaredType, reference.Name);
        if (named is not null)
        {
            return new IrIntegerValue { Value = named.Value };
        }

        if (!_valuesByName.TryGetValue(reference.Name, out var assignment))
        {
            throw new CompileException(
                $"Unknown value reference '{reference.Name}'.",
                reference.Line,
                reference.Column);
        }

        return ResolveValue(assignment.Value, assignment.Type);
    }

    private List<int> ResolveOidArcs(OidValueAst oid)
    {
        var arcs = new List<int>();
        var index = 0;
        if (oid.Arcs.Count > 0 && oid.Arcs[0].Name is not null && oid.Arcs[0].Number is null)
        {
            var head = oid.Arcs[0];
            if (!_valuesByName.TryGetValue(head.Name!, out var assignment))
            {
                throw new CompileException(
                    $"Unknown OID value reference '{head.Name}'.",
                    head.Line,
                    head.Column);
            }

            if (!_resolvingOids.Add(head.Name!))
            {
                throw new CompileException(
                    $"Cyclic OBJECT IDENTIFIER value '{head.Name}'.",
                    head.Line,
                    head.Column);
            }

            try
            {
                var resolved = ResolveValue(assignment.Value, assignment.Type);
                if (resolved is not IrOidValue parent)
                {
                    throw new CompileException(
                        $"OID component '{head.Name}' does not resolve to an OBJECT IDENTIFIER.",
                        head.Line,
                        head.Column);
                }

                arcs.AddRange(parent.Arcs);
            }
            finally
            {
                _resolvingOids.Remove(head.Name!);
            }

            index = 1;
        }

        for (; index < oid.Arcs.Count; index++)
        {
            var arc = oid.Arcs[index];
            if (arc.Number is null)
            {
                throw new CompileException(
                    $"OID component '{arc.Name}' must be numeric or name(number).",
                    arc.Line,
                    arc.Column);
            }

            arcs.Add(arc.Number.Value);
        }

        return arcs;
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

    private static IrNamedNumber ToNamedNumber(NamedNumberAst n) =>
        new() { Name = n.Name, Value = n.Value };

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

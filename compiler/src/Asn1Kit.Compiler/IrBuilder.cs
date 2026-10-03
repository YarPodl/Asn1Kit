using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

internal sealed class IrBuilder
{
    private readonly List<ModuleAst> _modules;
    private readonly Dictionary<string, ModuleAst> _modulesByName;
    private readonly Dictionary<string, Dictionary<string, TypeAssignmentAst>> _typesByModule;
    private readonly Dictionary<string, Dictionary<string, ValueAssignmentAst>> _valuesByModule;
    private readonly HashSet<string> _resolvingOids = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolvingValues = new(StringComparer.Ordinal);
    private string _currentModule = "";

    public IrBuilder(List<ModuleAst> modules)
    {
        _modules = modules;
        _modulesByName = new Dictionary<string, ModuleAst>(StringComparer.Ordinal);
        _typesByModule = new Dictionary<string, Dictionary<string, TypeAssignmentAst>>(StringComparer.Ordinal);
        _valuesByModule = new Dictionary<string, Dictionary<string, ValueAssignmentAst>>(StringComparer.Ordinal);

        foreach (var module in modules)
        {
            if (!_modulesByName.TryAdd(module.Name, module))
            {
                throw new CompileException(
                    $"Duplicate module '{module.Name}'.",
                    module.Line,
                    module.Column);
            }

            var types = new Dictionary<string, TypeAssignmentAst>(StringComparer.Ordinal);
            foreach (var type in module.TypeAssignments)
            {
                if (!types.TryAdd(type.Name, type))
                {
                    throw new CompileException(
                        $"Duplicate type '{type.Name}'.",
                        type.Line,
                        type.Column);
                }
            }

            _typesByModule[module.Name] = types;

            var values = new Dictionary<string, ValueAssignmentAst>(StringComparer.Ordinal);
            foreach (var value in module.ValueAssignments)
            {
                if (!values.TryAdd(value.Name, value))
                {
                    throw new CompileException(
                        $"Duplicate value '{value.Name}'.",
                        value.Line,
                        value.Column);
                }
            }

            _valuesByModule[module.Name] = values;
        }
    }

    public IrDocument Build()
    {
        foreach (var module in _modules)
        {
            foreach (var import in module.Imports)
            {
                if (!_modulesByName.TryGetValue(import.Module, out var source))
                {
                    throw new CompileException(
                        $"Imported module '{import.Module}' was not found among compiled modules.",
                        import.Line,
                        import.Column);
                }

                foreach (var type in import.Types)
                {
                    if (!source.TypeAssignments.Any(t => t.Name == type))
                    {
                        throw new CompileException(
                            $"Imported type '{type}' from '{import.Module}' was not found in module '{import.Module}'.",
                            import.Line,
                            import.Column);
                    }
                }

                foreach (var value in import.Values)
                {
                    if (!source.ValueAssignments.Any(v => v.Name == value))
                    {
                        throw new CompileException(
                            $"Imported value '{value}' from '{import.Module}' was not found in module '{import.Module}'.",
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
        _currentModule = ast.Name;
        var ir = new IrModule
        {
            Name = ast.Name,
            Oid = FormatOid(ast.Oid),
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
            var csharpTypeName = SanitizeTypeName(assignment.Name);
            if (!string.Equals(csharpTypeName, assignment.Name, StringComparison.Ordinal))
            {
                def.Options = IrOptions.SetCSharp(def.Options, "typeName", csharpTypeName);
            }

            ir.Types.Add(def);
        }

        foreach (var assignment in ast.ValueAssignments)
        {
            ir.Values.Add(new IrValueDef
            {
                Name = assignment.Name,
                Type = ConvertType(assignment.Type, ir.TagDefault, assignedName: null, ownerFields: null),
                Value = ResolveValue(assignment.Value, assignment.Type, _currentModule)
            });
        }

        return ir;
    }

    private bool TryResolveType(
        string scopeModule,
        string name,
        string? explicitModule,
        out TypeAssignmentAst assignment,
        out string definingModule)
    {
        if (!string.IsNullOrEmpty(explicitModule))
        {
            definingModule = explicitModule!;
            if (_typesByModule.TryGetValue(explicitModule!, out var typed) &&
                typed.TryGetValue(name, out assignment!))
            {
                return true;
            }

            assignment = null!;
            return false;
        }

        if (_typesByModule.TryGetValue(scopeModule, out var local) &&
            local.TryGetValue(name, out assignment!))
        {
            definingModule = scopeModule;
            return true;
        }

        if (_modulesByName.TryGetValue(scopeModule, out var scope))
        {
            foreach (var import in scope.Imports)
            {
                if (!import.Types.Contains(name))
                {
                    continue;
                }

                if (_typesByModule.TryGetValue(import.Module, out var imported) &&
                    imported.TryGetValue(name, out assignment!))
                {
                    definingModule = import.Module;
                    return true;
                }
            }
        }

        assignment = null!;
        definingModule = "";
        return false;
    }

    private bool TryResolveValue(
        string scopeModule,
        string name,
        out ValueAssignmentAst assignment,
        out string definingModule)
    {
        if (_valuesByModule.TryGetValue(scopeModule, out var local) &&
            local.TryGetValue(name, out assignment!))
        {
            definingModule = scopeModule;
            return true;
        }

        if (_modulesByName.TryGetValue(scopeModule, out var scope))
        {
            foreach (var import in scope.Imports)
            {
                if (!import.Values.Contains(name))
                {
                    continue;
                }

                if (_valuesByModule.TryGetValue(import.Module, out var imported) &&
                    imported.TryGetValue(name, out assignment!))
                {
                    definingModule = import.Module;
                    return true;
                }
            }
        }

        assignment = null!;
        definingModule = "";
        return false;
    }

    private TypeExpr ConvertType(TypeAst type, string tagDefault, string? assignedName, List<FieldAst>? ownerFields)
    {
        tagDefault = type.TagDefaultOverride switch
        {
            TagDefaultKind.Explicit => TagDefaults.Explicit,
            TagDefaultKind.Implicit => TagDefaults.Implicit,
            TagDefaultKind.Automatic => TagDefaults.Automatic,
            _ => tagDefault
        };
        TypeExpr converted;
        if (type is TaggedTypeAst tagged)
        {
            var innerIsChoice = IsChoice(tagged.Inner);
            if (tagged.Tag.Mode == TagModes.Implicit && tagged.Inner is AnyTypeAst { TableExtensible: not null })
                throw new CompileException("IMPLICIT tagging of an untagged open type is outside the Asn1Kit compiler profile.", tagged.Line, tagged.Column);
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
                    Extensible = enumerated.Extensible ? true : null,
                    Values = enumerated.Values.Select(ToNamedNumber).ToList()
                },
                TypeReferenceAst reference => new RefType { Name = reference.Name, Module = reference.Module },
                ContainingTypeAst contained => ConvertContaining(contained, tagDefault, ownerFields),
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
            converted.Constraint = ConvertConstraint(type.Constraint, type);
        }

        return converted;
    }

    private TypeExpr ConvertContaining(ContainingTypeAst contained, string tagDefault, List<FieldAst>? ownerFields)
    {
        var outer = ConvertType(contained.Outer, tagDefault, null, ownerFields);
        var inner = ConvertType(contained.Inner, tagDefault, null, ownerFields);
        if (outer is OctetStringType octets) octets.Containing = inner;
        else if (outer is BitStringType bits) bits.Containing = inner;
        else throw new CompileException("CONTAINING on this type is outside the Asn1Kit compiler profile.", contained.Line, contained.Column);
        return outer;
    }

    private AnyType ConvertAny(AnyTypeAst any, List<FieldAst>? ownerFields)
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

        return new AnyType
        {
            DefinedBy = any.DefinedBy, Selector = any.Selector, TableExtensible = any.TableExtensible,
            Bindings = any.Bindings?.Select(ConvertBinding).ToList()
        };
    }

    private IrOpenTypeBinding ConvertBinding(OpenTypeBindingAst binding)
    {
        var previous = _currentModule;
        if (binding.Module is not null) _currentModule = binding.Module;
        try
        {
            var type = ConvertType(binding.Type, _modulesByName[_currentModule].TagDefault switch
            {
                TagDefaultKind.Implicit => TagDefaults.Implicit, TagDefaultKind.Automatic => TagDefaults.Automatic, _ => TagDefaults.Explicit
            }, null, null);
            QualifyBindingReferences(type);
            return new IrOpenTypeBinding { Key = binding.Key, Name = string.IsNullOrEmpty(binding.Name) ? null : binding.Name, Type = type };
        }
        finally { _currentModule = previous; }
    }

    private void QualifyBindingReferences(TypeExpr type)
    {
        if (type is RefType reference && reference.Module is null && TryResolveType(_currentModule, reference.Name, null, out _, out var module)) reference.Module = module;
        var children = type switch
        {
            SequenceType seq => seq.Components.Select(c => c.Type), SetType set => set.Components.Select(c => c.Type), ChoiceType choice => choice.Components.Select(c => c.Type),
            SequenceOfType seq => new[] { seq.Element }, SetOfType set => new[] { set.Element },
            OctetStringType { Containing: { } inner } => new[] { inner },
            BitStringType { Containing: { } inner } => new[] { inner }, _ => Array.Empty<TypeExpr>()
        };
        foreach (var child in children) QualifyBindingReferences(child);
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
                Default = defaultValue,
                ExtensionAddition = field.ExtensionAddition ? true : null,
                ExtensionGroup = field.ExtensionGroup
            });
        }

        return result;
    }

    private IrConstraint ConvertConstraint(ConstraintAst constraint, TypeAst? type = null)
    {
        var ir = new IrConstraint { Unsupported = constraint.Unsupported };
        if (constraint.HasSize)
        {
            ir.Size = ConvertBound(constraint.SizeMin!, constraint.SizeMax!, type);
        }

        if (constraint.HasValue)
        {
            ir.Value = ConvertBound(constraint.ValueMin!, constraint.ValueMax!, type);
        }

        return ir;
    }

    private IrBound ConvertBound(BoundAst min, BoundAst max, TypeAst? type)
    {
        return new IrBound
        {
            Min = ResolveBoundNumber(min, type),
            Max = max.IsMax ? null : ResolveBoundNumber(max, type)
        };
    }

    private long ResolveBoundNumber(BoundAst bound, TypeAst? type)
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

        if (type is not null && FindNamedNumber(type, bound.Reference) is { } named) return named.Value;

        if (!TryResolveValue(bound.Module ?? _currentModule, bound.Reference, out var assignment, out var definingModule))
        {
            throw new CompileException(
                $"Unknown value reference '{bound.Reference}' in constraint.",
                bound.Line,
                bound.Column);
        }

        var resolved = ResolveValue(assignment.Value, assignment.Type, definingModule);
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

            if (TryResolveValue(reference.Module ?? _currentModule, reference.Name, out var assignment, out var definingModule))
            {
                return ResolveValue(assignment.Value, assignment.Type, definingModule);
            }

            throw new CompileException(
                $"Unknown DEFAULT value '{reference.Name}'.",
                reference.Line,
                reference.Column);
        }

        return ResolveValue(value, fieldType, _currentModule);
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
            TypeReferenceAst reference when TryResolveType(
                _currentModule, reference.Name, reference.Module, out var assignment, out _) =>
                FindNamedNumber(assignment.Type, name),
            _ => null
        };
    }

    private static TypeAst UnwrapTagged(TypeAst type) =>
        type is TaggedTypeAst tagged ? UnwrapTagged(tagged.Inner) : type;

    private IrValue ResolveValue(ValueAst value, TypeAst declaredType, string scopeModule)
    {
        return value switch
        {
            IntegerValueAst integer => new IrIntegerValue { Value = integer.Value },
            BooleanValueAst boolean => new IrBooleanValue { Value = boolean.Value },
            NullValueAst => new IrNullValue(),
            CStringValueAst cstring => new IrStringValue { Value = cstring.Value },
            BStringValueAst bstring => new IrBitStringValue { Bits = bstring.Bits },
            HStringValueAst hstring when ValueType(declaredType, scopeModule) is BuiltinTypeAst { Name: "OCTET STRING" } => new IrOctetStringValue { Hex = hstring.Hex },
            HStringValueAst hstring => new IrBitStringValue { Hex = hstring.Hex },
            StructuredValueAst structured => ResolveStructuredValue(structured, declaredType, scopeModule),
            TypedValueAst typed => new IrTypedValue { Type = ConvertType(typed.Type, TagDefaults.Explicit, null, null), Value = ResolveValue(typed.Value, typed.Type, scopeModule) },
            CollectionValueAst collection => ResolveCollectionValue(collection, declaredType, scopeModule),
            ChoiceValueAst choice => ResolveChoiceValue(choice, declaredType, scopeModule),
            OidValueAst oid => new IrOidValue { Value = FormatOid(ResolveOidArcs(oid, scopeModule))! },
            ValueReferenceAst reference => ResolveValueReference(reference, declaredType, scopeModule),
            _ => throw new CompileException("Unsupported value form.", value.Line, value.Column)
        };
    }

    private IrValue ResolveValueReference(ValueReferenceAst reference, TypeAst declaredType, string scopeModule)
    {
        var named = FindNamedNumber(declaredType, reference.Name);
        if (named is not null)
        {
            return new IrIntegerValue { Value = named.Value };
        }

        if (!TryResolveValue(reference.Module ?? scopeModule, reference.Name, out var assignment, out var definingModule))
        {
            throw new CompileException(
                $"Unknown value reference '{reference.Name}'.",
                reference.Line,
                reference.Column);
        }

        var key = definingModule + "." + reference.Name;
        if (!_resolvingValues.Add(key)) throw new CompileException($"Cyclic value reference '{reference.Name}'.", reference.Line, reference.Column);
        try { return ResolveValue(assignment.Value, assignment.Type, definingModule); }
        finally { _resolvingValues.Remove(key); }
    }

    private TypeAst ValueType(TypeAst type, string scopeModule, HashSet<string>? visited = null)
    {
        type = UnwrapTagged(type);
        if (type is TypeReferenceAst reference && TryResolveType(scopeModule, reference.Name, reference.Module, out var assignment, out var definingModule))
        {
            visited ??= new HashSet<string>();
            if (!visited.Add(definingModule + "." + reference.Name))
                throw new CompileException("Cyclic value type alias.", type.Line, type.Column);
            return ValueType(assignment.Type, definingModule, visited);
        }
        return type;
    }

    private IrValue ResolveStructuredValue(StructuredValueAst value, TypeAst type, string scopeModule)
    {
        var effective = ValueType(type, scopeModule);
        var fields = effective switch { SequenceTypeAst seq => seq.Fields, SetTypeAst set => set.Fields, _ => throw new CompileException("Structured value requires SEQUENCE or SET.", value.Line, value.Column) };
        return new IrStructuredValue { Fields = value.Fields.ToDictionary(p => p.Key, p => ResolveValue(p.Value, fields.Single(f => f.Name == p.Key).Type, scopeModule)) };
    }

    private IrValue ResolveCollectionValue(CollectionValueAst value, TypeAst type, string scopeModule)
    {
        var effective = ValueType(type, scopeModule);
        var element = effective switch { SequenceOfTypeAst seq => seq.Element, SetOfTypeAst set => set.Element, _ => throw new CompileException("Collection value requires an OF type.", value.Line, value.Column) };
        return new IrCollectionValue { Items = value.Items.Select(v => ResolveValue(v, element, scopeModule)).ToList() };
    }

    private IrValue ResolveChoiceValue(ChoiceValueAst value, TypeAst type, string scopeModule)
    {
        var effective = (ChoiceTypeAst)ValueType(type, scopeModule);
        return new IrChoiceValue { Alternative = value.Alternative, Value = ResolveValue(value.Value, effective.Fields.Single(f => f.Name == value.Alternative).Type, scopeModule) };
    }

    private List<int> ResolveOidArcs(OidValueAst oid, string scopeModule)
    {
        var arcs = new List<int>();
        var index = 0;
        if (oid.Arcs.Count > 0 && oid.Arcs[0].Name is not null && oid.Arcs[0].Number is null)
        {
            var head = oid.Arcs[0];
            if (!TryResolveValue(scopeModule, head.Name!, out var assignment, out var definingModule))
            {
                throw new CompileException(
                    $"Unknown OID value reference '{head.Name}'.",
                    head.Line,
                    head.Column);
            }

            var cycleKey = definingModule + "::" + head.Name!;
            if (!_resolvingOids.Add(cycleKey))
            {
                throw new CompileException(
                    $"Cyclic OBJECT IDENTIFIER value '{head.Name}'.",
                    head.Line,
                    head.Column);
            }

            try
            {
                var resolved = ResolveValue(assignment.Value, assignment.Type, definingModule);
                if (resolved is not IrOidValue parent)
                {
                    throw new CompileException(
                        $"OID component '{head.Name}' does not resolve to an OBJECT IDENTIFIER.",
                        head.Line,
                        head.Column);
                }

                arcs.AddRange(ParseOidArcs(parent.Value));
            }
            finally
            {
                _resolvingOids.Remove(cycleKey);
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

    private static string? FormatOid(IReadOnlyList<int>? arcs) =>
        arcs is null || arcs.Count == 0 ? null : string.Join(".", arcs);

    private static IEnumerable<int> ParseOidArcs(string oid) =>
        oid.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);

    private bool IsChoice(TypeAst type) => IsChoice(type, _currentModule, new HashSet<string>(StringComparer.Ordinal));

    private bool IsChoice(TypeAst type, string scopeModule, HashSet<string> visited) => type switch
    {
        ChoiceTypeAst => true,
        AnyTypeAst { TableExtensible: not null } => true,
        TaggedTypeAst tagged => IsChoice(tagged.Inner, scopeModule, visited),
        TypeReferenceAst reference => IsChoiceReference(reference, scopeModule, visited),
        _ => false
    };

    private bool IsChoiceReference(TypeReferenceAst reference, string scopeModule, HashSet<string> visited)
    {
        if (!TryResolveType(scopeModule, reference.Name, reference.Module, out var assignment, out var definingModule))
        {
            return false;
        }

        var key = definingModule + "." + reference.Name;
        return visited.Add(key) && IsChoice(assignment.Type, definingModule, visited);
    }

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
        return string.Join(".", parts.Select(p => char.IsDigit(p[0]) ? "_" + SanitizeTypeName(p) : SanitizeTypeName(p)));
    }

    internal static string SanitizeTypeName(string name)
    {
        var parts = name.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : "")));
    }
}

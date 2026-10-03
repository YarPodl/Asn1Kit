using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

/// <summary>Resolves information objects and specializes templates before the IR builder runs.</summary>
internal sealed class InformationResolver
{
    private readonly Dictionary<string, ModuleAst> _modules;
    private readonly Dictionary<string, ModuleAst> _output = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResolvedObject> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResolvedSet> _sets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _specializations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<InformationClassAst, string> _classModules = new();
    private readonly InformationClassAst _typeIdentifier;
    private readonly Dictionary<(ResolvedObject Object, InformationFieldAst Field), string> _objectKeys = new();
    private List<TypeAst> _selectorContext = new();
    private int _validatingGovernor;

    private sealed record Scope(string Module, Dictionary<string, Argument>? Arguments = null, bool QualifyReferences = false);
    private sealed record Argument(IReadOnlyList<Token> Tokens, Scope Scope);
    private sealed record Symbol(string Module, TypeAssignmentAst? Type, ValueAssignmentAst? Value);
    private sealed record FieldContent(IReadOnlyList<Token> Tokens, Scope Scope);
    private sealed class ResolvedObject
    {
        public string Name { get; init; } = "";
        public InformationClassAst Class { get; init; } = null!;
        public Dictionary<string, FieldContent> Fields { get; } = new(StringComparer.Ordinal);
    }
    private sealed class ResolvedSet
    {
        public bool Extensible { get; set; }
        public List<ResolvedObject> Objects { get; } = new();
    }

    public InformationResolver(List<ModuleAst> modules)
    {
        _modules = new Dictionary<string, ModuleAst>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            if (!_modules.TryAdd(module.Name, module)) throw Error(module, $"Duplicate module '{module.Name}'.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in module.TypeAssignments.Cast<AstNode>().Concat(module.ValueAssignments))
            {
                var name = definition is TypeAssignmentAst type ? type.Name : ((ValueAssignmentAst)definition).Name;
                if (!names.Add(name)) throw Error(definition, $"Duplicate {(definition is ValueAssignmentAst ? "value" : "type")} assignment '{name}'.");
            }
            _output.Add(module.Name, new ModuleAst
            {
                Name = module.Name, Oid = module.Oid, Source = module.Source, TagDefault = module.TagDefault,
                Line = module.Line, Column = module.Column
            });
            foreach (var definition in module.TypeAssignments)
                if (definition.Type is InformationClassAst cls) _classModules.Add(cls, module.Name);
        }
        _typeIdentifier = new InformationClassAst();
        _typeIdentifier.Fields.Add(new InformationFieldAst { Name = "id", Governor = new BuiltinTypeAst("OBJECT IDENTIFIER"), Unique = true });
        _typeIdentifier.Fields.Add(new InformationFieldAst { Name = "Type" });
        _typeIdentifier.Syntax = new List<SyntaxElementAst>
        {
            new() { Field = "Type" }, new() { Literal = "IDENTIFIED" }, new() { Literal = "BY" }, new() { Field = "id" }
        };
        ResolveModuleIdentities();
    }

    private void ResolveModuleIdentities()
    {
        foreach (var module in _modules.Values)
        {
            for (var i = 0; i < module.Imports.Count; i++)
            {
                var import = module.Imports[i];
                if (_modules.ContainsKey(import.Module) || import.Oid is null || import.Oid.Arcs.Any(a => a.Number is null)) continue;
                var identity = string.Join(".", import.Oid.Arcs.Select(a => a.Number));
                var matches = _modules.Values.Where(m => m.Oid is not null && string.Join(".", m.Oid) == identity).ToArray();
                if (matches.Length != 1) continue;
                var resolved = new ImportAst { Module = matches[0].Name, Oid = import.Oid, Line = import.Line, Column = import.Column };
                resolved.Types.AddRange(import.Types); resolved.Values.AddRange(import.Values);
                module.Imports[i] = resolved;
            }
        }
    }

    public List<ModuleAst> Resolve()
    {
        foreach (var module in _modules.Values)
        {
            var scope = new Scope(module.Name);
            foreach (var import in module.Imports)
            {
                if (!_modules.ContainsKey(import.Module)) throw Error(import, $"Imported module '{import.Module}' was not found among compiled modules.");
                foreach (var name in import.Types.Concat(import.Values)) Find(scope, name, import.Module, import);
            }
            foreach (var definition in module.TypeAssignments)
            {
                if (definition.Parameters.Count > 0 || ClassOf(definition.Type, scope) is not null) continue;
                _output[module.Name].TypeAssignments.Add(new TypeAssignmentAst
                {
                    Name = definition.Name, Type = NormalizeType(definition.Type, scope),
                    Line = definition.Line, Column = definition.Column
                });
            }
            foreach (var definition in module.ValueAssignments)
            {
                var governor = ClassOf(definition.Type, scope);
                if (governor is not null)
                {
                    if (char.IsUpper(definition.Name[0])) ResolveSet(new RawValueAst { Tokens = ValueTokens(definition.Value), Line = definition.Line, Column = definition.Column }, governor, scope);
                    else ResolveObject(definition.Name, governor, ValueTokens(definition.Value), scope, definition);
                    continue;
                }
                var type = NormalizeType(definition.Type, scope);
                if (char.IsUpper(definition.Name[0]) && definition.Value is RawValueAst) continue;
                _output[module.Name].ValueAssignments.Add(new ValueAssignmentAst
                {
                    Name = definition.Name, Type = type, Value = NormalizeValue(definition.Value, type, scope),
                    Line = definition.Line, Column = definition.Column
                });
            }
        }
        foreach (var module in _modules.Values)
        {
            foreach (var import in module.Imports)
            {
                foreach (var name in import.Types.Concat(import.Values))
                {
                    var symbol = Find(new Scope(module.Name), name, import.Module, import);
                    var source = _output[symbol.Module];
                    var isType = source.TypeAssignments.Any(t => t.Name == name);
                    var isValue = source.ValueAssignments.Any(v => v.Name == name);
                    if (!isType && !isValue) continue;
                    var filtered = _output[module.Name].Imports.SingleOrDefault(i => i.Module == symbol.Module);
                    if (filtered is null)
                    {
                        filtered = new ImportAst { Module = symbol.Module, Line = import.Line, Column = import.Column };
                        _output[module.Name].Imports.Add(filtered);
                    }
                    var names = isType ? filtered.Types : filtered.Values;
                    if (!names.Contains(name)) names.Add(name);
                }
            }
        }
        return _output.Values.ToList();
    }

    private Symbol Find(Scope scope, string name, string? explicitModule, AstNode position, HashSet<string>? visited = null)
    {
        if (explicitModule is not null)
        {
            visited ??= new HashSet<string>();
            if (!visited.Add(explicitModule + "." + name)) throw Error(position, $"Cyclic import of '{name}'.");
            if (!_modules.TryGetValue(explicitModule, out var module)) throw Error(position, $"Unknown module '{explicitModule}'.");
            var type = module.TypeAssignments.SingleOrDefault(t => t.Name == name);
            var value = module.ValueAssignments.SingleOrDefault(t => t.Name == name);
            if (type is null && value is null)
            {
                var reexports = module.Imports.Where(i => i.Types.Contains(name) || i.Values.Contains(name)).Select(i => i.Module).Distinct().ToArray();
                if (reexports.Length == 1) return Find(new Scope(explicitModule), name, reexports[0], position, visited);
                throw Error(position, $"Imported {(char.IsUpper(name[0]) ? "type" : "value")} '{name}' from '{explicitModule}' was not found in module '{explicitModule}'.");
            }
            return new Symbol(explicitModule, type, value);
        }
        var local = _modules[scope.Module];
        var localType = local.TypeAssignments.SingleOrDefault(t => t.Name == name);
        var localValue = local.ValueAssignments.SingleOrDefault(t => t.Name == name);
        if (localType is not null || localValue is not null) return new Symbol(scope.Module, localType, localValue);
        var imports = local.Imports.Where(i => i.Types.Contains(name) || i.Values.Contains(name)).Select(i => i.Module).Distinct().ToArray();
        if (imports.Length > 1) throw Error(position, $"Ambiguous imported symbol '{name}'; use a module-qualified reference.");
        if (imports.Length == 1) return Find(scope, name, imports[0], position, visited);
        throw Error(position, $"Unknown symbol '{name}' in module '{scope.Module}'.");
    }

    private InformationClassAst? ClassOf(TypeAst type, Scope scope, HashSet<string>? visited = null)
    {
        if (type is InformationClassAst cls) return cls;
        if (type is not TypeReferenceAst reference || reference.Arguments is not null) return null;
        if (scope.Arguments?.TryGetValue(reference.Name, out var argument) == true && reference.Module is null)
        {
            var parsed = Asn1Parser.ReadType(argument.Tokens, out _);
            return ClassOf(parsed, argument.Scope, visited);
        }
        if (reference.Name == "TYPE-IDENTIFIER" && reference.Module is null) return _typeIdentifier;
        var symbol = Find(scope, reference.Name, reference.Module, reference);
        if (symbol.Type is null) return null;
        visited ??= new HashSet<string>(StringComparer.Ordinal);
        if (!visited.Add(symbol.Module + "." + reference.Name)) throw Error(reference, $"Cyclic class/type alias '{reference.Name}'.");
        return ClassOf(symbol.Type.Type, new Scope(symbol.Module), visited);
    }

    private TypeAst NormalizeType(TypeAst type, Scope scope)
    {
        var construction = type is SequenceTypeAst or SetTypeAst or ChoiceTypeAst or SequenceOfTypeAst or SetOfTypeAst;
        if (construction) _selectorContext.Add(type);
        try { return NormalizeTypeCore(type, scope); }
        finally { if (construction) _selectorContext.RemoveAt(_selectorContext.Count - 1); }
    }

    private TypeAst NormalizeDefinition(TypeAst type, Scope scope)
    {
        var previous = _selectorContext;
        _selectorContext = new List<TypeAst>();
        try { return NormalizeType(type, scope); }
        finally { _selectorContext = previous; }
    }

    private TypeAst NormalizeTypeCore(TypeAst type, Scope scope)
    {
        TypeAst result;
        switch (type)
        {
            case TaggedTypeAst tagged:
                result = new TaggedTypeAst(tagged.Tag, NormalizeType(tagged.Inner, scope)); break;
            case TypeReferenceAst reference:
                if (reference.Module is null && scope.Arguments?.TryGetValue(reference.Name, out var argument) == true)
                {
                    var parsed = Asn1Parser.ReadType(argument.Tokens, out var consumed);
                    if (consumed != argument.Tokens.Count) throw Error(reference, $"Invalid type argument '{reference.Name}'.");
                    result = Qualify(NormalizeType(parsed, argument.Scope with { QualifyReferences = true }), argument.Scope.Module);
                    result.TagDefaultOverride = _modules[argument.Scope.Module].TagDefault;
                }
                else if (reference.Arguments is not null) result = Specialize(reference, scope);
                else
                {
                    var symbol = Find(scope, reference.Name, reference.Module, reference);
                    if (symbol.Type is null) throw Error(reference, $"Unknown type '{reference.Name}'; symbol is a value or object set.");
                    if (symbol.Type?.Parameters.Count > 0) throw Error(reference, $"Template '{reference.Name}' requires actual arguments.");
                    var defining = reference.Module is null && !scope.QualifyReferences ? null : symbol.Module;
                    result = new TypeReferenceAst(reference.Name, defining);
                }
                break;
            case ObjectFieldTypeAst field: result = ResolveFieldType(field, scope); break;
            case InstanceOfTypeAst instance:
                var cls = ClassOf(instance.Class, scope) ?? throw Error(type, "INSTANCE OF requires an object class.");
                if (cls.Fields.Count != 2 || !cls.Fields.Any(f => f.Name == "id" && f.Unique) || !cls.Fields.Any(f => f.Name == "Type" && f.Governor is null))
                    throw Error(type, "INSTANCE OF requires a TYPE-IDENTIFIER-shaped class.");
                var instanceValue = new SequenceTypeAst();
                instanceValue.Fields.Add(new FieldAst { Name = "type-id", Type = new BuiltinTypeAst("OBJECT IDENTIFIER") });
                instanceValue.Fields.Add(new FieldAst { Name = "value", Type = new TaggedTypeAst(new TagAst { Number = 0, Mode = "explicit" }, new AnyTypeAst { TableExtensible = true }) });
                result = new TaggedTypeAst(new TagAst { Class = "universal", Number = 8, Mode = "implicit" }, instanceValue);
                break;
            case SequenceTypeAst sequence:
                var seq = new SequenceTypeAst { Extensible = sequence.Extensible };
                NormalizeFields(sequence.Fields, seq.Fields, scope); result = seq; break;
            case SetTypeAst set:
                var normalizedSet = new SetTypeAst { Extensible = set.Extensible };
                NormalizeFields(set.Fields, normalizedSet.Fields, scope); result = normalizedSet; break;
            case ChoiceTypeAst choice:
                var normalizedChoice = new ChoiceTypeAst { Extensible = choice.Extensible };
                NormalizeFields(choice.Fields, normalizedChoice.Fields, scope); result = normalizedChoice; break;
            case SequenceOfTypeAst of: result = new SequenceOfTypeAst(NormalizeType(of.Element, scope)); break;
            case SetOfTypeAst of: result = new SetOfTypeAst(NormalizeType(of.Element, scope)); break;
            case ContainingTypeAst contained:
                result = new ContainingTypeAst { Outer = NormalizeType(contained.Outer, scope), Inner = NormalizeType(contained.Inner, scope) }; break;
            case BuiltinTypeAst builtin: result = new BuiltinTypeAst(builtin.Name, builtin.NamedNumbers); break;
            case BitStringTypeAst bits: result = new BitStringTypeAst(bits.NamedBits); break;
            case EnumeratedTypeAst enumeration: result = new EnumeratedTypeAst(enumeration.Values) { Extensible = enumeration.Extensible }; break;
            case StringTypeAst text: result = new StringTypeAst(text.StringType); break;
            case TimeTypeAst time: result = new TimeTypeAst(time.TimeType); break;
            case AnyTypeAst any: result = new AnyTypeAst(any.DefinedBy) { Selector = any.Selector, TableExtensible = any.TableExtensible, Bindings = any.Bindings }; break;
            default: result = type; break;
        }
        if (!ReferenceEquals(result, type)) { result.Line = type.Line; result.Column = type.Column; }
        if (type.TagDefaultOverride.HasValue) result.TagDefaultOverride = type.TagDefaultOverride;
        if (type.Constraint is not null) result.Constraint = NormalizeConstraint(type.Constraint, scope);
        return result;
    }

    private ConstraintAst NormalizeConstraint(ConstraintAst constraint, Scope scope) => new()
    {
        HasSize = constraint.HasSize, HasValue = constraint.HasValue, Unsupported = constraint.Unsupported,
        SizeMin = NormalizeBound(constraint.SizeMin, scope), SizeMax = NormalizeBound(constraint.SizeMax, scope),
        ValueMin = NormalizeBound(constraint.ValueMin, scope), ValueMax = NormalizeBound(constraint.ValueMax, scope),
        Line = constraint.Line, Column = constraint.Column
    };

    private BoundAst? NormalizeBound(BoundAst? bound, Scope scope)
    {
        if (bound?.Reference is null) return bound;
        if (scope.Arguments?.TryGetValue(bound.Reference, out var argument) == true)
        {
            var parsed = Asn1Parser.ReadValue(argument.Tokens, null, out var consumed);
            if (consumed != argument.Tokens.Count) throw Error(bound, "Invalid constraint bound argument.");
            if (parsed is IntegerValueAst integer) return new BoundAst { Number = integer.Value, Line = bound.Line, Column = bound.Column };
            if (parsed is ValueReferenceAst reference)
            {
                var symbol = Find(argument.Scope, reference.Name, reference.Module, reference);
                return new BoundAst { Reference = reference.Name, Module = symbol.Module, Line = bound.Line, Column = bound.Column };
            }
            throw Error(bound, "Constraint bound argument must be INTEGER.");
        }
        if (_modules[scope.Module].ValueAssignments.Any(v => v.Name == bound.Reference) || _modules[scope.Module].Imports.Any(i => i.Values.Contains(bound.Reference)))
        {
            var symbol = Find(scope, bound.Reference, bound.Module, bound);
            return new BoundAst { Reference = bound.Reference, Module = symbol.Module, Line = bound.Line, Column = bound.Column };
        }
        return bound;
    }

    private void NormalizeFields(List<FieldAst> fields, List<FieldAst> result, Scope scope)
    {
        foreach (var field in fields)
        {
            var type = NormalizeType(field.Type, scope);
            result.Add(new FieldAst
            {
                Name = field.Name, Type = type, Optional = field.Optional,
                Default = field.Default is null ? null : NormalizeValue(field.Default, type, scope),
                ExtensionAddition = field.ExtensionAddition, ExtensionGroup = field.ExtensionGroup,
                Line = field.Line, Column = field.Column
            });
        }
    }

    private TypeAst Specialize(TypeReferenceAst reference, Scope caller)
    {
        var symbol = Find(caller, reference.Name, reference.Module, reference);
        var template = symbol.Type ?? throw Error(reference, $"'{reference.Name}' is not a type template.");
        if (template.Parameters.Count != reference.Arguments!.Count)
            throw Error(reference, $"Template '{reference.Name}' expects {template.Parameters.Count} arguments, got {reference.Arguments.Count}.");
        var arguments = new Dictionary<string, Argument>(StringComparer.Ordinal);
        for (var i = 0; i < template.Parameters.Count; i++)
            arguments.Add(template.Parameters[i].Name, ExpandArgument(reference.Arguments[i], caller));
        string ArgumentKey(FormalParameterAst parameter)
        {
            var actual = arguments[parameter.Name];
            var prefix = "";
            if (parameter.Governor is null)
            {
                var parsed = Asn1Parser.ReadType(actual.Tokens, out var consumed);
                if (consumed != actual.Tokens.Count) throw Error(reference, $"Invalid type argument '{parameter.Name}'.");
                if (parsed is TaggedTypeAst or SequenceTypeAst or SetTypeAst or ChoiceTypeAst or SequenceOfTypeAst or SetOfTypeAst or ContainingTypeAst)
                    prefix = _modules[actual.Scope.Module].TagDefault + ":";
            }
            return prefix + TokensText(QualifiedTokens(actual.Tokens, actual.Scope));
        }
        var key = symbol.Module + "." + reference.Name + "{" + string.Join(",", template.Parameters.Select(p =>
            ArgumentKey(p))) + "}";
        if (_specializations.TryGetValue(key, out var existing)) return new TypeReferenceAst(existing, symbol.Module);
        if (_specializations.Count >= 512 || _active.Count >= 128) throw Error(reference, "Parameterized specialization limit exceeded (possible expanding recursion).");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 16);
        var name = reference.Name + "-" + hash;
        _specializations.Add(key, name);
        var scope = new Scope(symbol.Module, arguments);
        // Validate governors before constructing the specialization.
        foreach (var parameter in template.Parameters)
        {
            if (parameter.Governor is null)
            {
                var actual = arguments[parameter.Name];
                try
                {
                    var parsed = Asn1Parser.ReadType(actual.Tokens, out _);
                    if (ClassOf(parsed, actual.Scope) is null) NormalizeDefinition(parsed, actual.Scope);
                }
                catch (CompileException error)
                { throw new CompileException($"Invalid type argument '{parameter.Name}': {error.Detail}", error.Line, error.Column, error.SourceFile); }
                continue;
            }
            var governor = Asn1Parser.ReadType(parameter.Governor, out _);
            var cls = ClassOf(governor, scope);
            if (cls is not null)
                ResolveSet(new RawValueAst { Tokens = arguments[parameter.Name].Tokens }, cls, arguments[parameter.Name].Scope);
            else
            {
                var actual = arguments[parameter.Name];
                var declared = NormalizeType(governor, scope);
                var value = Asn1Parser.ReadValue(actual.Tokens, EffectiveType(declared, scope), out var consumed);
                if (consumed != actual.Tokens.Count) throw Error(reference, $"Invalid value argument '{parameter.Name}'.");
                ValidateGovernedValue(value, Qualify(declared, scope.Module), actual.Scope);
            }
        }
        _active.Add(key);
        try
        {
            var normalized = NormalizeDefinition(template.Type, scope);
            _output[symbol.Module].TypeAssignments.Add(new TypeAssignmentAst { Name = name, Type = normalized, Line = reference.Line, Column = reference.Column });
        }
        finally { _active.Remove(key); }
        return new TypeReferenceAst(name, symbol.Module);
    }

    private Argument ExpandArgument(IReadOnlyList<Token> tokens, Scope scope)
    {
        if (tokens.Count == 1 && scope.Arguments?.TryGetValue(tokens[0].Text, out var argument) == true) return argument;
        return new Argument(ExpandTokens(tokens, scope), scope);
    }

    private IReadOnlyList<Token> ExpandTokens(IReadOnlyList<Token> tokens, Scope scope)
    {
        var result = new List<Token>();
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Identifier && scope.Arguments?.TryGetValue(token.Text, out var argument) == true)
                result.AddRange(QualifiedTokens(argument.Tokens, argument.Scope));
            else result.Add(token);
        }
        return result;
    }

    private IReadOnlyList<Token> QualifiedTokens(IReadOnlyList<Token> tokens, Scope scope)
    {
        var result = new List<Token>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.Identifier && (i == 0 || tokens[i - 1].Kind != TokenKind.Dot) &&
                (i + 1 == tokens.Count || tokens[i + 1].Kind != TokenKind.Dot) &&
                (_modules[scope.Module].TypeAssignments.Any(t => t.Name == token.Text) || _modules[scope.Module].ValueAssignments.Any(v => v.Name == token.Text) ||
                 _modules[scope.Module].Imports.Any(i => i.Types.Contains(token.Text) || i.Values.Contains(token.Text))))
            {
                var symbol = Find(scope, token.Text, null, new RawValueAst { Line = token.Line, Column = token.Column });
                result.Add(new Token(TokenKind.Identifier, symbol.Module, token.Line, token.Column));
                result.Add(new Token(TokenKind.Dot, ".", token.Line, token.Column));
            }
            result.Add(token);
        }
        return result;
    }

    private TypeAst ResolveFieldType(ObjectFieldTypeAst field, Scope scope)
    {
        var cls = ClassOf(new TypeReferenceAst(field.Owner, field.Module), scope);
        if (cls is null)
        {
            var content = ObjectField(field.Owner, field.Module, field.Fields, scope, field);
            var parsed = Asn1Parser.ReadType(content.Tokens, out var consumed);
            if (consumed != content.Tokens.Count) throw Error(field, "Object field does not contain a type.");
            return Qualify(NormalizeType(parsed, content.Scope), content.Scope.Module);
        }
        var definition = cls.Fields.SingleOrDefault(f => f.Name == field.Fields[0]) ?? throw Error(field, $"Unknown class field '&{field.Fields[0]}'.");
        var classScope = ClassScope(cls, scope);
        if (definition.Governor is not null && ClassOf(definition.Governor, classScope) is null)
        {
            var fixedType = Qualify(NormalizeType(definition.Governor, classScope), classScope.Module);
            if (field.Table is not null)
            {
                ResolveSet(new RawValueAst { Tokens = ExpandTokens(field.Table, scope) }, cls, scope);
                fixedType.Constraint ??= new ConstraintAst();
                fixedType.Constraint.Unsupported = (fixedType.Constraint.Unsupported ?? "") + " table(" + TokensText(field.Table) + ")";
            }
            return fixedType;
        }
        if (definition.Governor is not null) throw Error(field, "Object/object-set field used as a type is outside the Asn1Kit compiler profile.");
        var any = new AnyTypeAst
        {
            Selector = NormalizeSelector(field, scope), TableExtensible = true, Bindings = new List<OpenTypeBindingAst>(),
            Line = field.Line, Column = field.Column
        };
        if (field.Table is null) return any;
        var set = ResolveSet(new RawValueAst { Tokens = ExpandTokens(field.Table, scope) }, cls, scope);
        any.TableExtensible = set.Extensible;
        var uniqueFields = cls.Fields.Where(f => f.Unique).ToArray();
        if (uniqueFields.Length != 1) throw Error(field, "Open-type table requires exactly one UNIQUE class field.");
        var unique = uniqueFields[0];
        foreach (var obj in set.Objects)
        {
            if (!obj.Fields.TryGetValue(definition.Name, out var content)) continue;
            var key = ObjectKey(obj, unique);
            var parsed = Asn1Parser.ReadType(content.Tokens, out var consumed);
            if (consumed != content.Tokens.Count) throw Error(field, $"Invalid type field '&{definition.Name}'.");
            any.Bindings.Add(new OpenTypeBindingAst
            {
                Key = key, Name = obj.Name, Module = content.Scope.Module, Type = Qualify(NormalizeDefinition(parsed, content.Scope), content.Scope.Module)
            });
        }
        if (any.Bindings.Count > 0 && any.Selector is null)
            throw Error(field, "A typed open-type table requires a component relation selector.");
        return any;
    }

    private IrOpenTypeSelector? NormalizeSelector(ObjectFieldTypeAst field, Scope scope)
    {
        if (field.Selector is null) return null;
        var nearest = _selectorContext.FindLastIndex(t => t is SequenceTypeAst or SetTypeAst);
        if (nearest < 0) throw Error(field, "Component relation requires an enclosing SEQUENCE or SET.");
        var target = field.SelectorOutermost
            ? _selectorContext.FindIndex(t => t is SequenceTypeAst or SetTypeAst or ChoiceTypeAst)
            : nearest - field.Selector.Levels;
        if (target < 0 || _selectorContext[target] is not (SequenceTypeAst or SetTypeAst or ChoiceTypeAst))
            throw Error(field, "Component relation selector exceeds its enclosing structures.");
        var selected = _selectorContext[target];
        foreach (var part in field.Selector.Path)
        {
            selected = SelectorType(selected, ref scope);
            var components = selected switch
            {
                SequenceTypeAst sequence => sequence.Fields,
                SetTypeAst set => set.Fields,
                ChoiceTypeAst choice => choice.Fields,
                _ => throw Error(field, $"Selector path cannot traverse '{part}'.")
            };
            selected = components.SingleOrDefault(c => c.Name == part)?.Type
                ?? throw Error(field, $"Unknown selector component '{part}'.");
        }
        selected = SelectorType(selected, ref scope);
        if (selected is not BuiltinTypeAst { Name: "OBJECT IDENTIFIER" or "INTEGER" })
            throw Error(field, "Component relation selector must select OBJECT IDENTIFIER or INTEGER.");
        return new IrOpenTypeSelector
        {
            Levels = _selectorContext.Skip(target + 1).Count(t => t is SequenceTypeAst or SetTypeAst or ChoiceTypeAst),
            Path = new List<string>(field.Selector.Path)
        };
    }

    private TypeAst SelectorType(TypeAst type, ref Scope scope)
    {
        var visited = new HashSet<string>();
        while (true)
        {
            if (type is TaggedTypeAst tagged) { type = tagged.Inner; continue; }
            if (type is ObjectFieldTypeAst field) { type = ResolveFieldType(field, scope); continue; }
            if (type is not TypeReferenceAst reference) return type;
            if (reference.Arguments is not null) { type = Specialize(reference, scope); continue; }
            if (reference.Module is null && scope.Arguments?.TryGetValue(reference.Name, out var argument) == true)
            { type = Asn1Parser.ReadType(argument.Tokens, out _); scope = argument.Scope; continue; }
            var module = reference.Module ?? scope.Module;
            if (!visited.Add(module + "." + reference.Name)) throw Error(type, "Cyclic selector type alias.");
            var specialized = _output[module].TypeAssignments.SingleOrDefault(t => t.Name == reference.Name);
            if (specialized is not null) { type = specialized.Type; scope = new Scope(module); continue; }
            var symbol = Find(scope, reference.Name, reference.Module, reference);
            type = symbol.Type?.Type ?? throw Error(type, "Selector path requires a type reference.");
            scope = new Scope(symbol.Module);
        }
    }

    private ResolvedObject ResolveObject(string name, InformationClassAst cls, IReadOnlyList<Token> tokens, Scope scope, AstNode position)
    {
        var cacheKey = scope.Module + "." + name;
        if (name.Length > 0 && _objects.TryGetValue(cacheKey, out var cached))
        {
            if (!ReferenceEquals(cached.Class, cls)) throw Error(position, $"Object '{name}' belongs to a different class.");
            return cached;
        }
        if (!IsBraceGroup(tokens))
        {
            var content = ResolveReference(tokens, scope, position);
            if (!ReferenceEquals(content.Class, cls)) throw Error(position, "Object belongs to a different class.");
            return ResolveObject(content.Name, cls, content.Tokens, content.Scope, position);
        }
        var cycleKey = "object:" + cacheKey;
        if (!_active.Add(cycleKey)) throw Error(position, $"Cyclic information object '{name}'.");
        try
        {
            var result = new ResolvedObject { Name = name, Class = cls };
            var body = tokens.Skip(1).Take(tokens.Count - 2).ToArray();
            if (body.Length > 0 && body[0].Kind == TokenKind.Ampersand)
            {
                foreach (var group in Asn1Parser.SplitTopLevel(body, TokenKind.Comma))
                {
                    if (group.Count < 3 || group[0].Kind != TokenKind.Ampersand) throw Error(position, "Invalid default object syntax.");
                    if (!result.Fields.TryAdd(group[1].Text, new FieldContent(group.Skip(2).ToArray(), scope)))
                        throw Error(position, $"Duplicate object field '&{group[1].Text}'.");
                }
            }
            else
            {
                if (cls.Syntax is null) throw Error(position, "Object class has no WITH SYNTAX template; use default object syntax.");
                var index = 0;
                MatchSyntax(cls.Syntax, cls, body, ref index, result, scope, position);
                if (index != body.Length) throw Error(position, $"Unexpected token '{body[index].Text}' in information object.");
            }
            foreach (var field in cls.Fields)
            {
                if (!result.Fields.ContainsKey(field.Name))
                {
                    if (field.Default is not null) result.Fields.Add(field.Name, new FieldContent(ValueTokens(field.Default), ClassScope(cls, scope)));
                    else if (!field.Optional) throw Error(position, $"Information object '{name}' is missing required field '&{field.Name}'.");
                }
            }
            foreach (var (fieldName, content) in result.Fields)
            {
                var field = cls.Fields.SingleOrDefault(f => f.Name == fieldName) ?? throw Error(position, $"Unknown class field '&{fieldName}'.");
                ValidateObjectField(field, content, ClassScope(cls, scope), position);
            }
            if (name.Length > 0) _objects[cacheKey] = result;
            return result;
        }
        finally { _active.Remove(cycleKey); }
    }

    private void MatchSyntax(List<SyntaxElementAst> syntax, InformationClassAst cls, IReadOnlyList<Token> tokens,
        ref int index, ResolvedObject result, Scope scope, AstNode position)
    {
        foreach (var element in syntax)
        {
            if (element.OptionalGroup is { } group)
            {
                var first = FirstLiteral(group);
                if (index >= tokens.Count || (first is not null && tokens[index].Text != first)) continue;
                MatchSyntax(group, cls, tokens, ref index, result, scope, position);
            }
            else if (element.Literal is { } literal)
            {
                if (index >= tokens.Count || tokens[index].Text != literal) throw Error(position, $"Expected '{literal}' in WITH SYNTAX object.");
                index++;
            }
            else
            {
                var field = cls.Fields.SingleOrDefault(f => f.Name == element.Field) ?? throw Error(element, $"Unknown syntax field '&{element.Field}'.");
                if (index >= tokens.Count) throw Error(position, $"Missing syntax field '&{field.Name}'.");
                var remaining = tokens.Skip(index).ToArray();
                int consumed;
                if (remaining[0].Kind == TokenKind.LBrace) consumed = GroupLength(remaining, position);
                else if (field.Governor is null) Asn1Parser.ReadType(remaining, out consumed);
                else if (ClassOf(field.Governor, ClassScope(cls, scope)) is not null) consumed = ReferenceLength(remaining);
                else Asn1Parser.ReadValue(remaining, null, out consumed);
                if (consumed == 0 || !result.Fields.TryAdd(field.Name, new FieldContent(remaining.Take(consumed).ToArray(), scope)))
                    throw Error(position, $"Duplicate or empty syntax field '&{field.Name}'.");
                index += consumed;
            }
        }
    }

    private static string? FirstLiteral(List<SyntaxElementAst> syntax) =>
        syntax.Select(e => e.Literal ?? (e.OptionalGroup is null ? null : FirstLiteral(e.OptionalGroup))).FirstOrDefault(s => s is not null);

    private void ValidateObjectField(InformationFieldAst field, FieldContent content, Scope classScope, AstNode position)
    {
        if (field.Governor is null)
        {
            var type = Asn1Parser.ReadType(content.Tokens, out var consumed);
            if (consumed != content.Tokens.Count) throw Error(position, $"Invalid type field '&{field.Name}'.");
            NormalizeType(type, content.Scope);
            return;
        }
        var cls = ClassOf(field.Governor, classScope);
        if (cls is not null)
        {
            if (char.IsUpper(field.Name[0])) ResolveSet(new RawValueAst { Tokens = content.Tokens }, cls, content.Scope);
            else ResolveObject("", cls, content.Tokens, content.Scope, position);
        }
        else if (!char.IsUpper(field.Name[0]))
        {
            var type = Qualify(NormalizeType(field.Governor, classScope), classScope.Module);
            var value = Asn1Parser.ReadValue(content.Tokens, EffectiveType(type, classScope), out var consumed);
            if (consumed != content.Tokens.Count) throw Error(position, $"Invalid value field '&{field.Name}'.");
            ValidateGovernedValue(value, type, content.Scope);
        }
        // Fixed-type value-set fields are retained in the semantic object, without runtime enforcement.
    }

    private ResolvedSet ResolveSet(RawValueAst value, InformationClassAst cls, Scope scope)
    {
        var tokens = value.Tokens;
        if (tokens.Count == 1 && scope.Arguments?.TryGetValue(tokens[0].Text, out var argument) == true)
            return ResolveSet(new RawValueAst { Tokens = argument.Tokens }, cls, argument.Scope);
        if (!IsBraceGroup(tokens))
        {
            var reference = ResolveReference(tokens, scope, value);
            if (!ReferenceEquals(reference.Class, cls)) throw Error(value, "Object set belongs to a different class.");
            var key = reference.Scope.Module + "." + reference.Name;
            if (_sets.TryGetValue(key, out var cached))
            {
                if (cached.Objects.Any(o => !ReferenceEquals(o.Class, cls))) throw Error(value, "Object set belongs to a different class.");
                return cached;
            }
            if (!_active.Add("set:" + key)) throw Error(value, $"Cyclic object set '{reference.Name}'.");
            try
            {
                var result = ResolveSet(new RawValueAst { Tokens = reference.Tokens, Line = value.Line, Column = value.Column }, cls, reference.Scope);
                _sets[key] = result; return result;
            }
            finally { _active.Remove("set:" + key); }
        }
        var body = tokens.Skip(1).Take(tokens.Count - 2).ToArray();
        // {{Set}} is the actual-parameter wrapper around an object-set expression.
        if (body.Length > 0 && IsBraceGroup(body)) return ResolveSet(new RawValueAst { Tokens = body }, cls, scope);
        var output = new ResolvedSet();
        var groups = SplitSet(body);
        foreach (var group in groups)
        {
            if (group.Count == 1 && group[0].Kind == TokenKind.Ellipsis) { output.Extensible = true; continue; }
            if (IsBraceGroup(group)) output.Objects.Add(ResolveObject("", cls, group, scope, value));
            else
            {
                var reference = ResolveReference(group, scope, value);
                if (char.IsUpper(reference.Name.FirstOrDefault()))
                {
                    var nested = ResolveSet(new RawValueAst { Tokens = group }, cls, scope);
                    output.Objects.AddRange(nested.Objects); output.Extensible |= nested.Extensible;
                }
                else output.Objects.Add(ResolveObject(reference.Name, cls, reference.Tokens, reference.Scope, value));
            }
        }
        foreach (var unique in cls.Fields.Where(f => f.Unique))
        {
            var keys = new Dictionary<string, ResolvedObject>(StringComparer.Ordinal);
            foreach (var obj in output.Objects)
            {
                if (!ReferenceEquals(obj.Class, cls)) throw Error(value, "Object set contains an object of a different class.");
                var key = ObjectKey(obj, unique);
                if (keys.TryGetValue(key, out var previous) && !ReferenceEquals(previous, obj))
                    throw Error(value, $"Conflicting UNIQUE field '&{unique.Name}' with key '{key}'.");
                keys[key] = obj;
            }
        }
        var distinct = output.Objects.Distinct().ToArray();
        output.Objects.Clear(); output.Objects.AddRange(distinct);
        return output;
    }

    private static List<List<Token>> SplitSet(IReadOnlyList<Token> tokens)
    {
        var groups = Asn1Parser.SplitTopLevel(tokens, TokenKind.Union);
        return groups.SelectMany(g => Asn1Parser.SplitTopLevel(g, TokenKind.Comma)).ToList();
    }

    private (string Name, IReadOnlyList<Token> Tokens, Scope Scope, InformationClassAst Class) ResolveReference(IReadOnlyList<Token> tokens, Scope scope, AstNode position)
    {
        if (tokens.Count == 0) throw Error(position, "Empty information reference.");
        if (tokens.Count == 1 && scope.Arguments?.TryGetValue(tokens[0].Text, out var argument) == true)
            return ResolveReference(argument.Tokens, argument.Scope, position);
        var name = tokens[0].Text;
        string? module = null;
        var index = 1;
        if (tokens.Count > 2 && tokens[1].Kind == TokenKind.Dot && tokens[2].Kind != TokenKind.Ampersand)
        { module = name; name = tokens[2].Text; index = 3; }
        var symbol = Find(scope, name, module, position);
        if (symbol.Value is null) throw Error(position, $"'{name}' is not an information object or object set.");
        var governor = ClassOf(symbol.Value.Type, new Scope(symbol.Module)) ?? throw Error(position, $"'{name}' is not governed by an object class.");
        var content = new FieldContent(ValueTokens(symbol.Value.Value), new Scope(symbol.Module));
        if (index < tokens.Count)
        {
            var fields = new List<string>();
            while (index < tokens.Count)
            {
                if (index + 2 >= tokens.Count || tokens[index].Kind != TokenKind.Dot || tokens[index + 1].Kind != TokenKind.Ampersand)
                    throw Error(position, "Invalid information field reference.");
                fields.Add(tokens[index + 2].Text); index += 3;
            }
            content = ObjectField(name, symbol.Module, fields, scope, position);
            foreach (var fieldName in fields)
            {
                var definition = governor.Fields.SingleOrDefault(f => f.Name == fieldName) ?? throw Error(position, $"Unknown information field '&{fieldName}'.");
                governor = definition.Governor is null ? throw Error(position, "Information reference does not select an object or object set.")
                    : ClassOf(definition.Governor, ClassScope(governor, content.Scope)) ?? throw Error(position, "Information reference does not select an object or object set.");
            }
            name = fields.Last();
        }
        return (name, content.Tokens, content.Scope, governor);
    }

    private FieldContent ObjectField(string owner, string? module, List<string> fields, Scope scope, AstNode position)
    {
        var symbol = Find(scope, owner, module, position);
        var value = symbol.Value ?? throw Error(position, $"'{owner}' is not an information object.");
        var cls = ClassOf(value.Type, new Scope(symbol.Module)) ?? throw Error(position, $"'{owner}' is not an information object.");
        var obj = ResolveObject(owner, cls, ValueTokens(value.Value), new Scope(symbol.Module), position);
        FieldContent content = null!;
        for (var i = 0; i < fields.Count; i++)
        {
            if (!obj.Fields.TryGetValue(fields[i], out content!)) throw Error(position, $"Object '{owner}' has no field '&{fields[i]}'.");
            if (i + 1 < fields.Count)
            {
                var def = cls.Fields.Single(f => f.Name == fields[i]);
                cls = ClassOf(def.Governor!, ClassScope(cls, content.Scope)) ?? throw Error(position, "Intermediate field is not an object.");
                obj = ResolveObject("", cls, content.Tokens, content.Scope, position);
            }
        }
        return content;
    }

    private string ObjectKey(ResolvedObject obj, InformationFieldAst unique)
    {
        if (_objectKeys.TryGetValue((obj, unique), out var cachedKey)) return cachedKey;
        if (!obj.Fields.TryGetValue(unique.Name, out var content))
            throw Error(unique, $"Information object '{obj.Name}' has no UNIQUE dispatch field '&{unique.Name}'.");
        var type = Qualify(NormalizeType(unique.Governor!, ClassScope(obj.Class, content.Scope)), ClassScope(obj.Class, content.Scope).Module);
        var value = NormalizeValue(new RawValueAst { Tokens = content.Tokens }, type, content.Scope);
        if (value is ValueReferenceAst reference)
        {
            var symbol = Find(content.Scope, reference.Name, reference.Module, reference);
            value = new ValueReferenceAst(reference.Name, symbol.Module);
        }
        var normalizedModules = _modules.Values.Select(m => new ModuleAst
        {
            Name = m.Name, TagDefault = m.TagDefault
        }).ToList();
        // OID chains and INTEGER values use the existing value resolver rather than a second codec.
        foreach (var module in normalizedModules)
        {
            var original = _modules[module.Name];
            module.Imports.AddRange(original.Imports.Select(i =>
            {
                var copy = new ImportAst { Module = i.Module };
                copy.Values.AddRange(i.Values.Where(n => _modules[i.Module].ValueAssignments.Any(v => v.Name == n && ClassOf(v.Type, new Scope(i.Module)) is null)));
                return copy;
            }));
            foreach (var definition in original.ValueAssignments)
            {
                if (ClassOf(definition.Type, new Scope(module.Name)) is not null) continue;
                var effective = ScalarType(definition.Type, new Scope(module.Name));
                if (effective is not null && !char.IsUpper(definition.Name[0]))
                    module.ValueAssignments.Add(new ValueAssignmentAst { Name = definition.Name, Type = effective, Value = NormalizeValue(definition.Value, effective, new Scope(module.Name)) });
            }
        }
        var temp = normalizedModules.Single(m => m.Name == content.Scope.Module);
        const string keyName = "asn1kit-internal-key";
        temp.ValueAssignments.Add(new ValueAssignmentAst { Name = keyName, Type = EffectiveType(type, content.Scope), Value = value });
        var ir = new IrBuilder(normalizedModules).Build().Modules.Single(m => m.Name == temp.Name).Values.Single(v => v.Name == keyName).Value;
        var key = ir switch
        {
            IrOidValue oid => oid.Value, IrIntegerValue integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            _ => throw Error(unique, "UNIQUE dispatch key must be OBJECT IDENTIFIER or INTEGER.")
        };
        _objectKeys.Add((obj, unique), key);
        return key;
    }

    private TypeAst EffectiveType(TypeAst type, Scope scope, HashSet<string>? visited = null)
    {
        if (type is TaggedTypeAst tagged) return EffectiveType(tagged.Inner, scope, visited);
        if (type is TypeReferenceAst reference)
        {
            if (_output.TryGetValue(reference.Module ?? scope.Module, out var output))
            {
                var specialized = output.TypeAssignments.SingleOrDefault(t => t.Name == reference.Name);
                if (specialized is not null) return EffectiveType(specialized.Type, new Scope(output.Name), visited);
            }
            var symbol = Find(scope, reference.Name, reference.Module, type);
            visited ??= new HashSet<string>(StringComparer.Ordinal);
            if (!visited.Add(symbol.Module + "." + reference.Name)) throw Error(type, "Cyclic value type alias.");
            return EffectiveType(NormalizeType(symbol.Type!.Type, new Scope(symbol.Module)), new Scope(symbol.Module), visited);
        }
        return type;
    }

    private BuiltinTypeAst? ScalarType(TypeAst type, Scope scope, HashSet<string>? visited = null)
    {
        if (type is BuiltinTypeAst { Name: "OBJECT IDENTIFIER" or "INTEGER" } builtin) return builtin;
        if (type is ObjectFieldTypeAst field)
        {
            var cls = ClassOf(new TypeReferenceAst(field.Owner, field.Module), scope);
            var definition = cls?.Fields.SingleOrDefault(f => f.Name == field.Fields[0]);
            if (definition?.Governor is not null) return ScalarType(definition.Governor, ClassScope(cls!, scope), visited);
            return null;
        }
        if (type is not TypeReferenceAst { Arguments: null } reference) return null;
        var symbol = Find(scope, reference.Name, reference.Module, type);
        if (symbol.Type is null) return null;
        visited ??= new HashSet<string>();
        if (!visited.Add(symbol.Module + "." + reference.Name)) return null;
        return ScalarType(symbol.Type.Type, new Scope(symbol.Module), visited);
    }

    private ValueAst NormalizeValue(ValueAst value, TypeAst type, Scope scope)
    {
        if (value is ValueReferenceAst { Module: null } parameter && scope.Arguments?.TryGetValue(parameter.Name, out var actual) == true)
        {
            var parsed = Asn1Parser.ReadValue(actual.Tokens, EffectiveType(type, scope), out var consumed);
            if (consumed != actual.Tokens.Count) throw Error(value, "Invalid value parameter.");
            return NormalizeValue(parsed, type, actual.Scope);
        }
        if (value is ValueReferenceAst reference &&
            (reference.Module is not null || _modules[scope.Module].ValueAssignments.Any(v => v.Name == reference.Name) ||
             _modules[scope.Module].Imports.Any(i => i.Values.Contains(reference.Name))))
        {
            var symbol = Find(scope, reference.Name, reference.Module, reference);
            if (_validatingGovernor > 0 && symbol.Value is not null)
            {
                var expected = EffectiveType(type, scope);
                var supplied = EffectiveType(NormalizeType(symbol.Value.Type, new Scope(symbol.Module)), new Scope(symbol.Module));
                var same = (expected, supplied) switch
                {
                    (BuiltinTypeAst left, BuiltinTypeAst right) => left.Name == right.Name,
                    (StringTypeAst left, StringTypeAst right) => left.StringType == right.StringType,
                    _ => expected.GetType() == supplied.GetType()
                };
                if (!same) throw Error(value, "Value reference is incompatible with its governing type.");
            }
            return new ValueReferenceAst(reference.Name, symbol.Module) { Line = value.Line, Column = value.Column };
        }
        if (value is TypedValueAst typed)
        {
            if (EffectiveType(type, scope) is not AnyTypeAst) throw Error(value, "Typed value requires an open governing type.");
            var concrete = NormalizeType(typed.Type, scope);
            return new TypedValueAst { Type = Qualify(concrete, scope.Module), Value = NormalizeValue(typed.Value, concrete, scope), Line = value.Line, Column = value.Column };
        }
        if (value is ObjectFieldValueAst objectField)
        {
            var content = ObjectField(objectField.Owner, objectField.Module, objectField.Fields, scope, value);
            return NormalizeValue(new RawValueAst { Tokens = content.Tokens }, type, content.Scope);
        }
        if (value is RawValueAst raw)
        {
            var effective = EffectiveType(type, scope);
            var parsed = Asn1Parser.ReadValue(raw.Tokens, effective, out var consumed);
            if (parsed is RawValueAst)
            {
                if (effective is BitStringTypeAst bits)
                {
                    if (raw.Tokens.Count < 2 || raw.Tokens[0].Kind != TokenKind.LBrace || raw.Tokens[^1].Kind != TokenKind.RBrace ||
                        raw.Tokens.Skip(1).Take(raw.Tokens.Count - 2).Where((_, i) => i % 2 == 0).Any(t => t.Kind != TokenKind.Identifier) ||
                        raw.Tokens.Skip(1).Take(raw.Tokens.Count - 2).Where((_, i) => i % 2 == 1).Any(t => t.Kind != TokenKind.Comma) ||
                        raw.Tokens.Count > 2 && raw.Tokens[^2].Kind == TokenKind.Comma)
                        throw Error(value, "Invalid named BIT STRING value.");
                    var names = raw.Tokens.Where(t => t.Kind == TokenKind.Identifier).Select(t => t.Text).ToArray();
                    var indices = names.Select(n => bits.NamedBits?.SingleOrDefault(b => b.Name == n)?.Value ?? throw Error(value, $"Unknown named bit '{n}'.")).ToArray();
                    var characters = new char[indices.Length == 0 ? 0 : checked((int)indices.Max() + 1)];
                    Array.Fill(characters, '0'); foreach (var index in indices) characters[index] = '1';
                    return new BStringValueAst(new string(characters));
                }
                throw Error(value, "Structured value form is outside the Asn1Kit compiler profile.");
            }
            if (consumed != raw.Tokens.Count) throw Error(value, "Unexpected trailing tokens in value.");
            return NormalizeValue(parsed, type, scope);
        }
        if (value is StructuredValueAst structured)
        {
            var effective = EffectiveType(type, scope);
            var fields = effective switch { SequenceTypeAst seq => seq.Fields, SetTypeAst set => set.Fields, _ => throw Error(value, "Structured value requires SEQUENCE or SET.") };
            var normalized = new StructuredValueAst { Line = value.Line, Column = value.Column };
            foreach (var (name, fieldValue) in structured.Fields)
            {
                var field = fields.SingleOrDefault(f => f.Name == name) ?? throw Error(value, $"Unknown value component '{name}'.");
                normalized.Fields.Add(name, NormalizeValue(fieldValue, field.Type, scope));
            }
            foreach (var field in fields.Where(f => !f.Optional && f.Default is null && !f.ExtensionAddition))
                if (!normalized.Fields.ContainsKey(field.Name)) throw Error(value, $"Missing value component '{field.Name}'.");
            return normalized;
        }
        if (value is CollectionValueAst collection)
        {
            var effective = EffectiveType(type, scope);
            var element = effective switch { SequenceOfTypeAst seq => seq.Element, SetOfTypeAst set => set.Element, _ => throw Error(value, "Collection value requires an OF type.") };
            var normalized = new CollectionValueAst();
            normalized.Items.AddRange(collection.Items.Select(v => NormalizeValue(v, element, scope))); return normalized;
        }
        if (value is ChoiceValueAst choice)
        {
            var effective = EffectiveType(type, scope) as ChoiceTypeAst ?? throw Error(value, "Choice value requires CHOICE.");
            var alternative = effective.Fields.SingleOrDefault(f => f.Name == choice.Alternative)
                ?? throw Error(value, $"Unknown CHOICE alternative '{choice.Alternative}'.");
            return new ChoiceValueAst { Alternative = choice.Alternative, Value = NormalizeValue(choice.Value, alternative.Type, scope), Line = value.Line, Column = value.Column };
        }
        if (_validatingGovernor == 0) return value;
        var effectiveType = EffectiveType(type, scope);
        var compatible = value switch
        {
            ValueReferenceAst named => effectiveType is EnumeratedTypeAst enumerated && enumerated.Values.Any(v => v.Name == named.Name) ||
                effectiveType is BuiltinTypeAst builtin && builtin.NamedNumbers?.Any(v => v.Name == named.Name) == true,
            IntegerValueAst => effectiveType is BuiltinTypeAst { Name: "INTEGER" } or EnumeratedTypeAst,
            BooleanValueAst => effectiveType is BuiltinTypeAst { Name: "BOOLEAN" },
            NullValueAst => effectiveType is BuiltinTypeAst { Name: "NULL" },
            OidValueAst => effectiveType is BuiltinTypeAst { Name: "OBJECT IDENTIFIER" },
            CStringValueAst => effectiveType is StringTypeAst or TimeTypeAst,
            BStringValueAst or HStringValueAst => effectiveType is BitStringTypeAst or BuiltinTypeAst { Name: "OCTET STRING" },
            _ => false
        };
        if (!compatible) throw Error(value, "Value is incompatible with its governing type.");
        return value;
    }

    private void ValidateGovernedValue(ValueAst value, TypeAst type, Scope scope)
    {
        _validatingGovernor++;
        try { NormalizeValue(value, type, scope); }
        finally { _validatingGovernor--; }
    }

    private TypeAst Qualify(TypeAst type, string module)
    {
        // Binding types are evaluated in their defining module, independently of the receiving module.
        if (type is TypeReferenceAst { Module: null } reference)
        {
            var defining = _output[module].TypeAssignments.Any(t => t.Name == reference.Name)
                ? module : Find(new Scope(module), reference.Name, null, reference).Module;
            return new TypeReferenceAst(reference.Name, defining) { Constraint = reference.Constraint, Line = type.Line, Column = type.Column };
        }
        return type;
    }

    private Scope ClassScope(InformationClassAst cls, Scope fallback) =>
        _classModules.TryGetValue(cls, out var module) ? new Scope(module) : fallback;

    private static bool IsBraceGroup(IReadOnlyList<Token> tokens) =>
        tokens.Count >= 2 && tokens[0].Kind == TokenKind.LBrace && GroupLength(tokens, new RawValueAst()) == tokens.Count;

    private static int GroupLength(IReadOnlyList<Token> tokens, AstNode position)
    {
        var depth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.LBrace) depth++;
            if (tokens[i].Kind == TokenKind.RBrace && --depth == 0) return i + 1;
        }
        throw Error(position, "Unterminated object/set group.");
    }

    private static int ReferenceLength(IReadOnlyList<Token> tokens)
    {
        var length = 1;
        while (length < tokens.Count && tokens[length].Kind == TokenKind.Dot)
        {
            length += 2;
            if (length <= tokens.Count && tokens[length - 1].Kind == TokenKind.Ampersand) length++;
        }
        return length;
    }

    private static IReadOnlyList<Token> ValueTokens(ValueAst value)
    {
        if (value is RawValueAst raw) return raw.Tokens;
        string text = value switch
        {
            IntegerValueAst integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            BooleanValueAst boolean => boolean.Value ? "TRUE" : "FALSE",
            NullValueAst => "NULL", CStringValueAst str => "\"" + str.Value.Replace("\"", "\"\"") + "\"",
            BStringValueAst bits => "'" + bits.Bits + "'B", HStringValueAst hex => "'" + hex.Hex + "'H",
            ValueReferenceAst reference => (reference.Module is null ? "" : reference.Module + ".") + reference.Name,
            ObjectFieldValueAst field => (field.Module is null ? "" : field.Module + ".") + field.Owner + ".&" + string.Join(".&", field.Fields),
            _ => throw Error(value, "Unsupported deferred value.")
        };
        return new Asn1Lexer(text).Tokenize().Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
    }

    private static string TokensText(IReadOnlyList<Token> tokens) => string.Join(" ", tokens.Select(t => t.Text));
    private static CompileException Error(AstNode position, string message) => new(message, position.Line, position.Column);
}

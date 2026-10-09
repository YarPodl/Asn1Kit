namespace Asn1Kit.Ir;

public sealed class IrException : Exception
{
    public IrException(string message) : base(message)
    {
    }
}

public static class IrValidator
{
    public static void Validate(IrDocument document)
    {
        if (document.IrVersion != 1)
        {
            throw new IrException($"Unsupported irVersion '{document.IrVersion}'. Expected 1.");
        }

        document.Modules ??= new List<IrModule>();
        var moduleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in document.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.Name))
            {
                throw new IrException("Module name is required.");
            }

            if (!moduleNames.Add(module.Name))
            {
                throw new IrException($"Duplicate module '{module.Name}'.");
            }

            ValidateModule(document, module);
        }
    }

    private static void ValidateModule(IrDocument document, IrModule module)
    {
        var tagDefault = module.TagDefault?.ToLowerInvariant();
        if (tagDefault is not (TagDefaults.Explicit or TagDefaults.Implicit or TagDefaults.Automatic))
        {
            throw new IrException($"Unknown tagDefault '{module.TagDefault}'.");
        }

        if (module.Oid is not null && string.IsNullOrWhiteSpace(module.Oid))
        {
            throw new IrException($"Module '{module.Name}' has an empty oid.");
        }

        module.Imports ??= new List<IrImport>();
        module.Types ??= new List<IrTypeDef>();
        module.Values ??= new List<IrValueDef>();

        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in module.Types)
        {
            if (string.IsNullOrWhiteSpace(type.Name))
            {
                throw new IrException($"Type name is required in module '{module.Name}'.");
            }

            if (!typeNames.Add(type.Name))
            {
                throw new IrException($"Duplicate type '{type.Name}' in module '{module.Name}'.");
            }

            if (type.Type is null)
            {
                throw new IrException($"Type '{type.Name}' is missing a type expression.");
            }

            if (type.Specialization is { } specialization &&
                (string.IsNullOrWhiteSpace(specialization.Module) || string.IsNullOrWhiteSpace(specialization.Name) ||
                 document.Modules.All(m => m.Name != specialization.Module)))
                throw new IrException($"Invalid specialization origin on '{module.Name}.{type.Name}'.");

            ValidateExpr(document, module, type.Type, type.Name, ownerComponents: null);
            ValidateCSharpAliasOf(document, module, type);
        }

        var valueNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in module.Values)
        {
            if (string.IsNullOrWhiteSpace(value.Name))
            {
                throw new IrException($"Value name is required in module '{module.Name}'.");
            }

            if (!valueNames.Add(value.Name))
            {
                throw new IrException($"Duplicate value '{value.Name}' in module '{module.Name}'.");
            }

            if (value.Type is null)
            {
                throw new IrException($"Value '{value.Name}' is missing a type expression.");
            }

            if (value.Value is null)
            {
                throw new IrException($"Value '{value.Name}' is missing a value expression.");
            }

            ValidateExpr(document, module, value.Type, value.Name, ownerComponents: null);
            ValidateValue(document, module, value.Value, value.Name);
        }
    }

    private static void ValidateExpr(
        IrDocument document,
        IrModule module,
        TypeExpr expr,
        string context,
        IReadOnlyList<IrComponent>? ownerComponents,
        IReadOnlyList<IReadOnlyList<IrComponent>>? ancestors = null)
    {
        ValidateTag(expr.Tag, context);
        ValidateConstraint(expr.Constraint, context);
        switch (expr)
        {
            case SequenceType sequence:
                ValidateComponents(document, module, sequence.Components, context, allowOptional: true, ancestors);
                break;
            case SetType set:
                ValidateComponents(document, module, set.Components, context, allowOptional: true, ancestors);
                break;
            case ChoiceType choice:
                if (choice.Components.Count == 0)
                {
                    throw new IrException($"CHOICE '{context}' has no alternatives.");
                }

                ValidateComponents(document, module, choice.Components, context, allowOptional: false, ancestors);
                break;
            case SequenceOfType sequenceOf:
                if (sequenceOf.Element is null)
                {
                    throw new IrException($"sequenceOf '{context}' is missing element.");
                }

                ValidateExpr(document, module, sequenceOf.Element, context + "[]", ownerComponents, ancestors);
                break;
            case SetOfType setOf:
                if (setOf.Element is null)
                {
                    throw new IrException($"setOf '{context}' is missing element.");
                }

                ValidateExpr(document, module, setOf.Element, context + "[]", ownerComponents, ancestors);
                break;
            case RefType reference:
                if (string.IsNullOrWhiteSpace(reference.Name))
                {
                    throw new IrException($"Type reference in '{context}' has no name.");
                }

                if (!ResolveRef(document, module, reference))
                {
                    throw new IrException($"Unresolved type '{FormatRef(reference)}' referenced from {context}.");
                }

                if (reference.OpenTypes is not null)
                {
                    var paths = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var use in reference.OpenTypes)
                    {
                        if (use.Path.Count == 0 || use.Path.Any(string.IsNullOrWhiteSpace) ||
                            !paths.Add(string.Join("/", use.Path)))
                            throw new IrException($"Invalid or duplicate open-type path in '{context}'.");
                        if (ResolveOpenTypePath(document, module, reference, use.Path) is not AnyType)
                            throw new IrException($"Open-type path in '{context}' does not select ANY.");
                        var keys = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var binding in use.Bindings)
                        {
                            if (string.IsNullOrWhiteSpace(binding.Key) || !keys.Add(binding.Key) ||
                                binding.Name is not null && string.IsNullOrWhiteSpace(binding.Name) || binding.Type is null)
                                throw new IrException($"Invalid open-type binding in '{context}'.");
                            ValidateExpr(document, module, binding.Type, context + ".openTypes", ownerComponents: null);
                        }
                    }
                }

                break;
            case EnumeratedType enumerated:
                if (enumerated.Values is null || enumerated.Values.Count == 0)
                {
                    throw new IrException($"ENUMERATED '{context}' has no values.");
                }

                if (enumerated.Values.Select(v => v.Name).Distinct(StringComparer.Ordinal).Count() != enumerated.Values.Count ||
                    enumerated.Values.Select(v => v.Value).Distinct().Count() != enumerated.Values.Count)
                    throw new IrException($"Duplicate ENUMERATED name or value in '{context}'.");
                break;
            case StringType stringType:
                if (string.IsNullOrWhiteSpace(stringType.Form))
                {
                    throw new IrException($"string '{context}' is missing stringType.");
                }

                break;
            case TimeType timeType:
                if (timeType.Form is not (TimeTypes.Utc or TimeTypes.Generalized))
                {
                    throw new IrException($"time '{context}' has unknown timeType '{timeType.Form}'.");
                }

                if (timeType.FractionDigits is { } digits)
                {
                    if (digits is < 0 or > 7)
                    {
                        throw new IrException(
                            $"time '{context}' has fractionDigits '{digits}' outside 0..7.");
                    }

                    if (timeType.Form == TimeTypes.Utc && digits != 0)
                    {
                        throw new IrException(
                            $"time '{context}' with timeType 'utc' cannot have fractionDigits '{digits}'.");
                    }
                }

                break;
            case AnyType any:
                if (any.Selector is { } selector)
                {
                    if (selector.Levels < 0 || selector.Path.Count == 0 || selector.Path.Any(string.IsNullOrWhiteSpace))
                        throw new IrException($"Invalid open-type selector in '{context}'.");
                    if (ancestors is null || selector.Levels >= ancestors.Count)
                        throw new IrException($"Open-type selector in '{context}' is outside its enclosing context.");
                    ValidateSelector(document, module, ancestors[ancestors.Count - 1 - selector.Levels], selector.Path, context);
                }
                if (any.DefinedBy is not null)
                {
                    if (ownerComponents is null || ownerComponents.All(c => c.Name != any.DefinedBy))
                    {
                        throw new IrException(
                            $"ANY DEFINED BY '{any.DefinedBy}' in '{context}' does not name a sibling component.");
                    }
                }

                if (any.Bindings is { Count: > 0 })
                {
                    if (string.IsNullOrWhiteSpace(any.DefinedBy) && any.Selector is null)
                    {
                        throw new IrException(
                            $"ANY bindings in '{context}' require definedBy naming a sibling component.");
                    }

                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < any.Bindings.Count; i++)
                    {
                        var binding = any.Bindings[i];
                        if (string.IsNullOrWhiteSpace(binding.Key))
                        {
                            throw new IrException($"ANY binding[{i}] in '{context}' has an empty key.");
                        }

                        if (!keys.Add(binding.Key))
                        {
                            throw new IrException(
                                $"ANY binding key '{binding.Key}' is duplicated in '{context}'.");
                        }

                        if (binding.Name is not null && string.IsNullOrWhiteSpace(binding.Name))
                        {
                            throw new IrException(
                                $"ANY binding '{binding.Key}' in '{context}' has an empty name.");
                        }

                        if (binding.Type is null)
                        {
                            throw new IrException(
                                $"ANY binding '{binding.Key}' in '{context}' is missing a type.");
                        }

                        ValidateExpr(
                            document,
                            module,
                            binding.Type,
                            $"{context}.bindings[{binding.Key}]",
                            ownerComponents: null);
                    }
                }

                break;
            case OctetStringType octets when octets.Containing is not null:
                ValidateExpr(document, module, octets.Containing, context + ".containing", ownerComponents, ancestors);
                break;
            case BitStringType bits when bits.Containing is not null:
                ValidateExpr(document, module, bits.Containing, context + ".containing", ownerComponents, ancestors);
                break;
            case BooleanType:
            case NullType:
            case OctetStringType:
            case OidType:
            case IntegerType:
            case BitStringType:
                break;
            default:
                throw new IrException($"Unknown type expression in '{context}'.");
        }
    }

    private static void ValidateComponents(
        IrDocument document,
        IrModule module,
        List<IrComponent> components,
        string owner,
        bool allowOptional,
        IReadOnlyList<IReadOnlyList<IrComponent>>? ancestors = null)
    {
        var context = (ancestors ?? Array.Empty<IReadOnlyList<IrComponent>>()).Append(components).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component.Name))
            {
                throw new IrException($"Type '{owner}' has a component without a name.");
            }

            if (!names.Add(component.Name))
            {
                throw new IrException($"Duplicate component '{component.Name}' on type '{owner}'.");
            }

            if (!allowOptional && (component.Optional || component.Default is not null))
            {
                throw new IrException($"CHOICE alternative '{owner}.{component.Name}' cannot be OPTIONAL/DEFAULT.");
            }

            if (component.Type is null)
            {
                throw new IrException($"Component '{owner}.{component.Name}' is missing a type.");
            }

            if (component.ExtensionGroup is not null && (component.ExtensionGroup < 0 || component.ExtensionAddition != true))
                throw new IrException($"Invalid extension group on '{owner}.{component.Name}'.");
            ValidateExpr(document, module, component.Type, $"{owner}.{component.Name}", components, context);
            if (component.Default is not null)
            {
                ValidateValue(document, module, component.Default, $"{owner}.{component.Name}.default");
            }
        }
    }

    private static void ValidateValue(IrDocument document, IrModule module, IrValue value, string context)
    {
        switch (value)
        {
            case IrTypedValue typed:
                if (typed.Type is null || typed.Value is null) throw new IrException($"Invalid typed value in '{context}'.");
                ValidateExpr(document, module, typed.Type, context + ".type", ownerComponents: null);
                ValidateValue(document, module, typed.Value, context + ".value");
                break;
            case IrStructuredValue structured:
                foreach (var (name, field) in structured.Fields)
                {
                    if (string.IsNullOrWhiteSpace(name) || field is null) throw new IrException($"Invalid structured value in '{context}'.");
                    ValidateValue(document, module, field, context + "." + name);
                }
                break;
            case IrCollectionValue collection:
                foreach (var item in collection.Items)
                {
                    if (item is null) throw new IrException($"Null collection item in '{context}'.");
                    ValidateValue(document, module, item, context + "[]");
                }
                break;
            case IrChoiceValue choice:
                if (string.IsNullOrWhiteSpace(choice.Alternative) || choice.Value is null)
                    throw new IrException($"Invalid choice value in '{context}'.");
                ValidateValue(document, module, choice.Value, context + "." + choice.Alternative);
                break;
            case IrOctetStringValue octets:
                if (octets.Hex.Length % 2 != 0 || octets.Hex.Any(c => !Uri.IsHexDigit(c)))
                    throw new IrException($"Invalid octetString value in '{context}'.");
                break;
            case IrIntegerValue:
            case IrBooleanValue:
            case IrNullValue:
            case IrStringValue:
                break;
            case IrOidValue oid:
                if (string.IsNullOrWhiteSpace(oid.Value))
                {
                    throw new IrException($"OID value '{context}' is empty.");
                }

                break;
            case IrBitStringValue bitString:
                if (bitString.Bits is null && bitString.Hex is null)
                {
                    throw new IrException($"bitString value '{context}' needs bits or hex.");
                }

                break;
            case IrValueRef reference:
                if (string.IsNullOrWhiteSpace(reference.Name))
                {
                    throw new IrException($"Value reference '{context}' has no name.");
                }

                break;
            default:
                throw new IrException($"Unknown value expression in '{context}'.");
        }
    }

    private static void ValidateSelector(IrDocument document, IrModule module, IReadOnlyList<IrComponent> fields, List<string> path, string context)
    {
        TypeExpr? selected = null;
        for (var i = 0; i < path.Count; i++)
        {
            selected = fields.SingleOrDefault(f => f.Name == path[i])?.Type
                ?? throw new IrException($"Unresolved selector component '{path[i]}' in '{context}'.");
            var visited = new HashSet<string>();
            while (selected is RefType reference)
            {
                var target = document.Modules.SingleOrDefault(m => m.Name == (reference.Module ?? module.Name));
                if (target is null || !visited.Add(target.Name + "." + reference.Name))
                    throw new IrException($"Unresolved selector type in '{context}'.");
                selected = target.Types.SingleOrDefault(t => t.Name == reference.Name)?.Type
                    ?? throw new IrException($"Unresolved selector type in '{context}'.");
            }
            if (i + 1 < path.Count)
                fields = selected switch
                {
                    SequenceType sequence => sequence.Components, SetType set => set.Components,
                    _ => throw new IrException($"Selector path traverses a non-constructed type in '{context}'.")
                };
        }
        if (selected is not (OidType or IntegerType))
            throw new IrException($"Open-type selector in '{context}' must select OBJECT IDENTIFIER or INTEGER.");
    }

    private static void ValidateConstraint(IrConstraint? constraint, string context)
    {
        if (constraint is null)
        {
            return;
        }

        if (constraint.Size is not null && constraint.Size.Min < 0)
        {
            throw new IrException($"Negative SIZE min on {context}.");
        }
    }

    private static bool ResolveRef(IrDocument document, IrModule module, RefType reference)
    {
        IEnumerable<IrModule> candidates = string.IsNullOrEmpty(reference.Module)
            ? new[] { module }.Concat(document.Modules.Where(m => m != module))
            : document.Modules.Where(m => m.Name == reference.Module);

        foreach (var candidate in candidates)
        {
            if (candidate.Types.Any(t => t.Name == reference.Name))
            {
                return true;
            }

            if (candidate.Imports.SelectMany(i => i.Types).Contains(reference.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static TypeExpr? ResolveOpenTypePath(IrDocument document, IrModule module, RefType reference,
        IReadOnlyList<string> path)
    {
        TypeExpr current = reference;
        var currentModule = module;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in path)
        {
            while (current is RefType link)
            {
                var resolved = ResolveRefTarget(document, currentModule, link);
                if (resolved is null) return null;
                var (targetModule, typeDef) = resolved.Value;
                var key = targetModule.Name + "." + link.Name;
                if (!seen.Add(key)) return null;
                current = typeDef.Type;
                currentModule = targetModule;
            }
            current = part switch
            {
                "[]" when current is SequenceOfType sequenceOf => sequenceOf.Element,
                "[]" when current is SetOfType setOf => setOf.Element,
                "containing" when current is OctetStringType octets => octets.Containing!,
                "containing" when current is BitStringType bits => bits.Containing!,
                _ when current is SequenceType sequence => sequence.Components.FirstOrDefault(c => c.Name == part)?.Type!,
                _ when current is SetType set => set.Components.FirstOrDefault(c => c.Name == part)?.Type!,
                _ when current is ChoiceType choice => choice.Components.FirstOrDefault(c => c.Name == part)?.Type!,
                _ => null!
            };
            if (current is null) return null;
        }
        while (current is RefType link)
        {
            var resolved = ResolveRefTarget(document, currentModule, link);
            if (resolved is null) return null;
            var (targetModule, typeDef) = resolved.Value;
            var key = targetModule.Name + "." + link.Name;
            if (!seen.Add(key)) return null;
            current = typeDef.Type;
            currentModule = targetModule;
        }
        return current;
    }

    /// <summary>
    /// Resolves a type reference to its defining module and definition, following
    /// explicit module qualifiers, local definitions, and IMPORTS.
    /// </summary>
    private static (IrModule Module, IrTypeDef Def)? ResolveRefTarget(
        IrDocument document,
        IrModule module,
        RefType reference)
    {
        if (!string.IsNullOrEmpty(reference.Module))
        {
            var targetModule = document.Modules.FirstOrDefault(m => m.Name == reference.Module);
            var type = targetModule?.Types.FirstOrDefault(t => t.Name == reference.Name);
            return targetModule is null || type is null ? null : (targetModule, type);
        }

        var local = module.Types.FirstOrDefault(t => t.Name == reference.Name);
        if (local is not null)
        {
            return (module, local);
        }

        var import = module.Imports.FirstOrDefault(i => i.Types.Contains(reference.Name));
        if (import is not null)
        {
            var targetModule = document.Modules.FirstOrDefault(m => m.Name == import.Module);
            var type = targetModule?.Types.FirstOrDefault(t => t.Name == reference.Name);
            return targetModule is null || type is null ? null : (targetModule, type);
        }

        foreach (var candidate in document.Modules.Where(m => m != module))
        {
            var type = candidate.Types.FirstOrDefault(t => t.Name == reference.Name);
            if (type is not null)
            {
                return (candidate, type);
            }
        }

        return null;
    }

    private static void ValidateCSharpAliasOf(IrDocument document, IrModule module, IrTypeDef type)
    {
        var aliasOf = IrOptions.CSharpAliasOf(type.Options);
        if (aliasOf is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(aliasOf))
        {
            throw new IrException(
                $"Type '{module.Name}.{type.Name}' has empty options.csharp.aliasOf.");
        }

        if (IrOptions.CSharpTypeName(type.Options) is not null)
        {
            throw new IrException(
                $"Type '{module.Name}.{type.Name}' cannot combine options.csharp.aliasOf with options.csharp.typeName.");
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { module.Name + "::" + type.Name };
        var currentModule = module;
        var targetName = aliasOf;
        while (true)
        {
            var found = FindTypeDef(document, currentModule, targetName);
            if (found is null)
            {
                throw new IrException(
                    $"Type '{module.Name}.{type.Name}' options.csharp.aliasOf '{aliasOf}' does not resolve.");
            }

            var (definingModule, def) = found.Value;
            var key = definingModule.Name + "::" + def.Name;
            if (!visited.Add(key))
            {
                throw new IrException(
                    $"Type '{module.Name}.{type.Name}' options.csharp.aliasOf forms a cycle at '{def.Name}'.");
            }

            if (!IrOptions.ShouldGenerate(def.Options))
            {
                throw new IrException(
                    $"Type '{module.Name}.{type.Name}' options.csharp.aliasOf '{def.Name}' has generate: false.");
            }

            var next = IrOptions.CSharpAliasOf(def.Options);
            if (next is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(next))
            {
                throw new IrException(
                    $"Type '{definingModule.Name}.{def.Name}' has empty options.csharp.aliasOf.");
            }

            currentModule = definingModule;
            targetName = next;
        }
    }

    private static (IrModule Module, IrTypeDef Def)? FindTypeDef(
        IrDocument document,
        IrModule module,
        string name)
    {
        foreach (var candidate in new[] { module }.Concat(document.Modules))
        {
            var match = candidate.Types.FirstOrDefault(t => t.Name == name);
            if (match is not null)
            {
                return (candidate, match);
            }
        }

        return null;
    }

    private static string FormatRef(RefType reference) =>
        string.IsNullOrEmpty(reference.Module) ? reference.Name : $"{reference.Module}.{reference.Name}";

    private static void ValidateTag(IrTag? tag, string context)
    {
        if (tag is null)
        {
            return;
        }

        var cls = tag.Class?.ToLowerInvariant();
        if (cls is not (TagClasses.Universal or TagClasses.Application or TagClasses.Context or TagClasses.Private))
        {
            throw new IrException($"Unknown tag class '{tag.Class}' on {context}.");
        }

        if (tag.Number < 0)
        {
            throw new IrException($"Negative tag number on {context}.");
        }

        var mode = tag.Mode?.ToLowerInvariant();
        if (mode is not (TagModes.Implicit or TagModes.Explicit))
        {
            throw new IrException($"Unknown tag mode '{tag.Mode}' on {context}.");
        }
    }
}

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

            ValidateExpr(document, module, type.Type, type.Name, ownerComponents: null);
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
            ValidateValue(value.Value, value.Name);
        }
    }

    private static void ValidateExpr(
        IrDocument document,
        IrModule module,
        TypeExpr expr,
        string context,
        IReadOnlyList<IrComponent>? ownerComponents)
    {
        ValidateTag(expr.Tag, context);
        ValidateConstraint(expr.Constraint, context);
        switch (expr)
        {
            case SequenceType sequence:
                ValidateComponents(document, module, sequence.Components, context, allowOptional: true);
                break;
            case SetType set:
                ValidateComponents(document, module, set.Components, context, allowOptional: true);
                break;
            case ChoiceType choice:
                if (choice.Components.Count == 0)
                {
                    throw new IrException($"CHOICE '{context}' has no alternatives.");
                }

                ValidateComponents(document, module, choice.Components, context, allowOptional: false);
                break;
            case SequenceOfType sequenceOf:
                if (sequenceOf.Element is null)
                {
                    throw new IrException($"sequenceOf '{context}' is missing element.");
                }

                ValidateExpr(document, module, sequenceOf.Element, context + "[]", ownerComponents: null);
                break;
            case SetOfType setOf:
                if (setOf.Element is null)
                {
                    throw new IrException($"setOf '{context}' is missing element.");
                }

                ValidateExpr(document, module, setOf.Element, context + "[]", ownerComponents: null);
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

                break;
            case EnumeratedType enumerated:
                if (enumerated.Values is null || enumerated.Values.Count == 0)
                {
                    throw new IrException($"ENUMERATED '{context}' has no values.");
                }

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
                    if (string.IsNullOrWhiteSpace(any.DefinedBy))
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
        bool allowOptional)
    {
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

            ValidateExpr(document, module, component.Type, $"{owner}.{component.Name}", components);
            if (component.Default is not null)
            {
                ValidateValue(component.Default, $"{owner}.{component.Name}.default");
            }
        }
    }

    private static void ValidateValue(IrValue value, string context)
    {
        switch (value)
        {
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

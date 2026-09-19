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

        module.Imports ??= new List<IrImport>();
        module.Types ??= new List<IrTypeDef>();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in module.Types)
        {
            if (string.IsNullOrWhiteSpace(type.Name))
            {
                throw new IrException($"Type name is required in module '{module.Name}'.");
            }

            if (!names.Add(type.Name))
            {
                throw new IrException($"Duplicate type '{type.Name}' in module '{module.Name}'.");
            }

            if (type.Type is null)
            {
                throw new IrException($"Type '{type.Name}' is missing a type expression.");
            }

            ValidateExpr(document, module, type.Type, type.Name);
        }
    }

    private static void ValidateExpr(IrDocument document, IrModule module, TypeExpr expr, string context)
    {
        ValidateTag(expr.Tag, context);
        switch (expr)
        {
            case SequenceType sequence:
                ValidateComponents(document, module, sequence.Components, context, allowOptional: true);
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

                ValidateExpr(document, module, sequenceOf.Element, context + "[]");
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
            case BooleanType:
            case NullType:
            case OctetStringType:
            case OidType:
            case IntegerType:
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

            if (!allowOptional && component.Optional)
            {
                throw new IrException($"CHOICE alternative '{owner}.{component.Name}' cannot be OPTIONAL.");
            }

            if (component.Type is null)
            {
                throw new IrException($"Component '{owner}.{component.Name}' is missing a type.");
            }

            ValidateExpr(document, module, component.Type, $"{owner}.{component.Name}");
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

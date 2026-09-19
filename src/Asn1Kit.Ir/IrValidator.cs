namespace Asn1Kit.Ir;

public sealed class IrException : Exception
{
    public IrException(string message) : base(message)
    {
    }
}

public static class IrValidator
{
    public static void Validate(IrModule module)
    {
        if (module.IrVersion != 1)
        {
            throw new IrException($"Unsupported irVersion '{module.IrVersion}'. Expected 1.");
        }

        if (string.IsNullOrWhiteSpace(module.Module))
        {
            throw new IrException("Module name is required.");
        }

        var tagDefault = module.TagDefault?.ToLowerInvariant();
        if (tagDefault is not (TagDefaults.Explicit or TagDefaults.Implicit or TagDefaults.Automatic))
        {
            throw new IrException($"Unknown tagDefault '{module.TagDefault}'.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in module.Types)
        {
            if (string.IsNullOrWhiteSpace(type.Name))
            {
                throw new IrException("Type name is required.");
            }

            if (!names.Add(type.Name))
            {
                throw new IrException($"Duplicate type '{type.Name}' in module '{module.Module}'.");
            }

            ValidateType(module, type);
        }

        foreach (var type in module.Types)
        {
            ValidateReferences(module, type);
        }
    }

    private static void ValidateType(IrModule module, IrTypeDef type)
    {
        switch (type.Kind)
        {
            case TypeKinds.Sequence:
            case TypeKinds.Choice:
                if (type.Fields is null)
                {
                    throw new IrException($"Type '{type.Name}' is missing fields.");
                }

                var fields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in type.Fields)
                {
                    if (string.IsNullOrWhiteSpace(field.Name))
                    {
                        throw new IrException($"Type '{type.Name}' has a field without a name.");
                    }

                    if (!fields.Add(field.Name))
                    {
                        throw new IrException($"Duplicate field '{field.Name}' on type '{type.Name}'.");
                    }

                    if (string.IsNullOrWhiteSpace(field.Type))
                    {
                        throw new IrException($"Field '{type.Name}.{field.Name}' is missing a type.");
                    }

                    ValidateTag(field.Tag, $"{type.Name}.{field.Name}");
                }

                break;
            case TypeKinds.SequenceOf:
                if (string.IsNullOrWhiteSpace(type.ElementType))
                {
                    throw new IrException($"Type '{type.Name}' is sequence-of without elementType.");
                }

                break;
            case TypeKinds.Alias:
                if (string.IsNullOrWhiteSpace(type.Type))
                {
                    throw new IrException($"Type '{type.Name}' is alias without type.");
                }

                break;
            default:
                throw new IrException($"Unknown kind '{type.Kind}' on type '{type.Name}'.");
        }

        ValidateTag(type.Tag, type.Name);
    }

    private static void ValidateReferences(IrModule module, IrTypeDef type)
    {
        void Check(string name, string context)
        {
            if (BuiltinTypes.IsBuiltin(name))
            {
                return;
            }

            if (module.Types.Any(t => t.Name == name))
            {
                return;
            }

            if (module.Imports.SelectMany(i => i.Types).Contains(name))
            {
                return;
            }

            throw new IrException($"Unresolved type '{name}' referenced from {context}.");
        }

        switch (type.Kind)
        {
            case TypeKinds.Alias:
                Check(type.Type!, type.Name);
                break;
            case TypeKinds.SequenceOf:
                Check(type.ElementType!, type.Name);
                break;
            case TypeKinds.Sequence:
            case TypeKinds.Choice:
                foreach (var field in type.Fields!)
                {
                    Check(field.Type, $"{type.Name}.{field.Name}");
                }

                break;
        }
    }

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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Asn1Kit.Ir;

/// <summary>
/// Sidecar overlay that deep-merges <c>options</c> onto modules and SEQUENCE/SET/CHOICE fields.
/// Field keys are <c>Module.Type.field</c> (ASN.1 component / alternative name).
/// Unknown module, type, or field fails with <see cref="IrException"/> (no silent skip).
/// </summary>
public static class IrOptionsPatch
{
    public static void ApplyFile(IrDocument document, string path)
    {
        if (!File.Exists(path))
        {
            throw new IrException($"Options patch file not found: {path}");
        }

        ApplyJson(document, File.ReadAllText(path));
    }

    public static void ApplyJson(IrDocument document, string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new IrException("Options patch JSON must be an object.");
        }
        catch (JsonException ex)
        {
            throw new IrException("Options patch JSON is invalid: " + ex.Message);
        }

        Apply(document, root);
    }

    public static void Apply(IrDocument document, JsonObject patch)
    {
        if (patch["modules"] is JsonObject modules)
        {
            foreach (var property in modules)
            {
                if (string.IsNullOrWhiteSpace(property.Key))
                {
                    throw new IrException("Options patch module name is required.");
                }

                if (property.Value is not JsonObject modulePatch)
                {
                    throw new IrException(
                        $"Options patch modules['{property.Key}'] must be a JSON object.");
                }

                var module = document.Modules.FirstOrDefault(m => m.Name == property.Key)
                    ?? throw new IrException(
                        $"Options patch modules['{property.Key}']: module not found.");

                module.Options = Merge(module.Options, modulePatch);
            }
        }
        else if (patch.ContainsKey("modules") && patch["modules"] is not null)
        {
            throw new IrException("Options patch 'modules' must be a JSON object.");
        }

        if (patch["fields"] is JsonObject fields)
        {
            foreach (var property in fields)
            {
                ApplyField(document, property.Key, property.Value);
            }
        }
        else if (patch.ContainsKey("fields") && patch["fields"] is not null)
        {
            throw new IrException("Options patch 'fields' must be a JSON object.");
        }
    }

    private static void ApplyField(IrDocument document, string path, JsonNode? value)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new IrException("Options patch field path is required.");
        }

        if (value is not JsonObject fieldPatch)
        {
            throw new IrException($"Options patch fields['{path}'] must be a JSON object.");
        }

        var parts = path.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new IrException(
                $"Options patch field path '{path}' must be Module.Type.field.");
        }

        var moduleName = parts[0];
        var typeName = parts[1];
        var fieldName = parts[2];

        var module = document.Modules.FirstOrDefault(m => m.Name == moduleName)
            ?? throw new IrException($"Options patch field '{path}': module '{moduleName}' not found.");

        var typeDef = module.Types.FirstOrDefault(t => t.Name == typeName)
            ?? throw new IrException($"Options patch field '{path}': type '{typeName}' not found.");

        var component = FindComponent(typeDef.Type, fieldName, path);
        component.Options = Merge(component.Options, fieldPatch);
    }

    private static IrComponent FindComponent(TypeExpr type, string fieldName, string path)
    {
        IReadOnlyList<IrComponent> components = type switch
        {
            SequenceType sequence => sequence.Components,
            SetType set => set.Components,
            ChoiceType choice => choice.Components,
            _ => throw new IrException(
                $"Options patch field '{path}': type is not SEQUENCE, SET, or CHOICE.")
        };

        return components.FirstOrDefault(c => c.Name == fieldName)
            ?? throw new IrException(
                $"Options patch field '{path}': field '{fieldName}' not found.");
    }

    /// <summary>
    /// Deep-merges <paramref name="patch"/> into <paramref name="target"/>.
    /// Nested objects merge; other values replace. Patch nodes are cloned.
    /// </summary>
    public static JsonObject Merge(JsonObject? target, JsonObject patch)
    {
        target ??= new JsonObject();
        foreach (var property in patch)
        {
            if (property.Value is JsonObject patchObject &&
                target[property.Key] is JsonObject existingObject)
            {
                target[property.Key] = Merge(existingObject, patchObject);
            }
            else if (property.Value is null)
            {
                target[property.Key] = null;
            }
            else
            {
                target[property.Key] = JsonNode.Parse(property.Value.ToJsonString());
            }
        }

        return target;
    }
}

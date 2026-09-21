using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asn1Kit.Ir;

/// <summary>
/// Sidecar overlay that attaches open-type <see cref="AnyType.Bindings"/> after ASN.1 compile.
/// Keys are <c>Module.Type.field</c> (ASN.1 component name).
/// </summary>
public static class OpenTypeBindings
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static void ApplyFile(IrDocument document, string path)
    {
        if (!File.Exists(path))
        {
            throw new IrException($"Open-type bindings file not found: {path}");
        }

        ApplyJson(document, File.ReadAllText(path));
    }

    public static void ApplyJson(IrDocument document, string json)
    {
        Dictionary<string, List<IrOpenTypeBinding>>? map;
        try
        {
            map = JsonSerializer.Deserialize<Dictionary<string, List<IrOpenTypeBinding>>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new IrException("Open-type bindings JSON is invalid: " + ex.Message);
        }

        if (map is null)
        {
            throw new IrException("Open-type bindings JSON is empty.");
        }

        Apply(document, map);
    }

    public static void Apply(IrDocument document, IReadOnlyDictionary<string, List<IrOpenTypeBinding>> map)
    {
        foreach (var (path, bindings) in map)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new IrException("Open-type binding path is required.");
            }

            var parts = path.Split('.');
            if (parts.Length != 3 ||
                parts.Any(string.IsNullOrWhiteSpace))
            {
                throw new IrException(
                    $"Open-type binding path '{path}' must be Module.Type.field.");
            }

            var moduleName = parts[0];
            var typeName = parts[1];
            var fieldName = parts[2];

            var module = document.Modules.FirstOrDefault(m => m.Name == moduleName);
            if (module is null)
            {
                // Sidecar may list targets from a multi-module set; skip when that module is absent.
                continue;
            }

            var typeDef = module.Types.FirstOrDefault(t => t.Name == typeName)
                ?? throw new IrException($"Open-type binding path '{path}': type '{typeName}' not found.");

            if (typeDef.Type is not SequenceType and not SetType)
            {
                throw new IrException(
                    $"Open-type binding path '{path}': type '{typeName}' is not SEQUENCE/SET.");
            }

            var components = typeDef.Type is SequenceType sequence
                ? sequence.Components
                : ((SetType)typeDef.Type).Components;
            var component = components.FirstOrDefault(c => c.Name == fieldName)
                ?? throw new IrException(
                    $"Open-type binding path '{path}': field '{fieldName}' not found.");

            if (component.Type is not AnyType any)
            {
                throw new IrException(
                    $"Open-type binding path '{path}': field '{fieldName}' is not ANY.");
            }

            if (string.IsNullOrWhiteSpace(any.DefinedBy))
            {
                throw new IrException(
                    $"Open-type binding path '{path}': ANY has no definedBy.");
            }

            if (bindings is null || bindings.Count == 0)
            {
                throw new IrException($"Open-type binding path '{path}' has no bindings.");
            }

            any.Bindings = bindings
                .Select(b => new IrOpenTypeBinding
                {
                    Key = b.Key,
                    Type = b.Type
                })
                .ToList();
        }

        IrValidator.Validate(document);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new TypeExprConverter());
        options.Converters.Add(new IrValueConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

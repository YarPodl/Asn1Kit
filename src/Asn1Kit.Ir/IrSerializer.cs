using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Asn1Kit.Ir;

public static class IrSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .DisableAliases()
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string ToYaml(IrModule module)
    {
        NormalizeForWrite(module);
        return YamlSerializer.Serialize(module);
    }

    public static string ToJson(IrModule module)
    {
        NormalizeForWrite(module);
        return JsonSerializer.Serialize(module, JsonOptions);
    }

    public static IrModule FromYaml(string yaml)
    {
        var module = YamlDeserializer.Deserialize<IrModule>(yaml)
                     ?? throw new IrException("YAML did not contain a module.");
        NormalizeForRead(module);
        return module;
    }

    public static IrModule FromJson(string json)
    {
        var module = JsonSerializer.Deserialize<IrModule>(json, JsonOptions)
                     ?? throw new IrException("JSON did not contain a module.");
        NormalizeForRead(module);
        return module;
    }

    public static IrModule Load(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => FromJson(text),
            _ => FromYaml(text)
        };
    }

    public static void Save(IrModule module, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = ext == ".json" ? ToJson(module) : ToYaml(module);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void NormalizeForWrite(IrModule module)
    {
        module.Options ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var type in module.Types)
        {
            type.Options ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            if (type.Fields is null)
            {
                continue;
            }

            foreach (var field in type.Fields)
            {
                field.Options ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            }
        }
    }

    private static void NormalizeForRead(IrModule module)
    {
        module.Options = ToStringKeyed(module.Options);
        module.Imports ??= new List<IrImport>();
        module.Types ??= new List<IrTypeDef>();
        foreach (var type in module.Types)
        {
            type.Options = ToStringKeyed(type.Options);
            type.Fields ??= type.Kind is TypeKinds.Sequence or TypeKinds.Choice
                ? new List<IrField>()
                : type.Fields;
            if (type.Fields is null)
            {
                continue;
            }

            foreach (var field in type.Fields)
            {
                field.Options = ToStringKeyed(field.Options);
            }
        }
    }

    private static Dictionary<string, object?> ToStringKeyed(Dictionary<string, object?>? source)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (source is null)
        {
            return result;
        }

        foreach (var pair in source)
        {
            result[pair.Key] = NormalizeValue(pair.Value);
        }

        return result;
    }

    private static object? NormalizeValue(object? value)
    {
        switch (value)
        {
            case Dictionary<object, object> untyped:
                var converted = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var pair in untyped)
                {
                    converted[Convert.ToString(pair.Key) ?? ""] = NormalizeValue(pair.Value);
                }

                return converted;
            case Dictionary<string, object?> typed:
                return ToStringKeyed(typed);
            default:
                return value;
        }
    }
}

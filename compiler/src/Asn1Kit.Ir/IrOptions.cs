using System.Text.Json;
using System.Text.Json.Nodes;

namespace Asn1Kit.Ir;

public static class IrOptions
{
    public static class IntegerRepresentations
    {
        public const string Int32 = "int32";
        public const string UInt32 = "uint32";
        public const string Int64 = "int64";
        public const string UInt64 = "uint64";
        public const string BigInt = "bigint";
        public const string Der = "der";
    }

    public static class OpenTypeMismatchModes
    {
        public const string Soft = "soft";
        public const string Strict = "strict";
    }

    public static string? CSharpNamespace(JsonObject? options) =>
        GetCSharp(options, "namespace");

    public static string? CSharpTypeName(JsonObject? options) =>
        GetCSharp(options, "typeName");

    public static string? CSharpPropertyName(JsonObject? options) =>
        GetCSharp(options, "propertyName");

    public static string? IntegerRepresentation(JsonObject? options)
    {
        if (options?["integer"] is not JsonObject integer)
        {
            return null;
        }

        return integer["representation"] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    /// <summary>
    /// When a DEFINED BY key is known but the TLV does not match the bound type:
    /// <see cref="OpenTypeMismatchModes.Soft"/> (default) keeps <c>Asn1Any</c>;
    /// <see cref="OpenTypeMismatchModes.Strict"/> throws.
    /// </summary>
    public static string OpenTypeMismatch(JsonObject? options)
    {
        if (options?["openType"] is not JsonObject openType)
        {
            return OpenTypeMismatchModes.Soft;
        }

        if (openType["mismatch"] is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (string.Equals(text, OpenTypeMismatchModes.Strict, StringComparison.OrdinalIgnoreCase))
            {
                return OpenTypeMismatchModes.Strict;
            }

            if (string.Equals(text, OpenTypeMismatchModes.Soft, StringComparison.OrdinalIgnoreCase))
            {
                return OpenTypeMismatchModes.Soft;
            }

            throw new IrException(
                $"Unknown openType.mismatch '{text}'. Expected '{OpenTypeMismatchModes.Soft}' or '{OpenTypeMismatchModes.Strict}'.");
        }

        return OpenTypeMismatchModes.Soft;
    }

    public static bool ShouldGenerate(JsonObject? options)
    {
        if (options is null || !options.TryGetPropertyValue("generate", out var node) || node is null)
        {
            return true;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var flag))
            {
                return flag;
            }

            if (value.TryGetValue<string>(out var text))
            {
                return !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
            }
        }

        return true;
    }

    public static JsonObject SetCSharp(JsonObject? options, string key, string value) =>
        Set(options, "csharp." + key, JsonValue.Create(value)!);

    public static JsonObject SetIntegerRepresentation(JsonObject? options, string value) =>
        Set(options, "integer.representation", JsonValue.Create(value)!);

    public static JsonObject SetOpenTypeMismatch(JsonObject? options, string value) =>
        Set(options, "openType.mismatch", JsonValue.Create(value)!);

    public static JsonObject Set(JsonObject? options, string path, JsonNode value)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new IrException("Option path is required.");
        }

        var segments = path.Split('.');
        if (segments.Any(string.IsNullOrEmpty))
        {
            throw new IrException($"Invalid option path '{path}'.");
        }

        options ??= new JsonObject();
        var current = options;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (current[segment] is not JsonObject nested)
            {
                nested = new JsonObject();
                current[segment] = nested;
            }

            current = nested;
        }

        // JsonNode can have only one parent; clone so ApplyToModules can reuse the same value.
        current[segments[^1]] = JsonNode.Parse(value.ToJsonString())!;
        return options;
    }

    public static JsonNode ParseCliValue(string raw)
    {
        if (raw is null)
        {
            throw new IrException("Option value is required.");
        }

        try
        {
            var node = JsonNode.Parse(raw);
            if (node is not null)
            {
                return node;
            }
        }
        catch (JsonException)
        {
            // Not a JSON literal — treat as plain string.
        }

        return JsonValue.Create(raw)!;
    }

    public static (string Path, JsonNode Value) ParseCliAssignment(string assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment))
        {
            throw new IrException("Option assignment is required (path=value).");
        }

        var separator = assignment.IndexOf('=');
        if (separator <= 0)
        {
            throw new IrException($"Invalid option assignment '{assignment}'. Expected path=value.");
        }

        var path = assignment[..separator];
        var rawValue = assignment[(separator + 1)..];
        return (path, ParseCliValue(rawValue));
    }

    public static void ApplyToModules(IrDocument document, IEnumerable<string> assignments)
    {
        foreach (var assignment in assignments)
        {
            var (path, value) = ParseCliAssignment(assignment);
            foreach (var module in document.Modules)
            {
                module.Options = Set(module.Options, path, value);
            }
        }
    }

    private static string? GetCSharp(JsonObject? options, string key)
    {
        if (options?["csharp"] is not JsonObject csharp)
        {
            return null;
        }

        return csharp[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}

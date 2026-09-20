using System.Text.Json;
using System.Text.Json.Nodes;

namespace Asn1Kit.Ir;

public static class IrOptions
{
    public static string? CSharpNamespace(JsonObject? options) =>
        GetCSharp(options, "namespace");

    public static string? CSharpTypeName(JsonObject? options) =>
        GetCSharp(options, "typeName");

    public static string? CSharpPropertyName(JsonObject? options) =>
        GetCSharp(options, "propertyName");

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

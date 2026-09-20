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

    public static JsonObject SetCSharp(JsonObject? options, string key, string value)
    {
        options ??= new JsonObject();
        if (options["csharp"] is not JsonObject csharp)
        {
            csharp = new JsonObject();
            options["csharp"] = csharp;
        }

        csharp[key] = value;
        return options;
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

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Asn1Kit.Ir;

public sealed class TypeExprConverter : JsonConverter<TypeExpr>
{
    public override TypeExpr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new IrException("Type expression must be a JSON object.");
        }

        if (!root.TryGetProperty("kind", out var kindProp) || kindProp.ValueKind != JsonValueKind.String)
        {
            throw new IrException("Type expression is missing string 'kind'.");
        }

        var kind = kindProp.GetString()!;
        var inner = CloneWithoutKindConverter(options);
        var raw = root.GetRawText();
        TypeExpr? expr = kind switch
        {
            TypeKinds.Boolean => JsonSerializer.Deserialize<BooleanType>(raw, inner),
            TypeKinds.Null => JsonSerializer.Deserialize<NullType>(raw, inner),
            TypeKinds.OctetString => JsonSerializer.Deserialize<OctetStringType>(raw, inner),
            TypeKinds.Oid => JsonSerializer.Deserialize<OidType>(raw, inner),
            TypeKinds.Integer => JsonSerializer.Deserialize<IntegerType>(raw, inner),
            TypeKinds.Enumerated => JsonSerializer.Deserialize<EnumeratedType>(raw, inner),
            TypeKinds.BitString => JsonSerializer.Deserialize<BitStringType>(raw, inner),
            TypeKinds.String => JsonSerializer.Deserialize<StringType>(raw, inner),
            TypeKinds.Time => JsonSerializer.Deserialize<TimeType>(raw, inner),
            TypeKinds.Any => JsonSerializer.Deserialize<AnyType>(raw, inner),
            TypeKinds.Sequence => JsonSerializer.Deserialize<SequenceType>(raw, inner),
            TypeKinds.Set => JsonSerializer.Deserialize<SetType>(raw, inner),
            TypeKinds.Choice => JsonSerializer.Deserialize<ChoiceType>(raw, inner),
            TypeKinds.SequenceOf => JsonSerializer.Deserialize<SequenceOfType>(raw, inner),
            TypeKinds.SetOf => JsonSerializer.Deserialize<SetOfType>(raw, inner),
            TypeKinds.Ref => JsonSerializer.Deserialize<RefType>(raw, inner),
            _ => throw new IrException($"Unknown type kind '{kind}'.")
        };

        return expr ?? throw new IrException($"Failed to deserialize type kind '{kind}'.");
    }

    public override void Write(Utf8JsonWriter writer, TypeExpr value, JsonSerializerOptions options)
    {
        var inner = CloneWithoutKindConverter(options);
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), inner) as JsonObject
                   ?? throw new IrException("Failed to serialize type expression.");
        var ordered = new JsonObject { ["kind"] = value.Kind };
        foreach (var property in node)
        {
            if (property.Key == "kind")
            {
                continue;
            }

            ordered[property.Key] = property.Value is null
                ? null
                : JsonNode.Parse(property.Value.ToJsonString());
        }

        ordered.WriteTo(writer);
    }

    private static JsonSerializerOptions CloneWithoutKindConverter(JsonSerializerOptions options)
    {
        var clone = new JsonSerializerOptions(options);
        for (var i = clone.Converters.Count - 1; i >= 0; i--)
        {
            if (clone.Converters[i] is TypeExprConverter or IrValueConverter)
            {
                clone.Converters.RemoveAt(i);
            }
        }

        return clone;
    }
}

public sealed class IrValueConverter : JsonConverter<IrValue>
{
    public override IrValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new IrException("Value expression must be a JSON object.");
        }

        if (!root.TryGetProperty("kind", out var kindProp) || kindProp.ValueKind != JsonValueKind.String)
        {
            throw new IrException("Value expression is missing string 'kind'.");
        }

        var kind = kindProp.GetString()!;
        var inner = TypeExprConverterHelpers.CloneWithoutValueConverter(options);
        var raw = root.GetRawText();
        IrValue? value = kind switch
        {
            ValueKinds.Integer => JsonSerializer.Deserialize<IrIntegerValue>(raw, inner),
            ValueKinds.Boolean => JsonSerializer.Deserialize<IrBooleanValue>(raw, inner),
            ValueKinds.Null => JsonSerializer.Deserialize<IrNullValue>(raw, inner),
            ValueKinds.Oid => JsonSerializer.Deserialize<IrOidValue>(raw, inner),
            ValueKinds.String => JsonSerializer.Deserialize<IrStringValue>(raw, inner),
            ValueKinds.BitString => JsonSerializer.Deserialize<IrBitStringValue>(raw, inner),
            ValueKinds.Ref => JsonSerializer.Deserialize<IrValueRef>(raw, inner),
            _ => throw new IrException($"Unknown value kind '{kind}'.")
        };

        return value ?? throw new IrException($"Failed to deserialize value kind '{kind}'.");
    }

    public override void Write(Utf8JsonWriter writer, IrValue value, JsonSerializerOptions options)
    {
        var inner = TypeExprConverterHelpers.CloneWithoutValueConverter(options);
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), inner) as JsonObject
                   ?? throw new IrException("Failed to serialize value expression.");
        var ordered = new JsonObject { ["kind"] = value.Kind };
        foreach (var property in node)
        {
            if (property.Key == "kind")
            {
                continue;
            }

            ordered[property.Key] = property.Value is null
                ? null
                : JsonNode.Parse(property.Value.ToJsonString());
        }

        ordered.WriteTo(writer);
    }
}

internal static class TypeExprConverterHelpers
{
    public static JsonSerializerOptions CloneWithoutValueConverter(JsonSerializerOptions options)
    {
        var clone = new JsonSerializerOptions(options);
        for (var i = clone.Converters.Count - 1; i >= 0; i--)
        {
            if (clone.Converters[i] is IrValueConverter or TypeExprConverter)
            {
                clone.Converters.RemoveAt(i);
            }
        }

        return clone;
    }
}

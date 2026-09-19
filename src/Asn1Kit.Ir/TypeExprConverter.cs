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
            TypeKinds.Sequence => JsonSerializer.Deserialize<SequenceType>(raw, inner),
            TypeKinds.Choice => JsonSerializer.Deserialize<ChoiceType>(raw, inner),
            TypeKinds.SequenceOf => JsonSerializer.Deserialize<SequenceOfType>(raw, inner),
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
            if (clone.Converters[i] is TypeExprConverter)
            {
                clone.Converters.RemoveAt(i);
            }
        }

        return clone;
    }
}

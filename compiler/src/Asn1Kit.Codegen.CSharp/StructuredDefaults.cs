using System.Text;
using System.Text.Json;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.CSharp;

public sealed partial class CSharpBackend
{
    private readonly Dictionary<string, (string Name, string Type, string Expression)> _encodedDefaults = new(StringComparer.Ordinal);
    private string _defaultStorageName = "";

    private void EmitEncodedDefaults(StringBuilder sb, IrModule module)
    {
        if (_encodedDefaults.Count == 0) return;
        sb.AppendLine($"internal static class {SanitizeIdentifier(module.Name)}Defaults");
        sb.AppendLine("{");
        foreach (var entry in _encodedDefaults.Values)
            sb.AppendLine($"    internal static readonly {entry.Type} {entry.Name} = {entry.Expression};");
        sb.AppendLine("}");
    }

    private string EncodedDefault(IrDocument document, IrModule module, IrTypedValue typed)
    {
        var key = module.Name + JsonSerializer.Serialize(typed, IrSerializer.JsonOptions);
        if (!_encodedDefaults.TryGetValue(key, out var entry))
        {
            var body = new StringBuilder();
            var value = ValueExpression(document, module, "Default", "Value", typed.Type, typed.Value);
            body.Append($"Asn1Any.FromValue({value}, static (writer, value) => {{ ");
            EmitEncodeValue(body, document, module, "Default", "Value", typed.Type, "", "writer", "value");
            body.Append(" })");
            entry = ("Value" + _encodedDefaults.Count, "Asn1Any", body.ToString());
            _encodedDefaults.Add(key, entry);
        }
        return _defaultStorageName + "." + entry.Name;
    }

    private string ConstantDefault(IrModule module, string key, string type, string expression)
    {
        key = module.Name + ":" + type + ":" + key;
        if (!_encodedDefaults.TryGetValue(key, out var entry))
        {
            entry = ("Value" + _encodedDefaults.Count, type, expression);
            _encodedDefaults.Add(key, entry);
        }
        return _defaultStorageName + "." + entry.Name;
    }

    private string ValueExpression(IrDocument document, IrModule module, string owner, string hint, TypeExpr declared, IrValue value)
    {
        var csType = CsType(document, module, owner, hint, declared, false);
        var (typeModule, type) = ResolveDefaultTarget(document, module, declared);
        if (!csType.Contains('.') && (NeedsNamedType(type) || IsEnumerated(type) || IsNamedBitString(type)))
            csType = ModuleNamespace(typeModule) + "." + csType;
        if (value is IrStructuredValue structure && type is SequenceType or SetType)
        {
            var components = type is SequenceType seq ? seq.Components : ((SetType)type).Components;
            return $"new {csType} {{ " + string.Join(", ", structure.Fields.Select(pair =>
            {
                var field = components.Single(f => f.Name == pair.Key);
                return PropertyName(field, csType) + " = " + ValueExpression(document, typeModule, csType, field.Name, field.Type, pair.Value);
            })) + " }";
        }
        if (value is IrCollectionValue collection && type is SequenceOfType or SetOfType)
        {
            var element = type is SequenceOfType of ? of.Element : ((SetOfType)type).Element;
            ResolveOfItemNaming(document, module, declared, owner, hint, out var itemOwner, out var itemHint);
            return $"new {CsType(document, module, itemOwner, itemHint, element, false)}[] {{ " +
                string.Join(", ", collection.Items.Select(v => ValueExpression(document, typeModule, itemOwner, itemHint, element, v))) + " }";
        }
        if (value is IrChoiceValue choice && type is ChoiceType alternatives)
        {
            var field = alternatives.Components.Single(f => f.Name == choice.Alternative);
            var inner = ValueExpression(document, typeModule, csType, field.Name, field.Type, choice.Value);
            return IsSingleAlternativeChoice(type) ? inner : $"{csType}.From{PropertyName(field, csType).TrimStart('@')}({inner})";
        }
        if (value is IrTypedValue typed && type is AnyType any)
        {
            if (!IsOpenType(any)) return EncodedDefault(document, module, typed);
            var alt = DefaultAlternative(document, module, csType, any, typed);
            return alt is null ? $"{csType}.FromUnknown({EncodedDefault(document, module, typed)})"
                : $"{csType}.From{alt.PropName}({ValueExpression(document, module, csType, alt.PropName, alt.Type, typed.Value)})";
        }
        if (value is IrOctetStringValue octets)
            return ConstantDefault(module, octets.Hex, "ReadOnlyMemory<byte>", $"new ReadOnlyMemory<byte>(Convert.FromHexString(\"{octets.Hex}\"))");
        if (value is IrOidValue oid)
            return KnownOidExpression(document, module, oid.Value, qualifyLocal: true) is { } known
                ? known
                : ConstantDefault(module, oid.Value, "Asn1Oid", $"Asn1Oid.Parse(\"{EscapeCSharpString(oid.Value)}\")");
        if (value is IrIntegerValue integer && type is IntegerType &&
            (TryResolveIntegerRepresentation(document, module, declared) ?? IrOptions.IntegerRepresentations.Der) == IrOptions.IntegerRepresentations.Der)
            return ConstantDefault(module, integer.Value.ToString(), "Asn1Integer", IntegerDefaultExpression(integer.Value, IrOptions.IntegerRepresentations.Der));
        if (value is IrBitStringValue bits)
        {
            var contents = ConstantDefault(module, JsonSerializer.Serialize(bits, IrSerializer.JsonOptions), "Asn1BitString", BitStringDefaultExpression(bits));
            return type is BitStringType { NamedBits.Count: > 0 } ? $"new {csType} {{ Value = {contents} }}" : contents;
        }
        return DefaultStorageExpression(document, module, owner, new IrComponent { Name = hint, Type = declared, Default = value });
    }

    private OpenTypeAlternative? DefaultAlternative(IrDocument document, IrModule module, string owner, AnyType any, IrTypedValue typed)
    {
        var (expectedModule, expected) = ResolveDefaultTarget(document, module, typed.Type);
        return BuildOpenTypeAlternatives(document, module, owner, any).FirstOrDefault(alt =>
        {
            var (actualModule, actual) = ResolveDefaultTarget(document, module, alt.Type);
            return ReferenceEquals(actual, expected) ||
                (actual.Kind == expected.Kind && actual is NullType or BooleanType or IntegerType or OidType or OctetStringType);
        });
    }

    private string ValueEqualsExpression(IrDocument document, IrModule module, string owner, string hint, TypeExpr declared, IrValue expected, string actual)
    {
        var (typeModule, type) = ResolveDefaultTarget(document, module, declared);
        var csType = CsType(document, module, owner, hint, declared, false);
        if (!csType.Contains('.') && (NeedsNamedType(type) || IsEnumerated(type) || IsNamedBitString(type)))
            csType = ModuleNamespace(typeModule) + "." + csType;
        if (expected is IrStructuredValue structure && type is SequenceType or SetType)
        {
            var components = type is SequenceType seq ? seq.Components : ((SetType)type).Components;
            var tests = new List<string>();
            if (!IrOptions.IsCSharpValueType(type.Options)) tests.Add(actual + " != null");
            foreach (var field in components)
            {
                var access = actual + "." + PropertyName(field, csType);
                var fieldExpected = structure.Fields.TryGetValue(field.Name, out var present) ? present : field.Default;
                if (fieldExpected is null) { tests.Add(access + " == null"); continue; }
                var optional = field.Optional && field.Default is null;
                if (optional) tests.Add(access + " != null");
                if (optional && IsValueOptionalWrapper(document, typeModule, field.Type)) access += ".Value";
                tests.Add(ValueEqualsExpression(document, typeModule, csType, field.Name, field.Type, fieldExpected, access));
            }
            return "(" + string.Join(" && ", tests) + ")";
        }
        if (expected is IrCollectionValue collection && type is SequenceOfType or SetOfType)
        {
            var element = type is SequenceOfType of ? of.Element : ((SetOfType)type).Element;
            ResolveOfItemNaming(document, module, declared, owner, hint, out var itemOwner, out var itemHint);
            if (type is SetOfType)
            {
                var groups = collection.Items.GroupBy(v => ComparableDefaultKey(document, typeModule, element, v));
                return "(" + actual + " != null && " + actual + ".Length == " + collection.Items.Count +
                    string.Concat(groups.Select(group => " && Asn1Collection.Count(" + actual + ", static item => " +
                        ValueEqualsExpression(document, typeModule, itemOwner, itemHint, element, group.First(), "item") + ") == " + group.Count())) + ")";
            }
            return "(" + actual + " != null && " + actual + ".Length == " + collection.Items.Count +
                string.Concat(collection.Items.Select((v, i) => " && " + ValueEqualsExpression(document, typeModule, itemOwner, itemHint, element, v, actual + "[" + i + "]"))) + ")";
        }
        if (expected is IrChoiceValue choice && type is ChoiceType alternatives)
        {
            var field = alternatives.Components.Single(f => f.Name == choice.Alternative);
            if (IsSingleAlternativeChoice(type)) return ValueEqualsExpression(document, typeModule, owner, hint, field.Type, choice.Value, actual);
            var homogeneous = TryHomogeneousChoiceCsType(document, typeModule, csType, alternatives) is not null;
            var property = PropertyName(field, csType);
            var access = actual + "." + (homogeneous ? "Value" : property);
            var guard = actual + " != null && " + actual + ".Kind == " + csType + "Kind." + property;
            if (!homogeneous)
            {
                guard += " && " + access + " != null";
                if (IsValueOptionalWrapper(document, typeModule, field.Type)) access += ".Value";
            }
            return "(" + guard + " && " + ValueEqualsExpression(document, typeModule, csType, field.Name, field.Type, choice.Value, access) + ")";
        }
        if (expected is IrTypedValue typed && type is AnyType any)
        {
            if (!IsOpenType(any)) return actual + ".Equals(" + EncodedDefault(document, module, typed) + ")";
            var alt = DefaultAlternative(document, module, csType, any, typed);
            if (alt is null) return $"({actual} != null && {actual}.Unknown.HasValue && {actual}.Unknown.Value.Equals({EncodedDefault(document, module, typed)}))";
            var openAlternatives = BuildOpenTypeAlternatives(document, module, csType, any);
            var grouped = openAlternatives.GroupBy(a => a.CsType).ToList();
            var collapsed = grouped.Any(g => g.Count() > 1);
            var group = grouped.Single(g => g.Key == alt.CsType);
            var property = collapsed && group.Count() > 1
                ? grouped.Count == 1 ? "Value" : OpenTypeClrGroupPropertyName(alt.CsType)
                : alt.PropName;
            var access = actual + "." + property;
            var guard = $"{actual} != null && {access} != null";
            if (collapsed) guard += $" && {actual}.Kind == {csType}Kind.{alt.PropName}";
            if (IsValueOptionalWrapper(document, module, alt.Type)) access += ".Value";
            return "(" + guard + " && " + ValueEqualsExpression(document, module, csType, alt.PropName, alt.Type, typed.Value, access) + ")";
        }
        if (expected is IrOctetStringValue octets)
            return actual + ".Span.SequenceEqual(" + ValueExpression(document, module, owner, hint, declared, octets) + ".Span)";
        if (expected is IrBitStringValue bits && type is BitStringType { NamedBits.Count: > 0 })
            return "(" + actual + " != null && " + actual + ".Value == " + ConstantDefault(module, JsonSerializer.Serialize(bits, IrSerializer.JsonOptions), "Asn1BitString", BitStringDefaultExpression(bits)) + ")";
        if (expected is IrNullValue) return "true";
        return actual + " == " + ValueExpression(document, module, owner, hint, declared, expected);
    }

    private static string ComparableDefaultKey(IrDocument document, IrModule module, TypeExpr declared, IrValue value)
    {
        var (context, type) = ResolveDefaultTarget(document, module, declared);
        if (value is IrStructuredValue structure && type is SequenceType or SetType)
        {
            var fields = type is SequenceType sequence ? sequence.Components : ((SetType)type).Components;
            return "{" + string.Join(",", fields.Select(f => f.Name + ":" +
                (structure.Fields.TryGetValue(f.Name, out var v) ? ComparableDefaultKey(document, context, f.Type, v)
                : f.Default is not null ? ComparableDefaultKey(document, context, f.Type, f.Default) : "absent"))) + "}";
        }
        return JsonSerializer.Serialize(value, IrSerializer.JsonOptions);
    }
}

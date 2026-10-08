using System.Text;
using Asn1Kit.Runtime;
using Asn1Kit.Modern.PKIX1Explicit2009;
using Asn1Kit.Modern.PKIXCommonTypes2009;
using PkixAttribute = Asn1Kit.Pkix.AttributeTypeAndValue;
using PkixValueKind = Asn1Kit.Pkix.AttributeTypeAndValue_ValueKind;
using ModernAttribute = Asn1Kit.Modern.PKIXCommonTypes2009.SingleAttribute;

namespace Asn1Kit.Cms.Demo;

/// <summary>RFC 4514 display form for the two generated PKIX name models.</summary>
public static class RdnSequenceFormatter
{
    public static string Format(PkixAttribute[][] name) => Format(name, FormatAttribute);

    public static string Format(ModernAttribute[][] name) => Format(name, FormatAttribute);

    private static string Format<T>(T[][] name, Func<T, string> formatAttribute) =>
        string.Join(",", name.Reverse().Select(rdn => string.Join("+", rdn.Select(formatAttribute))));

    private static string FormatAttribute(PkixAttribute attribute)
    {
        var type = ShortName(attribute.Type);
        if (type is null)
            return HexAttribute(attribute.Type, Asn1Any.FromValue(attribute.Value, static (writer, value) => value.Encode(writer)));

        var value = attribute.Value;
        return type + "=" + (value.Kind == PkixValueKind.Unknown || value.Value is null
            ? Hex(value.Unknown ?? throw new Asn1Exception("Attribute value has no alternative."))
            : Escape(value.Value));
    }

    private static string FormatAttribute(ModernAttribute attribute)
    {
        var type = ShortName(attribute.Type);
        if (type is null)
            return HexAttribute(attribute.Type, attribute.Value);

        try
        {
            return type + "=" + (TryDecodeStringAttribute(attribute, type, out var value)
                ? Escape(value) : Hex(attribute.Value));
        }
        catch (Asn1Exception)
        {
            return type + "=" + Hex(attribute.Value);
        }
    }

    private static bool TryDecodeStringAttribute(ModernAttribute attribute, string type, out string value)
    {
        switch (type)
        {
            case "CN":
                return attribute.TryDecodeValue(
                    SupportedAttributesValueBindings.X520CommonNameStringValue, out value);
            case "C":
                return attribute.TryDecodeValue(
                    SupportedAttributesValueBindings.X520countryName, out value);
            case "L":
                return attribute.TryDecodeValue(
                    SupportedAttributesValueBindings.X520LocalityNameStringValue, out value);
            case "ST":
                return attribute.TryDecodeValue(
                    SupportedAttributesValueBindings.X520StateOrProvinceNameStringValue, out value);
            case "O":
                return attribute.TryDecodeValue(
                    SupportedAttributesValueBindings.X520OrganizationNameStringValue, out value);
            case "OU":
                return attribute.TryDecodeValue(
                    SupportedAttributesValueBindings.X520OrganizationalUnitNameStringValue, out value);
            case "DC":
                return attribute.TryDecodeValue(
                    SupportedAttributesValueBindings.DomainComponent, out value);
            default:
                value = default!;
                return false;
        }
    }

    private static string HexAttribute(Asn1Oid type, Asn1Any value) => type + "=" + Hex(value);

    private static string Hex(Asn1Any value) => "#" + Convert.ToHexString(value.Span);

    private static string? ShortName(Asn1Oid oid) => oid.ToString() switch
    {
        "2.5.4.3" => "CN",
        "2.5.4.6" => "C",
        "2.5.4.7" => "L",
        "2.5.4.8" => "ST",
        "2.5.4.9" => "STREET",
        "2.5.4.10" => "O",
        "2.5.4.11" => "OU",
        "0.9.2342.19200300.100.1.25" => "DC",
        "0.9.2342.19200300.100.1.1" => "UID",
        _ => null
    };

    private static string Escape(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\0') result.Append("\\00");
            else
            {
                if (character is ',' or '+' or '"' or '\\' or '<' or '>' or ';' ||
                    (index == 0 && character is ' ' or '#') ||
                    (index == value.Length - 1 && character == ' '))
                    result.Append('\\');
                result.Append(character);
            }
        }
        return result.ToString();
    }
}

using System.Text;
using Asn1Kit.Runtime;
using BenchAttribute = Asn1Kit.Pkix.Bench.AttributeTypeAndValue;
using BenchValueKind = Asn1Kit.Pkix.Bench.AttributeTypeAndValue_ValueKind;
using ModernAttribute = Asn1Kit.Modern.PKIXCommonTypes2009.SingleAttribute;

namespace Asn1Kit.Cms.Demo;

/// <summary>RFC 4514 display form for the two generated PKIX name models.</summary>
public static class RdnSequenceFormatter
{
    public static string Format(BenchAttribute[][] name) => Format(name, FormatAttribute);

    public static string Format(ModernAttribute[][] name) => Format(name, FormatAttribute);

    private static string Format<T>(T[][] name, Func<T, string> formatAttribute) =>
        string.Join(",", name.Reverse().Select(rdn => string.Join("+", rdn.Select(formatAttribute))));

    private static string FormatAttribute(BenchAttribute attribute)
    {
        var type = ShortName(attribute.Type);
        if (type is null)
            return HexAttribute(attribute.Type, Asn1Any.FromValue(attribute.Value, static (writer, value) => value.Encode(writer)));

        var value = attribute.Value;
        return type + "=" + (value.Kind == BenchValueKind.Unknown || value.Value is null
            ? Hex(value.Unknown ?? throw new Asn1Exception("Attribute value has no alternative."))
            : Escape(value.Value));
    }

    private static string FormatAttribute(ModernAttribute attribute)
    {
        var type = ShortName(attribute.Type);
        if (type is null)
            return HexAttribute(attribute.Type, attribute.Value);

        var value = attribute.Value;
        var form = value.Tag.Equals(Asn1Tag.Utf8String) ? Asn1StringForm.Utf8
            : value.Tag.Equals(Asn1Tag.PrintableString) ? Asn1StringForm.Printable
            : value.Tag.Equals(Asn1Tag.TeletexString) ? Asn1StringForm.Teletex
            : value.Tag.Equals(Asn1Tag.Ia5String) ? Asn1StringForm.Ia5
            : value.Tag.Equals(Asn1Tag.BmpString) ? Asn1StringForm.Bmp
            : value.Tag.Equals(Asn1Tag.UniversalString) ? Asn1StringForm.Universal
            : (Asn1StringForm?)null;
        if (form is null) return type + "=" + Hex(value);
        try
        {
            return type + "=" + Escape(value.DecodeValue(reader => reader.ReadString(value.Tag, form.Value)));
        }
        catch (Asn1Exception)
        {
            return type + "=" + Hex(value);
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

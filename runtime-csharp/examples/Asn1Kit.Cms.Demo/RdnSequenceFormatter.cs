using System.Text;
using Asn1Kit.Runtime;
using Asn1Kit.Pkix;
using Asn1Kit.Modern.PKIXCommonTypes2009;
using PkixAttribute = Asn1Kit.Pkix.AttributeTypeAndValue;
using ModernAttribute = Asn1Kit.Modern.PKIXCommonTypes2009.SingleAttribute;
using ModernValueBindings = Asn1Kit.Modern.PKIXCommonTypes2009.SupportedAttributesValueBindings;

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

    private static bool TryDecodeStringAttribute(PkixAttribute attribute, string type, out string value)
    {
        if (PkixStringBinding(type) is not { } binding)
        {
            value = default!;
            return false;
        }

        return attribute.TryDecodeValue(binding, out value);
    }

    private static bool TryDecodeStringAttribute(ModernAttribute attribute, string type, out string value)
    {
        if (ModernStringBinding(type) is not { } binding)
        {
            value = default!;
            return false;
        }

        return attribute.TryDecodeValue(binding, out value);
    }

    private static Asn1Kit.Pkix.ValueBinding<string>? PkixStringBinding(string type) => type switch
    {
        "CN" => AttributeTypeAndValueValueBindings.AsString.DirectoryString6,
        "C" => AttributeTypeAndValueValueBindings.AsString.Oid2546,
        "L" => AttributeTypeAndValueValueBindings.AsString.DirectoryString7,
        "ST" => AttributeTypeAndValueValueBindings.AsString.DirectoryString8,
        "O" => AttributeTypeAndValueValueBindings.AsString.DirectoryString9,
        "OU" => AttributeTypeAndValueValueBindings.AsString.DirectoryString10,
        "DC" => AttributeTypeAndValueValueBindings.AsString.Oid09234219200300100125,
        _ => null
    };

    private static Asn1Kit.Modern.PKIXCommonTypes2009.ValueBinding<string>? ModernStringBinding(string type) => type switch
    {
        "CN" => ModernValueBindings.AsString.X520CommonName,
        "C" => ModernValueBindings.AsString.X520countryName,
        "L" => ModernValueBindings.AsString.X520LocalityName,
        "ST" => ModernValueBindings.AsString.X520StateOrProvinceName,
        "O" => ModernValueBindings.AsString.X520OrganizationName,
        "OU" => ModernValueBindings.AsString.X520OrganizationalUnitName,
        "DC" => ModernValueBindings.AsString.DomainComponent,
        _ => null
    };

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

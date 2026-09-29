using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>Universal ASN.1 primitive helpers used by tests and applications (codegen uses Write*/Read* directly).</summary>
public static class Asn1Boolean
{
    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, bool value, Asn1Tag? tag = null) =>
        writer.WriteBoolean(tag ?? Asn1Tag.Boolean, value);

    /// <summary>Decodes the ASN.1 value.</summary>
    public static bool Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadBoolean(tag ?? Asn1Tag.Boolean);

    internal static bool DecodeContents(ReadOnlySpan<byte> contents, Asn1Encoding encoding)
    {
        if (contents.Length != 1)
        {
            throw new Asn1Exception("BOOLEAN must contain one octet.");
        }

        if (encoding == Asn1Encoding.Der && contents[0] is not (0x00 or 0xFF))
        {
            throw new Asn1Exception("DER BOOLEAN must be 0x00 or 0xFF.");
        }

        return contents[0] != 0x00;
    }
}

/// <summary>Provides convenience methods for ASN.1 ENUMERATED values.</summary>
public static class Asn1Enumerated
{
    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, BigInteger value, Asn1Tag? tag = null) =>
        writer.WriteEnumerated(tag ?? Asn1Tag.Enumerated, value);

    /// <summary>Decodes the ASN.1 value.</summary>
    public static BigInteger Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadEnumerated(tag ?? Asn1Tag.Enumerated);
}

/// <summary>Provides convenience methods for ASN.1 OCTET STRING values.</summary>
public static class Asn1OctetString
{
    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, ReadOnlySpan<byte> value, Asn1Tag? tag = null) =>
        writer.WriteOctetString(tag ?? Asn1Tag.OctetString, value);

    /// <summary>Decodes the ASN.1 value.</summary>
    public static ReadOnlyMemory<byte> Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadOctetString(tag ?? Asn1Tag.OctetString);

    /// <summary>Attempts to decode.</summary>
    public static bool TryDecode(
        Asn1Reader reader,
        Span<byte> destination,
        out int bytesWritten,
        Asn1Tag? tag = null) =>
        reader.TryReadOctetString(tag ?? Asn1Tag.OctetString, destination, out bytesWritten);
}

/// <summary>Provides convenience methods for supported ASN.1 character string types.</summary>
public static class Asn1String
{
    /// <summary>Provides the <c>DefaultTag</c> operation.</summary>
    public static Asn1Tag DefaultTag(Asn1StringForm form) => Asn1TextCodec.DefaultStringTag(form);

    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, string value, Asn1StringForm form, Asn1Tag? tag = null) =>
        writer.WriteString(tag ?? DefaultTag(form), value, form);

    /// <summary>Decodes the ASN.1 value.</summary>
    public static string Decode(Asn1Reader reader, Asn1StringForm form, Asn1Tag? tag = null) =>
        reader.ReadString(tag ?? DefaultTag(form), form);
}

/// <summary>Provides convenience methods for ASN.1 UTC and generalized time values.</summary>
public static class Asn1Time
{
    /// <summary>Provides the <c>DefaultTag</c> operation.</summary>
    public static Asn1Tag DefaultTag(Asn1TimeForm form) => Asn1TextCodec.DefaultTimeTag(form);

    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(
        Asn1Writer writer,
        DateTimeOffset value,
        Asn1TimeForm form,
        Asn1Tag? tag = null,
        int fractionDigits = 3) =>
        writer.WriteTime(tag ?? DefaultTag(form), value, form, fractionDigits);

    /// <summary>Decodes the ASN.1 value.</summary>
    public static DateTimeOffset Decode(Asn1Reader reader, Asn1TimeForm form, Asn1Tag? tag = null) =>
        reader.ReadTime(tag ?? DefaultTag(form), form);
}

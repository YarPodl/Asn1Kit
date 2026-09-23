using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>Universal ASN.1 primitive helpers used by tests and applications (codegen uses Write*/Read* directly).</summary>
public static class Asn1Boolean
{
    public static void Encode(Asn1Writer writer, bool value, Asn1Tag? tag = null) =>
        writer.WriteBoolean(tag ?? Asn1Tag.Boolean, value);

    public static bool Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadBoolean(tag ?? Asn1Tag.Boolean);
}

public static class Asn1Enumerated
{
    public static void Encode(Asn1Writer writer, BigInteger value, Asn1Tag? tag = null) =>
        writer.WriteEnumerated(tag ?? Asn1Tag.Enumerated, value);

    public static BigInteger Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadEnumerated(tag ?? Asn1Tag.Enumerated);
}

public static class Asn1OctetString
{
    public static void Encode(Asn1Writer writer, ReadOnlySpan<byte> value, Asn1Tag? tag = null) =>
        writer.WriteOctetString(tag ?? Asn1Tag.OctetString, value);

    public static ReadOnlyMemory<byte> Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadOctetString(tag ?? Asn1Tag.OctetString);

    public static bool TryDecode(
        Asn1Reader reader,
        Span<byte> destination,
        out int bytesWritten,
        Asn1Tag? tag = null) =>
        reader.TryReadOctetString(tag ?? Asn1Tag.OctetString, destination, out bytesWritten);
}

public static class Asn1String
{
    public static Asn1Tag DefaultTag(Asn1StringForm form) => Asn1TextCodec.DefaultStringTag(form);

    public static void Encode(Asn1Writer writer, string value, Asn1StringForm form, Asn1Tag? tag = null) =>
        writer.WriteString(tag ?? DefaultTag(form), value, form);

    public static string Decode(Asn1Reader reader, Asn1StringForm form, Asn1Tag? tag = null) =>
        reader.ReadString(tag ?? DefaultTag(form), form);
}

public static class Asn1Time
{
    public static Asn1Tag DefaultTag(Asn1TimeForm form) => Asn1TextCodec.DefaultTimeTag(form);

    public static void Encode(
        Asn1Writer writer,
        DateTimeOffset value,
        Asn1TimeForm form,
        Asn1Tag? tag = null,
        int fractionDigits = 3) =>
        writer.WriteTime(tag ?? DefaultTag(form), value, form, fractionDigits);

    public static DateTimeOffset Decode(Asn1Reader reader, Asn1TimeForm form, Asn1Tag? tag = null) =>
        reader.ReadTime(tag ?? DefaultTag(form), form);
}

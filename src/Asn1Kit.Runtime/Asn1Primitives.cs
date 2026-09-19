using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>Universal ASN.1 primitive helpers used by generated code and tests.</summary>
public static class Asn1Boolean
{
    public static void Encode(Asn1Writer writer, bool value, Asn1Tag? tag = null) =>
        writer.WriteBoolean(tag ?? Asn1Tag.Boolean, value);

    public static bool Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadBoolean(tag ?? Asn1Tag.Boolean);
}

public static class Asn1Integer
{
    public static void Encode(Asn1Writer writer, BigInteger value, Asn1Tag? tag = null) =>
        writer.WriteInteger(tag ?? Asn1Tag.Integer, value);

    public static BigInteger Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadInteger(tag ?? Asn1Tag.Integer);
}

public static class Asn1OctetString
{
    public static void Encode(Asn1Writer writer, ReadOnlySpan<byte> value, Asn1Tag? tag = null) =>
        writer.WriteOctetString(tag ?? Asn1Tag.OctetString, value);

    public static byte[] Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadOctetString(tag ?? Asn1Tag.OctetString);
}

public static class Asn1Null
{
    public static void Encode(Asn1Writer writer, Asn1Tag? tag = null) =>
        writer.WriteNull(tag ?? Asn1Tag.Null);

    public static bool Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadNull(tag ?? Asn1Tag.Null);
}

public static class Asn1ObjectIdentifier
{
    public static void Encode(Asn1Writer writer, string oid, Asn1Tag? tag = null) =>
        writer.WriteObjectIdentifier(tag ?? Asn1Tag.ObjectIdentifier, oid);

    public static string Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadObjectIdentifier(tag ?? Asn1Tag.ObjectIdentifier);
}

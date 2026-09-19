using System.Globalization;
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

    public static int[] ParseArcs(string oid)
    {
        if (string.IsNullOrWhiteSpace(oid))
        {
            throw new Asn1Exception("OID is empty.");
        }

        var parts = oid.Split('.');
        if (parts.Length < 2)
        {
            throw new Asn1Exception($"OID '{oid}' is too short.");
        }

        var arcs = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var arc) ||
                arc < 0)
            {
                throw new Asn1Exception($"OID '{oid}' has an invalid component.");
            }

            arcs[i] = arc;
        }

        return arcs;
    }

    public static byte[] EncodeContents(string oid)
    {
        var parts = ParseArcs(oid);
        var output = new List<byte> { (byte)(40 * parts[0] + parts[1]) };
        for (var i = 2; i < parts.Length; i++)
        {
            EncodeBase128(output, parts[i]);
        }

        return output.ToArray();
    }

    private static void EncodeBase128(List<byte> output, int value)
    {
        var stack = new Stack<byte>();
        stack.Push((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            stack.Push((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        while (stack.Count > 0)
        {
            output.Add(stack.Pop());
        }
    }
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

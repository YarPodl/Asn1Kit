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

public static class Asn1ObjectIdentifier
{
    public static void Encode(Asn1Writer writer, string oid, Asn1Tag? tag = null) =>
        writer.WriteObjectIdentifier(tag ?? Asn1Tag.ObjectIdentifier, oid);

    public static string Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadObjectIdentifier(tag ?? Asn1Tag.ObjectIdentifier);

    public static int[] ParseArcs(string oid)
    {
        var span = NormalizeOid(oid);
        var arcCount = CountArcs(span);
        if (arcCount < 2)
        {
            throw new Asn1Exception($"OID '{oid}' is too short.");
        }

        var arcs = new int[arcCount];
        var index = 0;
        for (var i = 0; i < arcCount; i++)
        {
            arcs[i] = ParseNextArc(span, ref index, oid);
        }

        return arcs;
    }

    public static byte[] EncodeContents(string oid)
    {
        var maxBytes = GetEncodeContentsMaxLength(oid);
        Span<byte> scratch = maxBytes <= 128 ? stackalloc byte[maxBytes] : new byte[maxBytes];
        var written = EncodeContents(oid, scratch);
        return scratch.Slice(0, written).ToArray();
    }

    /// <summary>Upper bound on OID contents size (5 base-128 octets per arc).</summary>
    public static int GetEncodeContentsMaxLength(string oid)
    {
        var span = NormalizeOid(oid);
        var arcCount = CountArcs(span);
        if (arcCount < 2)
        {
            throw new Asn1Exception($"OID '{oid}' is too short.");
        }

        return arcCount * 5;
    }

    /// <summary>Encodes OID contents into <paramref name="destination"/>; returns octet count.</summary>
    public static int EncodeContents(string oid, Span<byte> destination)
    {
        var span = NormalizeOid(oid);
        var arcCount = CountArcs(span);
        if (arcCount < 2)
        {
            throw new Asn1Exception($"OID '{oid}' is too short.");
        }

        var maxBytes = arcCount * 5;
        if (destination.Length < maxBytes)
        {
            throw new Asn1Exception("OID encode destination is too small.");
        }

        var index = 0;
        var arc0 = ParseNextArc(span, ref index, oid);
        var arc1 = ParseNextArc(span, ref index, oid);
        if (arc0 > 2)
        {
            throw new Asn1Exception($"OID '{oid}' first arc must be 0, 1, or 2.");
        }

        if (arc0 < 2 && arc1 >= 40)
        {
            throw new Asn1Exception($"OID '{oid}' second arc must be in 0..39 when first arc is {arc0}.");
        }

        long first = 40L * arc0 + arc1;
        if (first > int.MaxValue)
        {
            throw new Asn1Exception($"OID '{oid}' first subidentifier is too large.");
        }

        var written = EncodeBase128(destination, (int)first);
        for (var i = 2; i < arcCount; i++)
        {
            written += EncodeBase128(destination.Slice(written), ParseNextArc(span, ref index, oid));
        }

        return written;
    }

    private static ReadOnlySpan<char> NormalizeOid(string oid)
    {
        if (string.IsNullOrWhiteSpace(oid))
        {
            throw new Asn1Exception("OID is empty.");
        }

        return oid.AsSpan().Trim();
    }

    private static int CountArcs(ReadOnlySpan<char> span)
    {
        var count = 1;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '.')
            {
                count++;
            }
        }

        return count;
    }

    private static int ParseNextArc(ReadOnlySpan<char> span, ref int index, string oid)
    {
        if (index >= span.Length)
        {
            throw new Asn1Exception($"OID '{oid}' has an invalid component.");
        }

        var start = index;
        while (index < span.Length && span[index] != '.')
        {
            index++;
        }

        if (index == start)
        {
            throw new Asn1Exception($"OID '{oid}' has an invalid component.");
        }

        var component = span.Slice(start, index - start);
        if (!int.TryParse(component, NumberStyles.Integer, CultureInfo.InvariantCulture, out var arc) ||
            arc < 0)
        {
            throw new Asn1Exception($"OID '{oid}' has an invalid component.");
        }

        if (index < span.Length)
        {
            index++; // skip '.'
        }

        return arc;
    }

    private static int EncodeBase128(Span<byte> destination, int value)
    {
        if (value < 0)
        {
            throw new Asn1Exception("OID arc must not be negative.");
        }

        Span<byte> temp = stackalloc byte[5];
        var count = 0;
        temp[count++] = (byte)(value & 0x7F);
        value >>= 7;
        while (value > 0)
        {
            temp[count++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        for (var i = 0; i < count; i++)
        {
            destination[i] = temp[count - 1 - i];
        }

        return count;
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

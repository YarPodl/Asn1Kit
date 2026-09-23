using System.Globalization;
using System.Text;

namespace Asn1Kit.Runtime;

/// <summary>
/// OBJECT IDENTIFIER value: DER contents octets (no tag/length).
/// Sole place for dotted-string ↔ arcs ↔ base-128 contents.
/// Prefer this over <see cref="string"/> on hot encode/decode paths.
/// </summary>
public readonly struct Asn1Oid : IEquatable<Asn1Oid>
{
    private readonly ReadOnlyMemory<byte> _contents;

    private Asn1Oid(ReadOnlyMemory<byte> contents) => _contents = contents;

    /// <summary>Wraps DER contents without copying (caller owns lifetime).</summary>
    public static Asn1Oid FromContents(ReadOnlyMemory<byte> contents)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("OBJECT IDENTIFIER is empty.");
        }

        return new Asn1Oid(contents);
    }

    /// <summary>Copies DER contents into an owned buffer.</summary>
    public static Asn1Oid CopyFrom(ReadOnlySpan<byte> contents)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("OBJECT IDENTIFIER is empty.");
        }

        return new Asn1Oid(contents.ToArray());
    }

    /// <summary>Parses a dotted OID string into owned DER contents.</summary>
    public static Asn1Oid Parse(string oid) => new(EncodeContents(oid));

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

    /// <summary>DER contents span (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlySpan<byte> Span => _contents.Span;

    /// <summary>DER contents as memory (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlyMemory<byte> Memory => _contents;

    /// <summary>Detaches DER contents into a new array.</summary>
    public byte[] ToArray() => _contents.ToArray();

    /// <summary>Returns a copy that does not alias an external buffer.</summary>
    public Asn1Oid Clone() => new(_contents.ToArray());

    public static void Encode(Asn1Writer writer, Asn1Oid value, Asn1Tag? tag = null) =>
        writer.WriteObjectIdentifier(tag ?? Asn1Tag.ObjectIdentifier, value);

    public static void Encode(Asn1Writer writer, string oid, Asn1Tag? tag = null) =>
        writer.WriteObjectIdentifier(tag ?? Asn1Tag.ObjectIdentifier, oid);

    public static Asn1Oid Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadOid(tag ?? Asn1Tag.ObjectIdentifier);

    public static string DecodeString(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadObjectIdentifier(tag ?? Asn1Tag.ObjectIdentifier);

    /// <summary>Formats the dotted decimal string (allocates).</summary>
    public override string ToString() => FormatDotted(_contents.Span);

    public bool Equals(Asn1Oid other) => Span.SequenceEqual(other.Span);

    public override bool Equals(object? obj) => obj is Asn1Oid other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in Span)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Asn1Oid left, Asn1Oid right) => left.Equals(right);

    public static bool operator !=(Asn1Oid left, Asn1Oid right) => !left.Equals(right);

    internal static string FormatDotted(ReadOnlySpan<byte> contents)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("OBJECT IDENTIFIER is empty.");
        }

        var i = 0;
        var first = ReadArc(contents, ref i, rejectOverlong: false);
        int arc0;
        int arc1;
        if (first < 40)
        {
            arc0 = 0;
            arc1 = first;
        }
        else if (first < 80)
        {
            arc0 = 1;
            arc1 = first - 40;
        }
        else
        {
            arc0 = 2;
            arc1 = first - 80;
        }

        var builder = new StringBuilder();
        builder.Append(arc0.ToString(CultureInfo.InvariantCulture));
        builder.Append('.');
        builder.Append(arc1.ToString(CultureInfo.InvariantCulture));
        while (i < contents.Length)
        {
            builder.Append('.');
            builder.Append(ReadArc(contents, ref i, rejectOverlong: false).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Reads one base-128 OID subidentifier; optionally rejects overlong encoding.</summary>
    internal static int ReadArc(ReadOnlySpan<byte> contents, ref int offset, bool rejectOverlong)
    {
        if (offset >= contents.Length)
        {
            throw new Asn1Exception("Truncated OBJECT IDENTIFIER.");
        }

        var value = 0;
        var first = true;
        byte b;
        do
        {
            if (offset >= contents.Length)
            {
                throw new Asn1Exception("Truncated OBJECT IDENTIFIER.");
            }

            b = contents[offset++];
            if (first)
            {
                if (rejectOverlong && b == 0x80)
                {
                    throw new Asn1Exception("OID base-128 encoding is overlong.");
                }

                first = false;
            }

            if (value > (int.MaxValue >> 7))
            {
                throw new Asn1Exception("OBJECT IDENTIFIER arc is too large.");
            }

            value = (value << 7) | (b & 0x7F);
        } while ((b & 0x80) != 0);

        return value;
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

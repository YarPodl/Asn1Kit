using System.Globalization;
using System.Text;

namespace Asn1Kit.Runtime;

/// <summary>
/// OBJECT IDENTIFIER value: DER contents octets (no tag/length).
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
    public static Asn1Oid Parse(string oid)
    {
        var encoded = Asn1ObjectIdentifier.EncodeContents(oid);
        return new Asn1Oid(encoded);
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

    public static Asn1Oid Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadOid(tag ?? Asn1Tag.ObjectIdentifier);

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
        var first = ReadOidArc(contents, ref i);
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
            builder.Append(ReadOidArc(contents, ref i).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static int ReadOidArc(ReadOnlySpan<byte> contents, ref int offset)
    {
        if (offset >= contents.Length)
        {
            throw new Asn1Exception("Truncated OBJECT IDENTIFIER.");
        }

        var value = 0;
        byte b;
        do
        {
            if (offset >= contents.Length)
            {
                throw new Asn1Exception("Truncated OBJECT IDENTIFIER.");
            }

            b = contents[offset++];
            if ((value & ~0x01FFFFFF) != 0)
            {
                throw new Asn1Exception("OBJECT IDENTIFIER arc is too large.");
            }

            value = (value << 7) | (b & 0x7F);
        } while ((b & 0x80) != 0);

        return value;
    }
}

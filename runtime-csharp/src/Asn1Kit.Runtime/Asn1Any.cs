namespace Asn1Kit.Runtime;

/// <summary>
/// ANY value: a complete encoded TLV (tag + length + value), without open-type resolution.
/// </summary>
public readonly struct Asn1Any : IEquatable<Asn1Any>
{
    private readonly ReadOnlyMemory<byte> _encoded;
    private readonly ReadOnlyMemory<byte> _contents;

    private Asn1Any(Asn1Tag tag, ReadOnlyMemory<byte> encoded, ReadOnlyMemory<byte> contents)
    {
        Tag = tag;
        _encoded = encoded;
        _contents = contents;
    }

    /// <summary>
    /// Wraps a complete TLV without copying (caller owns lifetime).
    /// The memory must be exactly one TLV; trailing octets are rejected.
    /// </summary>
    public Asn1Any(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.Length == 0)
        {
            throw new Asn1Exception("ANY encoded TLV must not be empty.");
        }

        var reader = new Asn1Reader(encoded, Asn1Encoding.Ber);
        var parsed = reader.ReadAny();
        if (!reader.Eof)
        {
            throw new Asn1Exception("Encoded value is not a single complete TLV.");
        }

        Tag = parsed.Tag;
        _encoded = parsed.EncodedMemory;
        _contents = parsed.ContentsMemory;
    }

    /// <summary>Copies a complete TLV into an owned buffer.</summary>
    public static Asn1Any CopyFrom(ReadOnlySpan<byte> encoded) =>
        new(encoded.Length == 0 ? ReadOnlyMemory<byte>.Empty : encoded.ToArray());

    /// <summary>
    /// Builds a definite-length TLV from <paramref name="tag"/> and value octets into an owned buffer.
    /// </summary>
    public static Asn1Any FromTagAndContents(Asn1Tag tag, ReadOnlySpan<byte> contents)
    {
        var encoded = Asn1Writer.EncodeDefiniteTlv(tag, contents);
        return Wrap(tag, encoded, encoded.AsMemory(encoded.Length - contents.Length, contents.Length));
    }

    /// <summary>Trusted wrap used by <see cref="Asn1Reader.ReadAny"/> (views may alias the reader buffer).</summary>
    internal static Asn1Any Wrap(Asn1Tag tag, ReadOnlyMemory<byte> encoded, ReadOnlyMemory<byte> contents) =>
        new(tag, encoded, contents);

    public Asn1Tag Tag { get; }

    /// <summary>Complete TLV as memory (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlyMemory<byte> EncodedMemory => _encoded;

    /// <summary>Value octets as memory (slice of <see cref="EncodedMemory"/>).</summary>
    public ReadOnlyMemory<byte> ContentsMemory => _contents;

    /// <summary>Complete TLV span.</summary>
    public ReadOnlySpan<byte> Span => _encoded.Span;

    /// <summary>Detaches the complete TLV into a new array.</summary>
    public byte[] ToArray() => _encoded.ToArray();

    /// <summary>Returns a copy that does not alias an external buffer.</summary>
    public Asn1Any Clone() => new(ToArray());

    public static void Encode(Asn1Writer writer, Asn1Any value) => writer.WriteAny(value);

    public static void Encode(Asn1Writer writer, Asn1Any value, Asn1Tag tag) => writer.WriteAny(tag, value);

    public static Asn1Any Decode(Asn1Reader reader) => reader.ReadAny();

    public static Asn1Any Decode(Asn1Reader reader, Asn1Tag tag) => reader.ReadAny(tag);

    public bool Equals(Asn1Any other) => Span.SequenceEqual(other.Span);

    public override bool Equals(object? obj) => obj is Asn1Any other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in Span)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Asn1Any left, Asn1Any right) => left.Equals(right);

    public static bool operator !=(Asn1Any left, Asn1Any right) => !left.Equals(right);
}

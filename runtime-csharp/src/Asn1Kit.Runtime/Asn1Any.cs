namespace Asn1Kit.Runtime;

/// <summary>
/// ANY value: a complete encoded TLV (tag + length + value), without open-type resolution.
/// </summary>
public readonly struct Asn1Any : IEquatable<Asn1Any>
{
    /// <summary>Returns the tag and complete TLV length without dumping bytes.</summary>
    public override string ToString() => _encoded.IsEmpty
        ? "<empty>"
        : Tag + " (" + _encoded.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes)";

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

    /// <summary>Encodes a supplied value as one DER TLV using the runtime writer.</summary>
    public static Asn1Any FromValue<T>(T value, Action<Asn1Writer, T> encode)
    {
        if (encode is null) throw new ArgumentNullException(nameof(encode));
        var writer = new Asn1Writer(Asn1Encoding.Der);
        encode(writer, value);
        return new Asn1Any(writer.Encode());
    }

    /// <summary>
    /// Builds a definite-length TLV from <paramref name="tag"/> and value octets into an owned buffer.
    /// </summary>
    public static Asn1Any FromTagAndContents(Asn1Tag tag, ReadOnlySpan<byte> contents)
    {
        var buffer = new Asn1EncodeBuffer();
        buffer.WriteTlv(tag, contents);
        var encoded = buffer.ToArray();
        return Wrap(tag, encoded, encoded.AsMemory(encoded.Length - contents.Length, contents.Length));
    }

    /// <summary>Trusted wrap used by <see cref="Asn1Reader.ReadAny()"/> (views may alias the reader buffer).</summary>
    internal static Asn1Any Wrap(Asn1Tag tag, ReadOnlyMemory<byte> encoded, ReadOnlyMemory<byte> contents) =>
        new(tag, encoded, contents);

    /// <summary>Gets the <c>Tag</c> value.</summary>
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

    /// <summary>Decodes the complete TLV as one value and rejects unconsumed bytes.</summary>
    public T DecodeValue<T>(Func<Asn1Reader, T> decode, Asn1Encoding encoding = Asn1Encoding.Ber)
    {
        if (decode is null) throw new ArgumentNullException(nameof(decode));
        var reader = new Asn1Reader(_encoded, encoding);
        var value = decode(reader);
        reader.ThrowIfNotEmpty();
        return value;
    }

    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, Asn1Any value) => writer.WriteAny(value);

    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, Asn1Any value, Asn1Tag tag) => writer.WriteAny(tag, value);

    /// <summary>Decodes the ASN.1 value.</summary>
    public static Asn1Any Decode(Asn1Reader reader) => reader.ReadAny();

    /// <summary>Decodes the ASN.1 value.</summary>
    public static Asn1Any Decode(Asn1Reader reader, Asn1Tag tag) => reader.ReadAny(tag);

    /// <summary>Provides the <c>Equals</c> operation.</summary>
    public bool Equals(Asn1Any other) => Span.SequenceEqual(other.Span);

    /// <summary>Provides the <c>Equals</c> operation.</summary>
    public override bool Equals(object? obj) => obj is Asn1Any other && Equals(other);

    /// <summary>Provides the <c>GetHashCode</c> operation.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in Span)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }

    /// <summary>Provides the <c>operator ==</c> operation.</summary>
    public static bool operator ==(Asn1Any left, Asn1Any right) => left.Equals(right);

    /// <summary>Provides the <c>operator !=</c> operation.</summary>
    public static bool operator !=(Asn1Any left, Asn1Any right) => !left.Equals(right);
}

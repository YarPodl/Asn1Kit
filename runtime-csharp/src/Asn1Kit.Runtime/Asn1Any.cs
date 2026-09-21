namespace Asn1Kit.Runtime;

/// <summary>ANY value: a complete TLV broken into tag and value octets (no open-type resolution).</summary>
public readonly struct Asn1Any : IEquatable<Asn1Any>
{
    private readonly ReadOnlyMemory<byte> _contents;

    /// <summary>Wraps <paramref name="contents"/> without copying (caller owns lifetime).</summary>
    public Asn1Any(Asn1Tag tag, ReadOnlyMemory<byte> contents)
    {
        Tag = tag;
        _contents = contents;
    }

    /// <summary>Copies <paramref name="contents"/> into an owned buffer.</summary>
    public static Asn1Any CopyFrom(Asn1Tag tag, ReadOnlySpan<byte> contents) =>
        new(tag, contents.Length == 0 ? ReadOnlyMemory<byte>.Empty : contents.ToArray());

    public Asn1Tag Tag { get; }

    /// <summary>Contents as memory (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlyMemory<byte> ContentsMemory => _contents;

    public ReadOnlySpan<byte> Span => _contents.Span;

    /// <summary>Detaches contents into a new array.</summary>
    public byte[] ToArray() => _contents.ToArray();

    /// <summary>Returns a copy that does not alias an external buffer.</summary>
    public Asn1Any Clone() => new(Tag, ToArray());

    public static void Encode(Asn1Writer writer, Asn1Any value) => writer.WriteAny(value);

    public static void Encode(Asn1Writer writer, Asn1Any value, Asn1Tag tag) => writer.WriteAny(tag, value);

    public static Asn1Any Decode(Asn1Reader reader) => reader.ReadAny();

    public static Asn1Any Decode(Asn1Reader reader, Asn1Tag tag) => reader.ReadAny(tag);

    public bool Equals(Asn1Any other) =>
        Tag.Equals(other.Tag) && Span.SequenceEqual(other.Span);

    public override bool Equals(object? obj) => obj is Asn1Any other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tag);
        foreach (var b in Span)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Asn1Any left, Asn1Any right) => left.Equals(right);

    public static bool operator !=(Asn1Any left, Asn1Any right) => !left.Equals(right);
}

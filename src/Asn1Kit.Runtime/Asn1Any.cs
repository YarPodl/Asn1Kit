namespace Asn1Kit.Runtime;

/// <summary>ANY value: a complete TLV broken into tag and value octets (no open-type resolution).</summary>
public readonly struct Asn1Any : IEquatable<Asn1Any>
{
    private readonly byte[]? _contents;

    public Asn1Any(Asn1Tag tag, ReadOnlySpan<byte> contents)
    {
        Tag = tag;
        _contents = contents.Length == 0 ? Array.Empty<byte>() : contents.ToArray();
    }

    public Asn1Tag Tag { get; }

    public byte[] Contents => _contents ?? Array.Empty<byte>();

    public static void Encode(Asn1Writer writer, Asn1Any value) => writer.WriteAny(value);

    public static void Encode(Asn1Writer writer, Asn1Any value, Asn1Tag tag) => writer.WriteAny(tag, value);

    public static Asn1Any Decode(Asn1Reader reader) => reader.ReadAny();

    public static Asn1Any Decode(Asn1Reader reader, Asn1Tag tag) => reader.ReadAny(tag);

    public bool Equals(Asn1Any other) =>
        Tag.Equals(other.Tag) && Contents.AsSpan().SequenceEqual(other.Contents);

    public override bool Equals(object? obj) => obj is Asn1Any other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tag);
        foreach (var b in Contents)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Asn1Any left, Asn1Any right) => left.Equals(right);

    public static bool operator !=(Asn1Any left, Asn1Any right) => !left.Equals(right);
}

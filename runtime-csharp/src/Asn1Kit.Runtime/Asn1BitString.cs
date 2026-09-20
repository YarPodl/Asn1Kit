namespace Asn1Kit.Runtime;

/// <summary>BIT STRING value: content octets plus the count of unused trailing bits.</summary>
public readonly struct Asn1BitString : IEquatable<Asn1BitString>
{
    private readonly ReadOnlyMemory<byte> _bytes;

    /// <summary>Wraps <paramref name="bytes"/> without copying (caller owns lifetime).</summary>
    public Asn1BitString(ReadOnlyMemory<byte> bytes, int unusedBits)
    {
        if (unusedBits is < 0 or > 7)
        {
            throw new Asn1Exception("BIT STRING unusedBits must be in 0..7.");
        }

        if (bytes.Length == 0)
        {
            if (unusedBits != 0)
            {
                throw new Asn1Exception("Empty BIT STRING must have unusedBits = 0.");
            }

            _bytes = ReadOnlyMemory<byte>.Empty;
            UnusedBits = 0;
            return;
        }

        _bytes = bytes;
        UnusedBits = unusedBits;
    }

    /// <summary>Copies <paramref name="bytes"/> into an owned buffer.</summary>
    public static Asn1BitString CopyFrom(ReadOnlySpan<byte> bytes, int unusedBits) =>
        new(bytes.Length == 0 ? ReadOnlyMemory<byte>.Empty : bytes.ToArray(), unusedBits);

    public ReadOnlySpan<byte> Span => _bytes.Span;

    /// <summary>Content octets as memory (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlyMemory<byte> Memory => _bytes;

    public int UnusedBits { get; }

    public int BitLength => Span.Length == 0 ? 0 : Span.Length * 8 - UnusedBits;

    /// <summary>Detaches content octets into a new array.</summary>
    public byte[] ToArray() => _bytes.ToArray();

    /// <summary>Returns a copy that does not alias an external buffer.</summary>
    public Asn1BitString Clone() => new(ToArray(), UnusedBits);

    public bool this[int index]
    {
        get
        {
            if (index < 0 || index >= BitLength)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var byteIndex = index / 8;
            var bitIndex = 7 - (index % 8);
            return (Span[byteIndex] & (1 << bitIndex)) != 0;
        }
    }

    public static Asn1BitString FromBits(ReadOnlySpan<bool> bits)
    {
        if (bits.Length == 0)
        {
            return default;
        }

        var byteCount = (bits.Length + 7) / 8;
        var bytes = new byte[byteCount];
        for (var i = 0; i < bits.Length; i++)
        {
            if (bits[i])
            {
                bytes[i / 8] |= (byte)(1 << (7 - (i % 8)));
            }
        }

        var unused = byteCount * 8 - bits.Length;
        return new Asn1BitString(bytes, unused);
    }

    public static void Encode(Asn1Writer writer, Asn1BitString value, Asn1Tag? tag = null) =>
        writer.WriteBitString(tag ?? Asn1Tag.BitString, value);

    public static Asn1BitString Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadBitString(tag ?? Asn1Tag.BitString);

    public bool Equals(Asn1BitString other) =>
        UnusedBits == other.UnusedBits && Span.SequenceEqual(other.Span);

    public override bool Equals(object? obj) => obj is Asn1BitString other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(UnusedBits);
        foreach (var b in Span)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Asn1BitString left, Asn1BitString right) => left.Equals(right);

    public static bool operator !=(Asn1BitString left, Asn1BitString right) => !left.Equals(right);
}

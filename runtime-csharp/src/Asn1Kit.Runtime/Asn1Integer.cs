using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>
/// INTEGER value holding DER contents (big-endian, without TLV).
/// Numeric factories produce canonical contents; <see cref="FromContents"/> wraps Memory without copy;
/// <see cref="CopyFrom"/> copies a Span.
/// <c>default</c> and <see cref="Zero"/> are the integer 0 (canonical contents <c>0x00</c>).
/// </summary>
public readonly struct Asn1Integer : IEquatable<Asn1Integer>
{
    private static readonly byte[] ZeroContents = { 0x00 };

    private readonly ReadOnlyMemory<byte> _bytes;

    private Asn1Integer(ReadOnlyMemory<byte> bytes)
    {
        _bytes = bytes;
    }

    /// <summary>Integer 0; same as <c>default</c> / <see cref="FromInt32"/>(0).</summary>
    public static Asn1Integer Zero => default;

    public ReadOnlySpan<byte> Span => CanonicalMemory.Span;

    /// <summary>DER contents as memory (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlyMemory<byte> Memory => CanonicalMemory;

    private ReadOnlyMemory<byte> CanonicalMemory =>
        _bytes.Length == 0 ? ZeroContents : _bytes;

    /// <summary>Detaches DER contents into a new array.</summary>
    public byte[] ToArray() => CanonicalMemory.ToArray();

    /// <summary>Returns a copy that does not alias an external buffer.</summary>
    public Asn1Integer Clone() => new(ToArray());

    /// <summary>Wraps <paramref name="contents"/> without copying (caller owns lifetime).</summary>
    public static Asn1Integer FromContents(ReadOnlyMemory<byte> contents)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("INTEGER contents must not be empty.");
        }

        return new Asn1Integer(contents);
    }

    /// <summary>Copies <paramref name="contents"/> into an owned buffer.</summary>
    public static Asn1Integer CopyFrom(ReadOnlySpan<byte> contents)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("INTEGER contents must not be empty.");
        }

        return new Asn1Integer(contents.ToArray());
    }

    public static Asn1Integer FromBigInteger(BigInteger value) =>
        value.IsZero ? Zero : new(Asn1Writer.EncodeInteger(value));

    public static Asn1Integer FromInt32(int value) =>
        value == 0 ? Zero : FromBigInteger(value);

    public static Asn1Integer FromUInt32(uint value) => FromBigInteger(value);

    public static Asn1Integer FromInt64(long value) => FromBigInteger(value);

    public static Asn1Integer FromUInt64(ulong value) => FromBigInteger(value);

    /// <summary>Interprets DER INTEGER/ENUMERATED contents as a signed big-endian integer.</summary>
    public static BigInteger ToBigInteger(ReadOnlySpan<byte> contents)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("INTEGER contents must not be empty.");
        }

        return new BigInteger(contents, isUnsigned: false, isBigEndian: true);
    }

    public BigInteger ToBigInteger() => ToBigInteger(Span);

    public bool TryGetInt32(out int value)
    {
        if (!TryReadSigned(Span, maxBytes: 4, out var signed) ||
            signed < int.MinValue ||
            signed > int.MaxValue)
        {
            value = default;
            return false;
        }

        value = (int)signed;
        return true;
    }

    public int GetInt32()
    {
        if (!TryGetInt32(out var value))
        {
            throw new Asn1Exception("INTEGER value does not fit in Int32.");
        }

        return value;
    }

    public bool TryGetUInt32(out uint value)
    {
        if (!TryReadUnsigned(Span, maxBytes: 4, out var unsigned) || unsigned > uint.MaxValue)
        {
            value = default;
            return false;
        }

        value = (uint)unsigned;
        return true;
    }

    public uint GetUInt32()
    {
        if (!TryGetUInt32(out var value))
        {
            throw new Asn1Exception("INTEGER value does not fit in UInt32.");
        }

        return value;
    }

    public bool TryGetInt64(out long value)
    {
        if (!TryReadSigned(Span, maxBytes: 8, out var signed))
        {
            value = default;
            return false;
        }

        value = signed;
        return true;
    }

    public long GetInt64()
    {
        if (!TryGetInt64(out var value))
        {
            throw new Asn1Exception("INTEGER value does not fit in Int64.");
        }

        return value;
    }

    public bool TryGetUInt64(out ulong value)
    {
        if (!TryReadUnsigned(Span, maxBytes: 8, out var unsigned))
        {
            value = default;
            return false;
        }

        value = unsigned;
        return true;
    }

    public ulong GetUInt64()
    {
        if (!TryGetUInt64(out var value))
        {
            throw new Asn1Exception("INTEGER value does not fit in UInt64.");
        }

        return value;
    }

    public static void Encode(Asn1Writer writer, Asn1Integer value, Asn1Tag? tag = null) =>
        writer.WriteInteger(tag ?? Asn1Tag.Integer, value);

    public static void Encode(Asn1Writer writer, BigInteger value, Asn1Tag? tag = null) =>
        writer.WriteInteger(tag ?? Asn1Tag.Integer, value);

    public static Asn1Integer Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadIntegerValue(tag ?? Asn1Tag.Integer);

    /// <summary>Warm convenience: decode as <see cref="BigInteger"/>.</summary>
    public static BigInteger DecodeBigInteger(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadInteger(tag ?? Asn1Tag.Integer);

    public bool Equals(Asn1Integer other) => Span.SequenceEqual(other.Span);

    public override bool Equals(object? obj) => obj is Asn1Integer other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in Span)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Asn1Integer left, Asn1Integer right) => left.Equals(right);

    public static bool operator !=(Asn1Integer left, Asn1Integer right) => !left.Equals(right);

    /// <summary>
    /// Reads signed big-endian INTEGER contents without allocating <see cref="BigInteger"/>.
    /// Strips redundant leading sign octets; fails if the significant width exceeds <paramref name="maxBytes"/>.
    /// </summary>
    private static bool TryReadSigned(ReadOnlySpan<byte> contents, int maxBytes, out long value)
    {
        if (contents.Length == 0)
        {
            value = default;
            return false;
        }

        var span = contents;
        while (span.Length > 1 &&
               ((span[0] == 0x00 && (span[1] & 0x80) == 0) ||
                (span[0] == 0xFF && (span[1] & 0x80) != 0)))
        {
            span = span.Slice(1);
        }

        if (span.Length > maxBytes)
        {
            value = default;
            return false;
        }

        long result = (sbyte)span[0];
        for (var i = 1; i < span.Length; i++)
        {
            result = (result << 8) | span[i];
        }

        value = result;
        return true;
    }

    /// <summary>
    /// Reads non-negative INTEGER contents as unsigned without allocating <see cref="BigInteger"/>.
    /// </summary>
    private static bool TryReadUnsigned(ReadOnlySpan<byte> contents, int maxBytes, out ulong value)
    {
        if (contents.Length == 0 || (contents[0] & 0x80) != 0)
        {
            value = default;
            return false;
        }

        var span = contents;
        while (span.Length > 1 && span[0] == 0x00 && (span[1] & 0x80) == 0)
        {
            span = span.Slice(1);
        }

        // Leading 0x00 required so a high-bit magnitude stays non-negative.
        if (span.Length > 1 && span[0] == 0x00)
        {
            span = span.Slice(1);
        }

        if (span.Length > maxBytes)
        {
            value = default;
            return false;
        }

        ulong result = 0;
        for (var i = 0; i < span.Length; i++)
        {
            result = (result << 8) | span[i];
        }

        value = result;
        return true;
    }
}

using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>
/// INTEGER value owning DER contents (big-endian, without TLV).
/// Numeric factories produce canonical contents; <see cref="FromContents"/> preserves wire bytes.
/// </summary>
public readonly struct Asn1Integer : IEquatable<Asn1Integer>
{
    private readonly byte[]? _bytes;

    private Asn1Integer(byte[] bytes)
    {
        _bytes = bytes;
    }

    public ReadOnlySpan<byte> Span => _bytes ?? Array.Empty<byte>();

    /// <summary>Owned DER contents as memory (same lifetime as this value).</summary>
    public ReadOnlyMemory<byte> Memory => _bytes ?? Array.Empty<byte>();

    public static Asn1Integer FromContents(ReadOnlySpan<byte> contents)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("INTEGER contents must not be empty.");
        }

        return new Asn1Integer(contents.ToArray());
    }

    public static Asn1Integer FromBigInteger(BigInteger value) =>
        new(Asn1Writer.EncodeInteger(value));

    public static Asn1Integer FromInt32(int value) => FromBigInteger(value);

    public static Asn1Integer FromUInt32(uint value) => FromBigInteger(value);

    public static Asn1Integer FromInt64(long value) => FromBigInteger(value);

    public static Asn1Integer FromUInt64(ulong value) => FromBigInteger(value);

    public BigInteger ToBigInteger()
    {
        var span = Span;
        if (span.Length == 0)
        {
            throw new Asn1Exception("INTEGER contents must not be empty.");
        }

        var copy = span.ToArray();
        Array.Reverse(copy);
        return new BigInteger(copy);
    }

    public bool TryGetInt32(out int value)
    {
        var big = ToBigInteger();
        if (big < int.MinValue || big > int.MaxValue)
        {
            value = default;
            return false;
        }

        value = (int)big;
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
        var big = ToBigInteger();
        if (big < 0 || big > uint.MaxValue)
        {
            value = default;
            return false;
        }

        value = (uint)big;
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
        var big = ToBigInteger();
        if (big < long.MinValue || big > long.MaxValue)
        {
            value = default;
            return false;
        }

        value = (long)big;
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
        var big = ToBigInteger();
        if (big < 0 || big > ulong.MaxValue)
        {
            value = default;
            return false;
        }

        value = (ulong)big;
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
}

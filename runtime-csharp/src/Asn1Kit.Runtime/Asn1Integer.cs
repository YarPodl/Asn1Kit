using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>
/// INTEGER value holding DER contents (big-endian, without TLV).
/// Numeric factories produce canonical contents; <see cref="FromContents"/> wraps Memory without copy;
/// <see cref="CopyFrom"/> copies a Span.
/// </summary>
public readonly struct Asn1Integer : IEquatable<Asn1Integer>
{
    private readonly ReadOnlyMemory<byte> _bytes;

    private Asn1Integer(ReadOnlyMemory<byte> bytes)
    {
        _bytes = bytes;
    }

    public ReadOnlySpan<byte> Span => _bytes.Span;

    /// <summary>DER contents as memory (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlyMemory<byte> Memory => _bytes;

    /// <summary>Detaches DER contents into a new array.</summary>
    public byte[] ToArray() => _bytes.ToArray();

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
        new(Asn1Writer.EncodeInteger(value));

    public static Asn1Integer FromInt32(int value) => FromBigInteger(value);

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

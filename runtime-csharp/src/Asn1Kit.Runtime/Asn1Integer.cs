using System.Buffers.Binary;
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
    private static readonly byte[] SingleOctetContents = CreateSingleOctetContents();

    private readonly ReadOnlyMemory<byte> _bytes;

    private Asn1Integer(ReadOnlyMemory<byte> bytes)
    {
        _bytes = bytes;
    }

    /// <summary>Integer 0; same as <c>default</c> / <see cref="FromInt32"/>(0).</summary>
    public static Asn1Integer Zero => default;

    /// <summary>Gets the <c>Span</c> value.</summary>
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

    /// <summary>Creates a value using the supplied input.</summary>
    public static Asn1Integer FromBigInteger(BigInteger value)
    {
        if (value.IsZero)
        {
            return Zero;
        }

        return GetEncodedByteCount(value) == 1
            ? FromSingleOctet((int)value)
            : new Asn1Integer(EncodeContents(value));
    }

    /// <summary>Creates a value using the supplied input.</summary>
    public static Asn1Integer FromInt32(int value)
        => value == 0 ? Zero : value is >= sbyte.MinValue and <= sbyte.MaxValue
            ? FromSingleOctet(value)
            : FromSignedInteger(value, maxLength: 4);

    /// <summary>Creates a value using the supplied input.</summary>
    public static Asn1Integer FromUInt32(uint value)
        => value == 0 ? Zero : value <= sbyte.MaxValue
            ? FromSingleOctet((int)value)
            : FromUnsignedInteger(value, maxLength: 5);

    /// <summary>Creates a value using the supplied input.</summary>
    public static Asn1Integer FromInt64(long value)
        => value == 0 ? Zero : value is >= sbyte.MinValue and <= sbyte.MaxValue
            ? FromSingleOctet((int)value)
            : FromSignedInteger(value, maxLength: 8);

    /// <summary>Creates a value using the supplied input.</summary>
    public static Asn1Integer FromUInt64(ulong value)
        => value == 0 ? Zero : value <= (ulong)sbyte.MaxValue
            ? FromSingleOctet((int)value)
            : FromUnsignedInteger(value, maxLength: 9);

    internal static int GetEncodedByteCount(BigInteger value) =>
        value.GetByteCount(isUnsigned: false);

    internal static int EncodeContents(BigInteger value, Span<byte> destination)
    {
        if (!value.TryWriteBytes(destination, out var written, isUnsigned: false, isBigEndian: true))
        {
            throw new Asn1Exception("Failed to encode INTEGER contents.");
        }

        return written;
    }

    internal static int EncodeContents(int value, Span<byte> destination) =>
        EncodeSignedContents(value, destination);

    internal static int EncodeContents(uint value, Span<byte> destination) =>
        EncodeSignedContents(value, destination);

    internal static int EncodeContents(long value, Span<byte> destination) =>
        EncodeSignedContents(value, destination);

    internal static int EncodeContents(ulong value, Span<byte> destination) =>
        EncodeUnsignedContents(value, destination);

    private static byte[] EncodeContents(BigInteger value)
    {
        var contents = new byte[GetEncodedByteCount(value)];
        var written = EncodeContents(value, contents);
        if (written != contents.Length)
        {
            throw new Asn1Exception("Failed to encode INTEGER contents.");
        }

        return contents;
    }

    private static Asn1Integer FromSingleOctet(int value) =>
        new(SingleOctetContents.AsMemory(unchecked((byte)value), 1));

    private static byte[] CreateSingleOctetContents()
    {
        var contents = new byte[256];
        for (var i = 0; i < contents.Length; i++)
        {
            contents[i] = (byte)i;
        }

        return contents;
    }

    private static Asn1Integer FromSignedInteger(long value, int maxLength)
    {
        Span<byte> contents = stackalloc byte[maxLength];
        var written = EncodeSignedContents(value, contents);
        return new Asn1Integer(contents.Slice(0, written).ToArray());
    }

    private static Asn1Integer FromUnsignedInteger(ulong value, int maxLength)
    {
        Span<byte> contents = stackalloc byte[maxLength];
        var written = EncodeUnsignedContents(value, contents);
        return new Asn1Integer(contents.Slice(0, written).ToArray());
    }

    /// <summary>Minimal signed big-endian INTEGER contents for a 64-bit two's-complement value.</summary>
    private static int EncodeSignedContents(long value, Span<byte> destination)
    {
        Span<byte> full = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(full, value);

        var start = 0;
        if (value >= 0)
        {
            while (start < 7 && full[start] == 0x00 && (full[start + 1] & 0x80) == 0)
            {
                start++;
            }
        }
        else
        {
            while (start < 7 && full[start] == 0xFF && (full[start + 1] & 0x80) != 0)
            {
                start++;
            }
        }

        var length = 8 - start;
        EnsureEncodeDestination(destination, length);
        full.Slice(start, length).CopyTo(destination);
        return length;
    }

    /// <summary>Minimal signed big-endian INTEGER contents for an unsigned 64-bit value.</summary>
    private static int EncodeUnsignedContents(ulong value, Span<byte> destination)
    {
        if (value <= (ulong)long.MaxValue)
        {
            return EncodeSignedContents((long)value, destination);
        }

        EnsureEncodeDestination(destination, 9);
        destination[0] = 0x00;
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(1), value);
        return 9;
    }

    private static void EnsureEncodeDestination(Span<byte> destination, int required)
    {
        if (destination.Length < required)
        {
            throw new Asn1Exception("INTEGER encode destination is too small.");
        }
    }

    /// <summary>Interprets DER INTEGER/ENUMERATED contents as a signed big-endian integer.</summary>
    public static BigInteger ToBigInteger(ReadOnlySpan<byte> contents)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("INTEGER contents must not be empty.");
        }

        return new BigInteger(contents, isUnsigned: false, isBigEndian: true);
    }

    /// <summary>X.690 §8.3.2 — no unnecessary leading 0x00 / 0xFF octets.</summary>
    internal static bool IsMinimalContents(ReadOnlySpan<byte> contents)
    {
        if (contents.Length <= 1)
        {
            return true;
        }

        if (contents[0] == 0x00 && (contents[1] & 0x80) == 0)
        {
            return false;
        }

        if (contents[0] == 0xFF && (contents[1] & 0x80) != 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>Provides the <c>ToBigInteger</c> operation.</summary>
    public BigInteger ToBigInteger() => ToBigInteger(Span);

    /// <summary>Attempts to get int32.</summary>
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

    /// <summary>Provides the <c>GetInt32</c> operation.</summary>
    public int GetInt32()
    {
        if (!TryGetInt32(out var value))
        {
            throw new Asn1Exception("INTEGER value does not fit in Int32.");
        }

        return value;
    }

    /// <summary>Attempts to get uint32.</summary>
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

    /// <summary>Provides the <c>GetUInt32</c> operation.</summary>
    public uint GetUInt32()
    {
        if (!TryGetUInt32(out var value))
        {
            throw new Asn1Exception("INTEGER value does not fit in UInt32.");
        }

        return value;
    }

    /// <summary>Attempts to get int64.</summary>
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

    /// <summary>Provides the <c>GetInt64</c> operation.</summary>
    public long GetInt64()
    {
        if (!TryGetInt64(out var value))
        {
            throw new Asn1Exception("INTEGER value does not fit in Int64.");
        }

        return value;
    }

    /// <summary>Attempts to get uint64.</summary>
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

    /// <summary>Provides the <c>GetUInt64</c> operation.</summary>
    public ulong GetUInt64()
    {
        if (!TryGetUInt64(out var value))
        {
            throw new Asn1Exception("INTEGER value does not fit in UInt64.");
        }

        return value;
    }

    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, Asn1Integer value, Asn1Tag? tag = null) =>
        writer.WriteInteger(tag ?? Asn1Tag.Integer, value);

    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, BigInteger value, Asn1Tag? tag = null) =>
        writer.WriteInteger(tag ?? Asn1Tag.Integer, value);

    /// <summary>Decodes the ASN.1 value.</summary>
    public static Asn1Integer Decode(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadIntegerValue(tag ?? Asn1Tag.Integer);

    /// <summary>Warm convenience: decode as <see cref="BigInteger"/>.</summary>
    public static BigInteger DecodeBigInteger(Asn1Reader reader, Asn1Tag? tag = null) =>
        reader.ReadInteger(tag ?? Asn1Tag.Integer);

    /// <summary>Provides the <c>Equals</c> operation.</summary>
    public bool Equals(Asn1Integer other) => Span.SequenceEqual(other.Span);

    /// <summary>Provides the <c>Equals</c> operation.</summary>
    public override bool Equals(object? obj) => obj is Asn1Integer other && Equals(other);

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
    public static bool operator ==(Asn1Integer left, Asn1Integer right) => left.Equals(right);

    /// <summary>Provides the <c>operator !=</c> operation.</summary>
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

using System.Buffers.Binary;
using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>Canonical primitive contents encoders shared by the writer and value types.</summary>
internal static class Asn1ContentsEncoder
{
    public static int GetIntegerByteCount(BigInteger value) =>
        value.GetByteCount(isUnsigned: false);

    public static int EncodeBoolean(bool value, Span<byte> destination)
    {
        EnsureDestination(destination, 1, "BOOLEAN");
        destination[0] = value ? (byte)0xFF : (byte)0x00;
        return 1;
    }

    public static int EncodeInteger(BigInteger value, Span<byte> destination)
    {
        if (!value.TryWriteBytes(destination, out var written, isUnsigned: false, isBigEndian: true))
        {
            throw new Asn1Exception("Failed to encode INTEGER contents.");
        }

        return written;
    }

    public static int EncodeInteger(int value, Span<byte> destination) =>
        EncodeSignedInteger(value, destination);

    public static int EncodeInteger(uint value, Span<byte> destination) =>
        EncodeSignedInteger(value, destination);

    public static int EncodeInteger(long value, Span<byte> destination) =>
        EncodeSignedInteger(value, destination);

    public static int EncodeInteger(ulong value, Span<byte> destination) =>
        EncodeUnsignedInteger(value, destination);

    public static byte[] EncodeInteger(BigInteger value)
    {
        var bytes = new byte[GetIntegerByteCount(value)];
        var written = EncodeInteger(value, bytes);
        if (written != bytes.Length)
        {
            throw new Asn1Exception("Failed to encode INTEGER contents.");
        }

        return bytes;
    }

    /// <summary>Minimal signed big-endian INTEGER contents for a 64-bit two's-complement value.</summary>
    private static int EncodeSignedInteger(long value, Span<byte> destination)
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
        EnsureDestination(destination, length, "INTEGER");
        full.Slice(start, length).CopyTo(destination);
        return length;
    }

    /// <summary>Minimal signed big-endian INTEGER contents for an unsigned 64-bit value.</summary>
    private static int EncodeUnsignedInteger(ulong value, Span<byte> destination)
    {
        if (value <= (ulong)long.MaxValue)
        {
            return EncodeSignedInteger((long)value, destination);
        }

        EnsureDestination(destination, 9, "INTEGER");
        destination[0] = 0x00;
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(1), value);
        return 9;
    }

    private static void EnsureDestination(Span<byte> destination, int required, string typeName)
    {
        if (destination.Length < required)
        {
            throw new Asn1Exception($"{typeName} encode destination is too small.");
        }
    }
}

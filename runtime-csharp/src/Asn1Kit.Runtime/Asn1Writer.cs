using System.Buffers.Binary;
using System.Numerics;

namespace Asn1Kit.Runtime;

public sealed class Asn1Writer
{
    /// <summary>Worst-case definite length encoding: long-form prefix + 4 length octets.</summary>
    private const int MaxDefiniteLengthBytes = 5;

    /// <summary>INTEGER / string contents larger than this use a heap buffer instead of stackalloc.</summary>
    private const int StackEncodeThreshold = 64;

    private readonly MemoryStream _buffer = new();

    public Asn1Writer(Asn1Encoding encoding = Asn1Encoding.Der)
    {
        Encoding = encoding;
    }

    public Asn1Encoding Encoding { get; }

    public int EncodedLength => checked((int)_buffer.Length);

    public byte[] Encode() => _buffer.ToArray();

    public bool TryEncode(Span<byte> destination, out int bytesWritten)
    {
        var length = EncodedLength;
        if (destination.Length < length)
        {
            bytesWritten = 0;
            return false;
        }

        if (_buffer.TryGetBuffer(out var segment))
        {
            segment.AsSpan(0, length).CopyTo(destination);
        }
        else
        {
            _buffer.ToArray().AsSpan(0, length).CopyTo(destination);
        }

        bytesWritten = length;
        return true;
    }

    public void WriteBoolean(Asn1Tag tag, bool value)
    {
        Span<byte> octet = stackalloc byte[1];
        octet[0] = value ? (byte)0xFF : (byte)0x00;
        WritePrimitive(tag.AsPrimitive(), octet);
    }

    public void WriteInteger(Asn1Tag tag, BigInteger value)
    {
        var byteCount = value.GetByteCount(isUnsigned: false);
        if (byteCount <= StackEncodeThreshold)
        {
            Span<byte> buffer = stackalloc byte[byteCount];
            if (!value.TryWriteBytes(buffer, out var written, isUnsigned: false, isBigEndian: true))
            {
                throw new Asn1Exception("Failed to encode INTEGER contents.");
            }

            WritePrimitive(tag.AsPrimitive(), buffer.Slice(0, written));
            return;
        }

        var rented = new byte[byteCount];
        if (!value.TryWriteBytes(rented, out var writtenLarge, isUnsigned: false, isBigEndian: true))
        {
            throw new Asn1Exception("Failed to encode INTEGER contents.");
        }

        WritePrimitive(tag.AsPrimitive(), rented.AsSpan(0, writtenLarge));
    }

    public void WriteInteger(Asn1Tag tag, int value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var written = EncodeSignedLong(value, buffer);
        WritePrimitive(tag.AsPrimitive(), buffer.Slice(0, written));
    }

    public void WriteInteger(Asn1Tag tag, uint value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var written = EncodeSignedLong(value, buffer);
        WritePrimitive(tag.AsPrimitive(), buffer.Slice(0, written));
    }

    public void WriteInteger(Asn1Tag tag, long value)
    {
        Span<byte> buffer = stackalloc byte[9];
        var written = EncodeSignedLong(value, buffer);
        WritePrimitive(tag.AsPrimitive(), buffer.Slice(0, written));
    }

    public void WriteInteger(Asn1Tag tag, ulong value)
    {
        Span<byte> buffer = stackalloc byte[9];
        var written = EncodeUnsignedLong(value, buffer);
        WritePrimitive(tag.AsPrimitive(), buffer.Slice(0, written));
    }

    /// <summary>Writes owned INTEGER contents as-is (may be non-minimal).</summary>
    public void WriteInteger(Asn1Tag tag, Asn1Integer value)
    {
        var span = value.Span;
        if (span.Length == 0)
        {
            throw new Asn1Exception("INTEGER contents must not be empty.");
        }

        WritePrimitive(tag.AsPrimitive(), span);
    }

    /// <summary>ENUMERATED uses the same contents encoding as INTEGER (X.690).</summary>
    public void WriteEnumerated(Asn1Tag tag, BigInteger value) => WriteInteger(tag, value);

    public void WriteOctetString(Asn1Tag tag, ReadOnlySpan<byte> value)
    {
        WritePrimitive(tag.AsPrimitive(), value);
    }

    public void WriteNull(Asn1Tag tag)
    {
        WritePrimitive(tag.AsPrimitive(), ReadOnlySpan<byte>.Empty);
    }

    public void WriteObjectIdentifier(Asn1Tag tag, string oid)
    {
        var maxBytes = Asn1ObjectIdentifier.GetEncodeContentsMaxLength(oid);
        Span<byte> scratch = maxBytes <= 128 ? stackalloc byte[maxBytes] : new byte[maxBytes];
        var written = Asn1ObjectIdentifier.EncodeContents(oid, scratch);
        WritePrimitive(tag.AsPrimitive(), scratch.Slice(0, written));
    }

    public void WriteBitString(Asn1Tag tag, Asn1BitString value)
    {
        if (Encoding == Asn1Encoding.Der)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(value.Span, value.UnusedBits);
        }

        var payload = value.Span;
        WriteTag(tag.AsPrimitive());
        WriteLength(1 + payload.Length, definiteOnly: true);
        _buffer.WriteByte((byte)value.UnusedBits);
        _buffer.Write(payload);
    }

    public void WriteString(Asn1Tag tag, string value, Asn1StringForm form)
    {
        var byteCount = Asn1TextCodec.GetEncodedByteCount(value, form);
        if (byteCount <= StackEncodeThreshold)
        {
            Span<byte> buffer = stackalloc byte[byteCount];
            Asn1TextCodec.EncodeString(value, form, buffer);
            WritePrimitive(tag.AsPrimitive(), buffer);
            return;
        }

        var rented = new byte[byteCount];
        Asn1TextCodec.EncodeString(value, form, rented);
        WritePrimitive(tag.AsPrimitive(), rented);
    }

    public void WriteTime(Asn1Tag tag, DateTimeOffset value, Asn1TimeForm form, int fractionDigits = 3)
    {
        Span<byte> buffer = stackalloc byte[Asn1TextCodec.MaxEncodedTimeBytes];
        var written = Asn1TextCodec.EncodeTime(value, form, fractionDigits, buffer);
        WritePrimitive(tag.AsPrimitive(), buffer.Slice(0, written));
    }

    public void WriteSequence(Asn1Tag tag, Action<Asn1Writer> content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        WriteConstructed(tag.AsConstructed(), content, sortDerSetOf: false);
    }

    /// <summary>
    /// Writes a SEQUENCE OF: one SEQUENCE TLV whose contents are the encodings of <paramref name="items"/>.
    /// <paramref name="encodeItem"/> is invoked once per item (no per-element delegate allocation beyond the call).
    /// </summary>
    public void WriteSequenceOf<T>(Asn1Tag tag, IList<T> items, Action<Asn1Writer, T> encodeItem)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (encodeItem is null)
        {
            throw new ArgumentNullException(nameof(encodeItem));
        }

        WriteSequence(tag, inner =>
        {
            for (var i = 0; i < items.Count; i++)
            {
                encodeItem(inner, items[i]);
            }
        });
    }

    public void WriteSet(Asn1Tag tag, Action<Asn1Writer> content) => WriteSequence(tag, content);

    /// <summary>
    /// Writes a SET OF. Each call inside <paramref name="content"/> should write one complete element TLV.
    /// In DER mode, element encodings are sorted lexicographically (X.690 §11.6).
    /// </summary>
    public void WriteSetOf(Asn1Tag tag, Action<Asn1Writer> content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        WriteConstructed(tag.AsConstructed(), content, sortDerSetOf: Encoding == Asn1Encoding.Der);
    }

    /// <summary>
    /// Writes a SET OF from <paramref name="items"/>. In DER mode, element encodings are sorted
    /// lexicographically (X.690 §11.6).
    /// </summary>
    public void WriteSetOf<T>(Asn1Tag tag, IList<T> items, Action<Asn1Writer, T> encodeItem)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (encodeItem is null)
        {
            throw new ArgumentNullException(nameof(encodeItem));
        }

        WriteSetOf(tag, inner =>
        {
            for (var i = 0; i < items.Count; i++)
            {
                encodeItem(inner, items[i]);
            }
        });
    }

    public void WriteExplicit(Asn1Tag outer, Action<Asn1Writer> inner)
    {
        WriteSequence(outer, inner);
    }

    public void WriteRaw(ReadOnlySpan<byte> tlv)
    {
        _buffer.Write(tlv);
    }

    /// <summary>Writes ANY by appending the stored TLV as-is (bit-exact).</summary>
    public void WriteAny(Asn1Any value) => WriteRaw(value.EncodedMemory.Span);

    /// <summary>
    /// Writes ANY with an IMPLICIT outer tag: class/number from <paramref name="tag"/>,
    /// constructed flag preserved from <paramref name="value"/>; value octets taken from the stored TLV.
    /// </summary>
    public void WriteAny(Asn1Tag tag, Asn1Any value)
    {
        var wire = new Asn1Tag(tag.TagClass, tag.Number, value.Tag.Constructed);
        WriteTlv(wire, value.ContentsMemory.Span, definiteOnly: Encoding == Asn1Encoding.Der);
    }

    /// <summary>Builds a minimal definite-length TLV into a new array.</summary>
    internal static byte[] EncodeDefiniteTlv(Asn1Tag tag, ReadOnlySpan<byte> contents)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteTlv(tag, contents, definiteOnly: true);
        return writer.Encode();
    }

    private void WriteConstructed(Asn1Tag tag, Action<Asn1Writer> content, bool sortDerSetOf)
    {
        WriteTag(tag);
        var lengthPos = checked((int)_buffer.Position);
        Span<byte> reserved = stackalloc byte[MaxDefiniteLengthBytes];
        reserved.Clear();
        _buffer.Write(reserved);

        var contentStart = checked((int)_buffer.Position);
        content(this);
        var contentEnd = checked((int)_buffer.Position);
        var contentLength = contentEnd - contentStart;

        if (sortDerSetOf)
        {
            SortDerSetOfContentsInPlace(contentStart, contentLength);
        }

        FinishDefiniteLength(lengthPos, contentStart, contentLength);
    }

    /// <summary>
    /// Patches the reserved length field at <paramref name="lengthPos"/> and compacts the stream when
    /// the minimal definite-length encoding uses fewer than <see cref="MaxDefiniteLengthBytes"/> octets.
    /// </summary>
    private void FinishDefiniteLength(int lengthPos, int contentStart, int contentLength)
    {
        Span<byte> encoded = stackalloc byte[MaxDefiniteLengthBytes];
        var lengthSize = EncodeDefiniteLength(contentLength, encoded);
        var shift = MaxDefiniteLengthBytes - lengthSize;
        var contentEnd = contentStart + contentLength;

        if (!_buffer.TryGetBuffer(out var segment))
        {
            throw new Asn1Exception("Writer buffer is not accessible.");
        }

        var array = segment.Array!;
        var origin = segment.Offset;
        encoded.Slice(0, lengthSize).CopyTo(array.AsSpan(origin + lengthPos, lengthSize));

        if (shift != 0 && contentLength > 0)
        {
            Buffer.BlockCopy(
                array,
                origin + contentStart,
                array,
                origin + contentStart - shift,
                contentLength);
        }

        var newEnd = contentEnd - shift;
        _buffer.SetLength(newEnd);
        _buffer.Position = newEnd;
    }

    /// <summary>Writes a minimal definite-length encoding into <paramref name="destination"/>; returns octet count (1…5).</summary>
    private static int EncodeDefiniteLength(int length, Span<byte> destination)
    {
        if (length < 128)
        {
            destination[0] = (byte)length;
            return 1;
        }

        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, length);
        var start = 0;
        while (start < bytes.Length - 1 && bytes[start] == 0)
        {
            start++;
        }

        var count = bytes.Length - start;
        destination[0] = (byte)(0x80 | count);
        bytes.Slice(start, count).CopyTo(destination.Slice(1));
        return 1 + count;
    }

    private void SortDerSetOfContentsInPlace(int contentStart, int contentLength)
    {
        if (contentLength == 0)
        {
            return;
        }

        if (!_buffer.TryGetBuffer(out var segment))
        {
            throw new Asn1Exception("Writer buffer is not accessible.");
        }

        SortDerSetOfContents(segment.Array!, segment.Offset + contentStart, contentLength);
    }

    private static void SortDerSetOfContents(byte[] data, int start, int length)
    {
        var end = start + length;
        var ranges = new List<(int Start, int Length)>();
        var offset = start;
        while (offset < end)
        {
            var tlvStart = offset;
            offset = GetTlvEnd(data, offset, end);
            ranges.Add((tlvStart, offset - tlvStart));
        }

        if (ranges.Count <= 1)
        {
            return;
        }

        ranges.Sort((left, right) =>
            data.AsSpan(left.Start, left.Length)
                .SequenceCompareTo(data.AsSpan(right.Start, right.Length)));

        var alreadySorted = true;
        for (var i = 1; i < ranges.Count; i++)
        {
            if (ranges[i].Start < ranges[i - 1].Start)
            {
                alreadySorted = false;
                break;
            }
        }

        if (alreadySorted)
        {
            return;
        }

        var sorted = new byte[length];
        var writeOffset = 0;
        foreach (var (tlvStart, tlvLength) in ranges)
        {
            Buffer.BlockCopy(data, tlvStart, sorted, writeOffset, tlvLength);
            writeOffset += tlvLength;
        }

        Buffer.BlockCopy(sorted, 0, data, start, length);
    }

    /// <summary>Returns the index just past one complete TLV starting at <paramref name="offset"/> (bounded by <paramref name="end"/>).</summary>
    private static int GetTlvEnd(byte[] data, int offset, int end)
    {
        if (offset >= end)
        {
            throw new Asn1Exception("Unexpected end of ASN.1 data.");
        }

        var first = data[offset++];
        if ((first & 0x1F) == 0x1F)
        {
            byte b;
            do
            {
                if (offset >= end)
                {
                    throw new Asn1Exception("Unexpected end of ASN.1 data.");
                }

                b = data[offset++];
            } while ((b & 0x80) != 0);
        }

        if (offset >= end)
        {
            throw new Asn1Exception("Unexpected end of ASN.1 data.");
        }

        var lengthFirst = data[offset++];
        if (lengthFirst == 0x80)
        {
            throw new Asn1Exception("Indefinite length is not allowed when sorting SET OF for DER.");
        }

        int length;
        if ((lengthFirst & 0x80) == 0)
        {
            length = lengthFirst;
        }
        else
        {
            var count = lengthFirst & 0x7F;
            if (count == 0 || count > 4 || offset + count > end)
            {
                throw new Asn1Exception("Unsupported length form.");
            }

            length = 0;
            for (var i = 0; i < count; i++)
            {
                length = (length << 8) | data[offset++];
            }
        }

        if (offset + length > end)
        {
            throw new Asn1Exception("Length exceeds buffer.");
        }

        return offset + length;
    }

    private void WritePrimitive(Asn1Tag tag, ReadOnlySpan<byte> contents)
    {
        WriteTlv(tag.AsPrimitive(), contents, definiteOnly: true);
    }

    private void WriteTlv(Asn1Tag tag, ReadOnlySpan<byte> contents, bool definiteOnly)
    {
        WriteTag(tag);
        WriteLength(contents.Length, definiteOnly);
        _buffer.Write(contents);
    }

    private void WriteTag(Asn1Tag tag)
    {
        var first = (byte)(((int)tag.TagClass << 6) | (tag.Constructed ? 0x20 : 0));
        if (tag.Number < 31)
        {
            _buffer.WriteByte((byte)(first | tag.Number));
            return;
        }

        _buffer.WriteByte((byte)(first | 0x1F));
        var number = tag.Number;
        Span<byte> temp = stackalloc byte[5];
        var count = 0;
        temp[count++] = (byte)(number & 0x7F);
        number >>= 7;
        while (number > 0)
        {
            temp[count++] = (byte)((number & 0x7F) | 0x80);
            number >>= 7;
        }

        for (var i = count - 1; i >= 0; i--)
        {
            _buffer.WriteByte(temp[i]);
        }
    }

    private void WriteLength(int length, bool definiteOnly)
    {
        _ = definiteOnly;
        Span<byte> encoded = stackalloc byte[MaxDefiniteLengthBytes];
        var size = EncodeDefiniteLength(length, encoded);
        _buffer.Write(encoded[..size]);
    }

    internal static byte[] EncodeInteger(BigInteger value)
    {
        var byteCount = value.GetByteCount(isUnsigned: false);
        var bytes = new byte[byteCount];
        if (!value.TryWriteBytes(bytes, out var written, isUnsigned: false, isBigEndian: true) || written != byteCount)
        {
            throw new Asn1Exception("Failed to encode INTEGER contents.");
        }

        return bytes;
    }

    /// <summary>Minimal signed big-endian INTEGER contents for a 64-bit two's-complement value.</summary>
    private static int EncodeSignedLong(long value, Span<byte> destination)
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
        full.Slice(start, length).CopyTo(destination);
        return length;
    }

    /// <summary>Minimal signed big-endian INTEGER contents for an unsigned 64-bit value.</summary>
    private static int EncodeUnsignedLong(ulong value, Span<byte> destination)
    {
        if (value <= (ulong)long.MaxValue)
        {
            return EncodeSignedLong((long)value, destination);
        }

        // High bit of the 8-byte magnitude is set — need a leading 0x00 sign octet.
        destination[0] = 0x00;
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(1), value);
        return 9;
    }
}

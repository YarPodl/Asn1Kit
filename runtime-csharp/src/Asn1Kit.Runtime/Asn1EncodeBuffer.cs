using System.Buffers;
using System.Buffers.Binary;

namespace Asn1Kit.Runtime;

/// <summary>Mutable BER/DER encode buffer responsible for TLV framing and constructed-value finalization.</summary>
internal struct Asn1EncodeBuffer
{
    private const int MaxDefiniteLengthBytes = 5;
    private const int InitialConstructedLengthBytes = 1;
    private const int DefaultCapacity = 256;

    private byte[] _buffer;
    private int _length;

    public Asn1EncodeBuffer()
    {
        _buffer = new byte[DefaultCapacity];
        _length = 0;
    }

    public int Length => _length;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    public void EnsureCapacity(int capacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (capacity > _buffer.Length)
        {
            Array.Resize(ref _buffer, capacity);
        }
    }

    public void Reset() => _length = 0;

    public byte[] ToArray()
    {
        var result = new byte[_length];
        Buffer.BlockCopy(_buffer, 0, result, 0, _length);
        return result;
    }

    public bool TryCopyTo(Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < _length)
        {
            bytesWritten = 0;
            return false;
        }

        WrittenSpan.CopyTo(destination);
        bytesWritten = _length;
        return true;
    }

    public void WritePrimitive(Asn1Tag tag, ReadOnlySpan<byte> contents) =>
        WriteTlv(tag.AsPrimitive(), contents);

    public void WritePrimitive(Asn1Tag tag, byte firstContentsOctet, ReadOnlySpan<byte> remainingContents)
    {
        WriteTag(tag.AsPrimitive());
        WriteLength(1 + remainingContents.Length);
        EnsureAdditionalCapacity(1 + remainingContents.Length);
        _buffer[_length++] = firstContentsOctet;
        remainingContents.CopyTo(_buffer.AsSpan(_length));
        _length += remainingContents.Length;
    }

    public void WriteTlv(Asn1Tag tag, ReadOnlySpan<byte> contents)
    {
        WriteTag(tag);
        WriteLength(contents.Length);
        WriteRaw(contents);
    }

    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        EnsureAdditionalCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    public Asn1EncodeFrame BeginConstructed(Asn1Tag tag)
        => BeginValue(tag.AsConstructed());

    public Asn1EncodeFrame BeginValue(Asn1Tag tag)
    {
        WriteTag(tag);
        var lengthPosition = _length;
        EnsureAdditionalCapacity(InitialConstructedLengthBytes);
        _buffer[_length++] = 0;
        return new Asn1EncodeFrame(lengthPosition, _length);
    }

    public void EndConstructed(Asn1EncodeFrame frame, bool sortContents)
    {
        var contentLength = _length - frame.ContentStart;
        if (sortContents && contentLength != 0)
        {
            SortTlvContents(frame.ContentStart, contentLength);
        }

        FinishDefiniteLength(frame.LengthPosition, frame.ContentStart, contentLength);
    }

    private void FinishDefiniteLength(int lengthPosition, int contentStart, int contentLength)
    {
        Span<byte> encoded = stackalloc byte[MaxDefiniteLengthBytes];
        var lengthSize = EncodeDefiniteLength(contentLength, encoded);
        var reservedLengthSize = contentStart - lengthPosition;
        var shift = lengthSize - reservedLengthSize;
        var contentEnd = contentStart + contentLength;

        if (shift > 0)
        {
            EnsureAdditionalCapacity(shift);
            if (contentLength > 0)
            {
                Buffer.BlockCopy(_buffer, contentStart, _buffer, contentStart + shift, contentLength);
            }
        }

        encoded.Slice(0, lengthSize).CopyTo(_buffer.AsSpan(lengthPosition, lengthSize));
        _length = contentEnd + shift;
    }

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

    private void SortTlvContents(int contentStart, int contentLength)
    {
        var end = contentStart + contentLength;
        var ranges = new List<(int Start, int Length)>(8);
        var offset = contentStart;
        while (offset < end)
        {
            var tlvStart = offset;
            offset = GetTlvEnd(_buffer, offset, end);
            ranges.Add((tlvStart, offset - tlvStart));
        }

        if (ranges.Count <= 1)
        {
            return;
        }

        var data = _buffer;
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

        var rented = ArrayPool<byte>.Shared.Rent(contentLength);
        try
        {
            var writeOffset = 0;
            foreach (var (tlvStart, tlvLength) in ranges)
            {
                Buffer.BlockCopy(_buffer, tlvStart, rented, writeOffset, tlvLength);
                writeOffset += tlvLength;
            }

            Buffer.BlockCopy(rented, 0, _buffer, contentStart, contentLength);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int GetTlvEnd(byte[] data, int offset, int end)
    {
        if (offset >= end)
        {
            throw new Asn1Exception("Unexpected end of ASN.1 data.");
        }

        var first = data[offset++];
        if ((first & 0x1F) == 0x1F)
        {
            byte current;
            do
            {
                if (offset >= end)
                {
                    throw new Asn1Exception("Unexpected end of ASN.1 data.");
                }

                current = data[offset++];
            } while ((current & 0x80) != 0);
        }

        if (offset >= end)
        {
            throw new Asn1Exception("Unexpected end of ASN.1 data.");
        }

        var firstLengthOctet = data[offset++];
        if (firstLengthOctet == 0x80)
        {
            throw new Asn1Exception("Indefinite length is not allowed when sorting SET OF for DER.");
        }

        int length;
        if ((firstLengthOctet & 0x80) == 0)
        {
            length = firstLengthOctet;
        }
        else
        {
            var count = firstLengthOctet & 0x7F;
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

        if (length > end - offset)
        {
            throw new Asn1Exception("Length exceeds buffer.");
        }

        return offset + length;
    }

    private void WriteTag(Asn1Tag tag)
    {
        var first = (byte)(((int)tag.TagClass << 6) | (tag.Constructed ? 0x20 : 0));
        if (tag.Number < 31)
        {
            EnsureAdditionalCapacity(1);
            _buffer[_length++] = (byte)(first | tag.Number);
            return;
        }

        EnsureAdditionalCapacity(6);
        _buffer[_length++] = (byte)(first | 0x1F);
        var number = tag.Number;
        Span<byte> temporary = stackalloc byte[5];
        var count = 0;
        temporary[count++] = (byte)(number & 0x7F);
        number >>= 7;
        while (number > 0)
        {
            temporary[count++] = (byte)((number & 0x7F) | 0x80);
            number >>= 7;
        }

        for (var i = count - 1; i >= 0; i--)
        {
            _buffer[_length++] = temporary[i];
        }
    }

    private void WriteLength(int length)
    {
        Span<byte> encoded = stackalloc byte[MaxDefiniteLengthBytes];
        var size = EncodeDefiniteLength(length, encoded);
        WriteRaw(encoded.Slice(0, size));
    }

    private void EnsureAdditionalCapacity(int additional)
    {
        var required = _length + additional;
        if (required <= _buffer.Length)
        {
            return;
        }

        var newSize = _buffer.Length;
        while (newSize < required)
        {
            newSize = newSize < 1024 ? newSize * 2 : newSize + (newSize / 2);
        }

        Array.Resize(ref _buffer, newSize);
    }
}

/// <summary>Positions reserved while a constructed value is being encoded.</summary>
internal readonly struct Asn1EncodeFrame
{
    public Asn1EncodeFrame(int lengthPosition, int contentStart)
    {
        LengthPosition = lengthPosition;
        ContentStart = contentStart;
    }

    public int LengthPosition { get; }

    public int ContentStart { get; }
}

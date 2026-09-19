using System.Buffers.Binary;
using System.Numerics;

namespace Asn1Kit.Runtime;

public sealed class Asn1Writer
{
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
        WritePrimitive(tag.AsPrimitive(), EncodeInteger(value));
    }

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
        WritePrimitive(tag.AsPrimitive(), Asn1ObjectIdentifier.EncodeContents(oid));
    }

    public void WriteBitString(Asn1Tag tag, Asn1BitString value)
    {
        if (Encoding == Asn1Encoding.Der)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(value.Span, value.UnusedBits);
        }

        var contents = new byte[1 + value.Span.Length];
        contents[0] = (byte)value.UnusedBits;
        value.Span.CopyTo(contents.AsSpan(1));
        WritePrimitive(tag.AsPrimitive(), contents);
    }

    public void WriteString(Asn1Tag tag, string value, Asn1StringForm form)
    {
        WritePrimitive(tag.AsPrimitive(), Asn1TextCodec.EncodeString(value, form));
    }

    public void WriteTime(Asn1Tag tag, DateTimeOffset value, Asn1TimeForm form, int fractionDigits = 3)
    {
        var text = Asn1TextCodec.FormatTime(value, form, fractionDigits);
        WritePrimitive(tag.AsPrimitive(), Asn1TextCodec.EncodeString(text, Asn1StringForm.Visible));
    }

    public void WriteSequence(Asn1Tag tag, Action<Asn1Writer> content)
    {
        var inner = new Asn1Writer(Encoding);
        content(inner);
        WriteTlv(tag.AsConstructed(), inner.Encode(), definiteOnly: Encoding == Asn1Encoding.Der);
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

        var inner = new Asn1Writer(Encoding);
        content(inner);
        var concatenated = inner.Encode();
        var contents = Encoding == Asn1Encoding.Der
            ? SortDerSetOfContents(concatenated)
            : concatenated;
        WriteTlv(tag.AsConstructed(), contents, definiteOnly: Encoding == Asn1Encoding.Der);
    }

    public void WriteExplicit(Asn1Tag outer, Action<Asn1Writer> inner)
    {
        WriteSequence(outer, inner);
    }

    public void WriteRaw(ReadOnlySpan<byte> tlv)
    {
        _buffer.Write(tlv);
    }

    /// <summary>Writes ANY as a complete TLV using the tag and contents from <paramref name="value"/>.</summary>
    public void WriteAny(Asn1Any value)
    {
        WriteTlv(value.Tag, value.ContentsMemory.Span, definiteOnly: Encoding == Asn1Encoding.Der);
    }

    /// <summary>
    /// Writes ANY with an IMPLICIT outer tag: class/number from <paramref name="tag"/>,
    /// constructed flag preserved from <paramref name="value"/>.
    /// </summary>
    public void WriteAny(Asn1Tag tag, Asn1Any value)
    {
        var wire = new Asn1Tag(tag.TagClass, tag.Number, value.Tag.Constructed);
        WriteTlv(wire, value.ContentsMemory.Span, definiteOnly: Encoding == Asn1Encoding.Der);
    }

    private static byte[] SortDerSetOfContents(byte[] concatenated)
    {
        if (concatenated.Length == 0)
        {
            return concatenated;
        }

        var ranges = new List<(int Start, int Length)>();
        var offset = 0;
        while (offset < concatenated.Length)
        {
            var start = offset;
            offset = GetTlvEnd(concatenated, offset);
            ranges.Add((start, offset - start));
        }

        if (ranges.Count <= 1)
        {
            return concatenated;
        }

        ranges.Sort((left, right) =>
            concatenated.AsSpan(left.Start, left.Length)
                .SequenceCompareTo(concatenated.AsSpan(right.Start, right.Length)));

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
            return concatenated;
        }

        var contents = new byte[concatenated.Length];
        var writeOffset = 0;
        foreach (var (start, length) in ranges)
        {
            Buffer.BlockCopy(concatenated, start, contents, writeOffset, length);
            writeOffset += length;
        }

        return contents;
    }

    /// <summary>Returns the index just past one complete TLV starting at <paramref name="offset"/>.</summary>
    private static int GetTlvEnd(byte[] data, int offset)
    {
        if (offset >= data.Length)
        {
            throw new Asn1Exception("Unexpected end of ASN.1 data.");
        }

        var first = data[offset++];
        if ((first & 0x1F) == 0x1F)
        {
            byte b;
            do
            {
                if (offset >= data.Length)
                {
                    throw new Asn1Exception("Unexpected end of ASN.1 data.");
                }

                b = data[offset++];
            } while ((b & 0x80) != 0);
        }

        if (offset >= data.Length)
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
            if (count == 0 || count > 4 || offset + count > data.Length)
            {
                throw new Asn1Exception("Unsupported length form.");
            }

            length = 0;
            for (var i = 0; i < count; i++)
            {
                length = (length << 8) | data[offset++];
            }
        }

        if (offset + length > data.Length)
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
        var stack = new Stack<byte>();
        stack.Push((byte)(number & 0x7F));
        number >>= 7;
        while (number > 0)
        {
            stack.Push((byte)((number & 0x7F) | 0x80));
            number >>= 7;
        }

        while (stack.Count > 0)
        {
            _buffer.WriteByte(stack.Pop());
        }
    }

    private void WriteLength(int length, bool definiteOnly)
    {
        _ = definiteOnly;
        if (length < 128)
        {
            _buffer.WriteByte((byte)length);
            return;
        }

        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, length);
        var start = 0;
        while (start < bytes.Length - 1 && bytes[start] == 0)
        {
            start++;
        }

        var count = bytes.Length - start;
        _buffer.WriteByte((byte)(0x80 | count));
        _buffer.Write(bytes, start, count);
    }

    internal static byte[] EncodeInteger(BigInteger value)
    {
        var bytes = value.ToByteArray();
        Array.Reverse(bytes);
        return bytes;
    }
}

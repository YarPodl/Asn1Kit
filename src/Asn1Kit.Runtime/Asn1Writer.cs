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

    public byte[] Encode() => _buffer.ToArray();

    public void WriteBoolean(Asn1Tag tag, bool value)
    {
        WritePrimitive(tag.AsPrimitive(), new byte[] { value ? (byte)0xFF : (byte)0x00 });
    }

    public void WriteInteger(Asn1Tag tag, BigInteger value)
    {
        WritePrimitive(tag.AsPrimitive(), EncodeInteger(value));
    }

    public void WriteOctetString(Asn1Tag tag, ReadOnlySpan<byte> value)
    {
        WritePrimitive(tag.AsPrimitive(), value.ToArray());
    }

    public void WriteNull(Asn1Tag tag)
    {
        WritePrimitive(tag.AsPrimitive(), Array.Empty<byte>());
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
    /// Writes a SET OF. In DER mode, element encodings are sorted lexicographically (X.690 §11.6).
    /// </summary>
    public void WriteSetOf(Asn1Tag tag, IReadOnlyList<byte[]> encodedElements)
    {
        if (encodedElements is null)
        {
            throw new ArgumentNullException(nameof(encodedElements));
        }

        byte[][] parts;
        if (Encoding == Asn1Encoding.Der && encodedElements.Count > 1)
        {
            parts = new byte[encodedElements.Count][];
            for (var i = 0; i < encodedElements.Count; i++)
            {
                parts[i] = encodedElements[i] ?? throw new ArgumentNullException(nameof(encodedElements));
            }

            Array.Sort(parts, CompareDerSetOfEncodings);
        }
        else
        {
            parts = new byte[encodedElements.Count][];
            for (var i = 0; i < encodedElements.Count; i++)
            {
                parts[i] = encodedElements[i] ?? throw new ArgumentNullException(nameof(encodedElements));
            }
        }

        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var contents = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, contents, offset, part.Length);
            offset += part.Length;
        }

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

    private static int CompareDerSetOfEncodings(byte[] left, byte[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            var cmp = left[i].CompareTo(right[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private void WritePrimitive(Asn1Tag tag, byte[] contents)
    {
        WriteTlv(tag.AsPrimitive(), contents, definiteOnly: true);
    }

    private void WriteTlv(Asn1Tag tag, byte[] contents, bool definiteOnly)
    {
        WriteTag(tag);
        WriteLength(contents.Length, definiteOnly);
        _buffer.Write(contents, 0, contents.Length);
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

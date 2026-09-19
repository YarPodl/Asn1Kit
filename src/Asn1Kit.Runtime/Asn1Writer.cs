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
        WritePrimitive(tag.AsPrimitive(), EncodeOid(oid));
    }

    public void WriteSequence(Asn1Tag tag, Action<Asn1Writer> content)
    {
        var inner = new Asn1Writer(Encoding);
        content(inner);
        WriteTlv(tag.AsConstructed(), inner.Encode(), definiteOnly: Encoding == Asn1Encoding.Der);
    }

    public void WriteExplicit(Asn1Tag outer, Action<Asn1Writer> inner)
    {
        WriteSequence(outer, inner);
    }

    public void WriteRaw(ReadOnlySpan<byte> tlv)
    {
        _buffer.Write(tlv);
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

    internal static byte[] EncodeOid(string oid)
    {
        var parts = oid.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
        if (parts.Length < 2)
        {
            throw new Asn1Exception($"OID '{oid}' is too short.");
        }

        var output = new List<byte> { (byte)(40 * parts[0] + parts[1]) };
        for (var i = 2; i < parts.Length; i++)
        {
            EncodeBase128(output, parts[i]);
        }

        return output.ToArray();
    }

    private static void EncodeBase128(List<byte> output, int value)
    {
        if (value < 0)
        {
            throw new Asn1Exception("OID component cannot be negative.");
        }

        var stack = new Stack<byte>();
        stack.Push((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            stack.Push((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        while (stack.Count > 0)
        {
            output.Add(stack.Pop());
        }
    }
}

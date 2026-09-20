using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Asn1Kit.Runtime;

public sealed class Asn1Reader
{
    private readonly byte[] _data;
    private int _offset;
    private readonly int _end;

    public Asn1Reader(byte[] data, Asn1Encoding encoding = Asn1Encoding.Ber)
        : this(data, 0, data is null ? 0 : data.Length, encoding)
    {
    }

    public Asn1Reader(byte[] data, int offset, int length, Asn1Encoding encoding = Asn1Encoding.Ber)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if ((uint)offset > (uint)data.Length || (uint)length > (uint)(data.Length - offset))
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _data = data;
        _offset = offset;
        _end = offset + length;
        Encoding = encoding;
    }

    public Asn1Reader(ReadOnlyMemory<byte> data, Asn1Encoding encoding = Asn1Encoding.Ber)
    {
        if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment) && segment.Array is not null)
        {
            _data = segment.Array;
            _offset = segment.Offset;
            _end = segment.Offset + segment.Count;
        }
        else
        {
            _data = data.ToArray();
            _offset = 0;
            _end = _data.Length;
        }

        Encoding = encoding;
    }

    public Asn1Encoding Encoding { get; }

    public bool Eof => _offset >= _end;

    public bool TryPeekTag(out Asn1Tag tag)
    {
        if (Eof)
        {
            tag = default;
            return false;
        }

        var saved = _offset;
        tag = ReadTag();
        _offset = saved;
        return true;
    }

    public T ReadSequence<T>(Asn1Tag expected, Func<Asn1Reader, T> read)
    {
        var contents = ReadValue(expected, allowConstructed: true);
        var inner = new Asn1Reader(contents, Encoding);
        var result = read(inner);
        return result;
    }

    public void ReadSequence(Asn1Tag expected, Action<Asn1Reader> read) =>
        ReadSequence(expected, reader =>
        {
            read(reader);
            return 0;
        });

    public T ReadSet<T>(Asn1Tag expected, Func<Asn1Reader, T> read) => ReadSequence(expected, read);

    public void ReadSet(Asn1Tag expected, Action<Asn1Reader> read) => ReadSequence(expected, read);

    public bool ReadBoolean(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: false);
        if (contents.Length != 1)
        {
            throw new Asn1Exception("BOOLEAN must contain one octet.");
        }

        if (Encoding == Asn1Encoding.Der && contents[0] is not (0x00 or 0xFF))
        {
            throw new Asn1Exception("DER BOOLEAN must be 0x00 or 0xFF.");
        }

        return contents[0] != 0x00;
    }

    public BigInteger ReadInteger(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: false);
        if (contents.Length == 0)
        {
            throw new Asn1Exception("INTEGER contents must not be empty.");
        }

        var copy = new byte[contents.Length];
        Array.Copy(contents, copy, contents.Length);
        Array.Reverse(copy);
        return new BigInteger(copy);
    }

    /// <summary>ENUMERATED uses the same contents encoding as INTEGER (X.690).</summary>
    public BigInteger ReadEnumerated(Asn1Tag expected) => ReadInteger(expected);

    public byte[] ReadOctetString(Asn1Tag expected)
    {
        var (tag, contents, constructed) = ReadTlv();
        if (!tag.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {tag}.");
        }

        if (!constructed)
        {
            return contents;
        }

        var nested = new Asn1Reader(contents, Encoding);
        var parts = new List<byte>();
        while (!nested.Eof)
        {
            parts.AddRange(nested.ReadOctetString(Asn1Tag.OctetString));
        }

        return parts.ToArray();
    }

    public bool TryReadOctetString(Asn1Tag expected, Span<byte> destination, out int bytesWritten)
    {
        // Always advances past the TLV; on false the value is not copied (caller must re-read from a saved buffer).
        var value = ReadOctetString(expected);
        if (destination.Length < value.Length)
        {
            bytesWritten = 0;
            return false;
        }

        value.CopyTo(destination);
        bytesWritten = value.Length;
        return true;
    }

    public bool ReadNull(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: false);
        if (contents.Length != 0)
        {
            throw new Asn1Exception("NULL must have empty contents.");
        }

        return true;
    }

    public string ReadObjectIdentifier(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: false);
        if (contents.Length == 0)
        {
            throw new Asn1Exception("OBJECT IDENTIFIER is empty.");
        }

        var i = 0;
        var first = ReadOidArc(contents, ref i);
        int arc0;
        int arc1;
        if (first < 40)
        {
            arc0 = 0;
            arc1 = first;
        }
        else if (first < 80)
        {
            arc0 = 1;
            arc1 = first - 40;
        }
        else
        {
            arc0 = 2;
            arc1 = first - 80;
        }

        var builder = new StringBuilder();
        builder.Append(arc0);
        builder.Append('.');
        builder.Append(arc1);
        while (i < contents.Length)
        {
            builder.Append('.');
            builder.Append(ReadOidArc(contents, ref i));
        }

        return builder.ToString();
    }

    private static int ReadOidArc(byte[] contents, ref int i)
    {
        var value = 0;
        byte b;
        do
        {
            if (i >= contents.Length)
            {
                throw new Asn1Exception("Truncated OID.");
            }

            b = contents[i++];
            if (value > (int.MaxValue >> 7))
            {
                throw new Asn1Exception("OID arc is too large.");
            }

            value = (value << 7) | (b & 0x7F);
        } while ((b & 0x80) != 0);

        return value;
    }

    public Asn1BitString ReadBitString(Asn1Tag expected)
    {
        var (tag, contents, constructed) = ReadTlv();
        if (!tag.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {tag}.");
        }

        if (!constructed)
        {
            return ParsePrimitiveBitString(contents, Encoding == Asn1Encoding.Der);
        }

        var nested = new Asn1Reader(contents, Encoding);
        var parts = new List<byte>();
        var unusedBits = 0;
        var sawSegment = false;
        while (!nested.Eof)
        {
            var segment = nested.ReadBitString(Asn1Tag.BitString);
            if (sawSegment && unusedBits != 0)
            {
                throw new Asn1Exception("Only the last BIT STRING segment may have unused bits.");
            }

            parts.AddRange(segment.Span.ToArray());
            unusedBits = segment.UnusedBits;
            sawSegment = true;
        }

        if (!sawSegment)
        {
            throw new Asn1Exception("Constructed BIT STRING has no segments.");
        }

        var value = new Asn1BitString(parts.ToArray().AsSpan(), unusedBits);
        if (Encoding == Asn1Encoding.Der)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(value.Span, value.UnusedBits);
        }

        return value;
    }

    public string ReadString(Asn1Tag expected, Asn1StringForm form)
    {
        var bytes = ReadOctetLike(expected);
        return Asn1TextCodec.DecodeString(bytes, form);
    }

    public DateTimeOffset ReadTime(Asn1Tag expected, Asn1TimeForm form)
    {
        var bytes = ReadOctetLike(expected);
        var text = Asn1TextCodec.DecodeString(bytes, Asn1StringForm.Visible);
        return Asn1TextCodec.ParseTime(text, form, Encoding);
    }

    public byte[] ReadValue(Asn1Tag expected, bool allowConstructed)
    {
        var (tag, contents, constructed) = ReadTlv();
        if (!tag.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {tag}.");
        }

        if (constructed && !allowConstructed)
        {
            throw new Asn1Exception($"Tag {expected} must be primitive.");
        }

        return contents;
    }

    public bool TryReadValue(Asn1Tag expected, bool allowConstructed, Span<byte> destination, out int bytesWritten)
    {
        var value = ReadValue(expected, allowConstructed);
        if (destination.Length < value.Length)
        {
            bytesWritten = 0;
            return false;
        }

        value.CopyTo(destination);
        bytesWritten = value.Length;
        return true;
    }

    /// <summary>Reads the next complete TLV as ANY (tag + value octets).</summary>
    public Asn1Any ReadAny()
    {
        var (tag, contents, _) = ReadTlv();
        return new Asn1Any(tag, contents.AsSpan());
    }

    /// <summary>Reads ANY expecting a specific tag (IMPLICIT); returns the wire tag and contents.</summary>
    public Asn1Any ReadAny(Asn1Tag expected)
    {
        var (tag, contents, _) = ReadTlv();
        if (!tag.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {tag}.");
        }

        return new Asn1Any(tag, contents.AsSpan());
    }

    public (Asn1Tag Tag, byte[] Contents, bool Constructed) ReadTlv()
    {
        var tag = ReadTag();
        var (length, indefinite) = ReadLength();
        byte[] contents;
        if (indefinite)
        {
            if (Encoding == Asn1Encoding.Der)
            {
                throw new Asn1Exception("Indefinite length is not allowed in DER.");
            }

            var start = _offset;
            while (true)
            {
                if (_offset + 1 >= _end)
                {
                    throw new Asn1Exception("Unterminated indefinite length.");
                }

                if (_data[_offset] == 0x00 && _data[_offset + 1] == 0x00)
                {
                    contents = _data[start.._offset];
                    _offset += 2;
                    break;
                }

                _ = ReadTlv();
            }
        }
        else
        {
            if (_offset + length > _end)
            {
                throw new Asn1Exception("Length exceeds buffer.");
            }

            contents = _data[_offset..(_offset + length)];
            _offset += length;
        }

        return (tag, contents, tag.Constructed);
    }

    private Asn1Tag ReadTag()
    {
        EnsureAvailable(1);
        var first = _data[_offset++];
        var tagClass = (Asn1TagClass)((first & 0xC0) >> 6);
        var constructed = (first & 0x20) != 0;
        var number = first & 0x1F;
        if (number == 0x1F)
        {
            number = 0;
            byte b;
            do
            {
                EnsureAvailable(1);
                b = _data[_offset++];
                number = (number << 7) | (b & 0x7F);
            } while ((b & 0x80) != 0);
        }

        return new Asn1Tag(tagClass, number, constructed);
    }

    private (int Length, bool Indefinite) ReadLength()
    {
        EnsureAvailable(1);
        var first = _data[_offset++];
        if (first == 0x80)
        {
            return (0, true);
        }

        if ((first & 0x80) == 0)
        {
            return (first, false);
        }

        var count = first & 0x7F;
        if (count == 0 || count > 4)
        {
            throw new Asn1Exception("Unsupported length form.");
        }

        EnsureAvailable(count);
        var length = 0;
        for (var i = 0; i < count; i++)
        {
            length = (length << 8) | _data[_offset++];
        }

        return (length, false);
    }

    private void EnsureAvailable(int count)
    {
        if (_offset + count > _end)
        {
            throw new Asn1Exception("Unexpected end of ASN.1 data.");
        }
    }

    private byte[] ReadOctetLike(Asn1Tag expected)
    {
        var (tag, contents, constructed) = ReadTlv();
        if (!tag.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {tag}.");
        }

        if (!constructed)
        {
            return contents;
        }

        var nested = new Asn1Reader(contents, Encoding);
        var parts = new List<byte>();
        while (!nested.Eof)
        {
            parts.AddRange(nested.ReadOctetLike(expected.AsPrimitive()));
        }

        return parts.ToArray();
    }

    private static Asn1BitString ParsePrimitiveBitString(byte[] contents, bool derStrict)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("BIT STRING contents must not be empty.");
        }

        var unusedBits = contents[0];
        if (unusedBits > 7)
        {
            throw new Asn1Exception("BIT STRING unusedBits must be in 0..7.");
        }

        if (contents.Length == 1)
        {
            if (unusedBits != 0)
            {
                throw new Asn1Exception("Empty BIT STRING must have unusedBits = 0.");
            }

            return default;
        }

        var bytes = contents.AsSpan(1).ToArray();
        if (derStrict)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(bytes, unusedBits);
        }

        return new Asn1BitString(bytes.AsSpan(), unusedBits);
    }
}

using System.Buffers;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;

namespace Asn1Kit.Runtime;

public sealed class Asn1Reader
{
    private readonly byte[] _data;
    private int _offset;
    private int _end;
    private int _start;

    public Asn1Reader(byte[] data, Asn1Encoding encoding = Asn1Encoding.Ber, Asn1ReaderOptions? options = null)
        : this(data, 0, data is null ? 0 : data.Length, encoding, options)
    {
    }

    public Asn1Reader(
        byte[] data,
        int offset,
        int length,
        Asn1Encoding encoding = Asn1Encoding.Ber,
        Asn1ReaderOptions? options = null)
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
        _start = offset;
        _offset = offset;
        _end = offset + length;
        Encoding = encoding;
        Options = options ?? Asn1ReaderOptions.Default;
    }

    public Asn1Reader(ReadOnlyMemory<byte> data, Asn1Encoding encoding = Asn1Encoding.Ber, Asn1ReaderOptions? options = null)
    {
        if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment) && segment.Array is not null)
        {
            _data = segment.Array;
            _start = segment.Offset;
            _offset = segment.Offset;
            _end = segment.Offset + segment.Count;
        }
        else
        {
            _data = data.ToArray();
            _start = 0;
            _offset = 0;
            _end = _data.Length;
        }

        Encoding = encoding;
        Options = options ?? Asn1ReaderOptions.Default;
    }

    public Asn1Encoding Encoding { get; }

    /// <summary>Strictness flags for this reader (and nested readers created from it).</summary>
    public Asn1ReaderOptions Options { get; }

    /// <summary>Window into the underlying buffer this reader was constructed over (lifetime anchor for views).</summary>
    public ReadOnlyMemory<byte> Source => _data.AsMemory(_start, _end - _start);

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

    /// <summary>
    /// Consumes a constructed SEQUENCE/SET TLV and restricts this reader to its contents.
    /// Restore the previous window via <see cref="Asn1ReaderCursor.Dispose"/> (or <c>using</c>).
    /// </summary>
    public Asn1ReaderCursor EnterSequence(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: true);
        return PushContentsWindow(contents);
    }

    /// <summary>Same as <see cref="EnterSequence"/> (SET is wire-identical to SEQUENCE for nesting).</summary>
    public Asn1ReaderCursor EnterSet(Asn1Tag expected) => EnterSequence(expected);

    /// <summary>
    /// Consumes an EXPLICIT constructed wrapper TLV and restricts this reader to its contents.
    /// Wire-identical to <see cref="EnterSequence"/>; named for symmetry with <c>WriteExplicit</c>.
    /// </summary>
    public Asn1ReaderCursor EnterExplicit(Asn1Tag expected) => EnterSequence(expected);

    /// <summary>
    /// Consumes the next complete TLV, eagerly decodes it, and retains the TLV for bit-exact re-encode.
    /// </summary>
    public Asn1Retained<T> ReadRetained<T>(Func<Asn1Reader, T> decode)
    {
        if (decode is null)
        {
            throw new ArgumentNullException(nameof(decode));
        }

        var start = _offset;
        _ = ReadTlv();
        var encoded = _data.AsMemory(start, _offset - start);
        using (PushContentsWindow(encoded))
        {
            var value = decode(this);
            return Asn1Retained<T>.Wrap(encoded, value);
        }
    }

    /// <summary>
    /// Restricts this reader to <paramref name="contents"/> when it aliases the backing buffer.
    /// Restore via <see cref="Asn1ReaderCursor.Dispose"/> or <see cref="PopContentsWindow"/>.
    /// </summary>
    internal Asn1ReaderCursor PushContentsWindow(ReadOnlyMemory<byte> contents)
    {
        if (!TryGetAliasedRange(contents, out var contentStart, out var contentEnd))
        {
            throw new Asn1Exception("Contents window must alias the reader buffer.");
        }

        var cursor = Asn1ReaderCursor.Create(this, _start, _offset, _end);
        _start = contentStart;
        _offset = contentStart;
        _end = contentEnd;
        return cursor;
    }

    internal void PopContentsWindow(int savedStart, int savedOffset, int savedEnd)
    {
        _start = savedStart;
        _offset = savedOffset;
        _end = savedEnd;
    }

    private bool TryGetAliasedRange(ReadOnlyMemory<byte> contents, out int start, out int end)
    {
        if (MemoryMarshal.TryGetArray(contents, out ArraySegment<byte> segment) &&
            ReferenceEquals(segment.Array, _data))
        {
            start = segment.Offset;
            end = segment.Offset + segment.Count;
            return true;
        }

        start = 0;
        end = 0;
        return false;
    }

    /// <summary>
    /// Reads a SEQUENCE OF into a new array, decoding elements until the contents are exhausted.
    /// Empty OF returns <see cref="Array.Empty{T}"/>.
    /// </summary>
    public T[] ReadSequenceOf<T>(Asn1Tag expected, Func<Asn1Reader, T> decodeItem)
    {
        if (decodeItem is null)
        {
            throw new ArgumentNullException(nameof(decodeItem));
        }

        // EnterSequence (not ReadSequence+lambda) avoids a per-call closure capturing decodeItem.
        using var cursor = EnterSequence(expected);
        if (Eof)
        {
            return Array.Empty<T>();
        }

        // Common OF size is 1 (RDN SET OF AVA): avoid pool round-trip.
        var first = decodeItem(this);
        if (Eof)
        {
            return new[] { first };
        }

        var rented = ArrayPool<T>.Shared.Rent(8);
        var count = 0;
        try
        {
            rented[count++] = first;
            while (!Eof)
            {
                if (count == rented.Length)
                {
                    var grown = ArrayPool<T>.Shared.Rent(rented.Length * 2);
                    Array.Copy(rented, grown, count);
                    ArrayPool<T>.Shared.Return(rented, clearArray: true);
                    rented = grown;
                }

                rented[count++] = decodeItem(this);
            }

            var result = new T[count];
            Array.Copy(rented, result, count);
            return result;
        }
        finally
        {
            ArrayPool<T>.Shared.Return(rented, clearArray: true);
        }
    }

    /// <summary>Reads a SET OF; same as <see cref="ReadSequenceOf{T}"/> (order is wire order).</summary>
    public T[] ReadSetOf<T>(Asn1Tag expected, Func<Asn1Reader, T> decodeItem) =>
        ReadSequenceOf(expected, decodeItem);

    public bool ReadBoolean(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: false).Span;
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

    public BigInteger ReadInteger(Asn1Tag expected) => ReadSignedIntegerContents(expected);

    /// <summary>Reads INTEGER contents (preserves wire bytes; may alias <see cref="Source"/>).</summary>
    public Asn1Integer ReadIntegerValue(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: false);
        EnsureMinimalIntegerContents(contents.Span);
        return Asn1Integer.FromContents(contents);
    }

    public int ReadInt32(Asn1Tag expected)
    {
        var value = ReadIntegerValue(expected);
        if (!value.TryGetInt32(out var number))
        {
            throw new Asn1Exception("INTEGER value does not fit in Int32.");
        }

        return number;
    }

    public uint ReadUInt32(Asn1Tag expected)
    {
        var value = ReadIntegerValue(expected);
        if (!value.TryGetUInt32(out var number))
        {
            throw new Asn1Exception("INTEGER value does not fit in UInt32.");
        }

        return number;
    }

    public long ReadInt64(Asn1Tag expected)
    {
        var value = ReadIntegerValue(expected);
        if (!value.TryGetInt64(out var number))
        {
            throw new Asn1Exception("INTEGER value does not fit in Int64.");
        }

        return number;
    }

    public ulong ReadUInt64(Asn1Tag expected)
    {
        var value = ReadIntegerValue(expected);
        if (!value.TryGetUInt64(out var number))
        {
            throw new Asn1Exception("INTEGER value does not fit in UInt64.");
        }

        return number;
    }

    /// <summary>ENUMERATED uses the same contents encoding as INTEGER (X.690).</summary>
    public BigInteger ReadEnumerated(Asn1Tag expected) => ReadSignedIntegerContents(expected);

    private BigInteger ReadSignedIntegerContents(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: false).Span;
        EnsureMinimalIntegerContents(contents);
        return Asn1Integer.ToBigInteger(contents);
    }

    private void EnsureMinimalIntegerContents(ReadOnlySpan<byte> contents)
    {
        if (!Options.RejectNonMinimalInteger)
        {
            return;
        }

        if (!IsMinimalIntegerContents(contents))
        {
            throw new Asn1Exception("INTEGER contents are not minimally encoded.");
        }
    }

    /// <summary>X.690 §8.3.2 — no unnecessary leading 0x00 / 0xFF octets.</summary>
    internal static bool IsMinimalIntegerContents(ReadOnlySpan<byte> contents)
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

    public ReadOnlyMemory<byte> ReadOctetString(Asn1Tag expected)
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

        return ConcatOctetLike(contents, Asn1Tag.OctetString);
    }

    public bool TryReadOctetString(Asn1Tag expected, Span<byte> destination, out int bytesWritten)
    {
        // Always advances past the TLV; on false the value is not copied (caller must re-read from a saved buffer).
        var (tag, contents, constructed) = ReadTlv();
        if (!tag.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {tag}.");
        }

        if (!constructed)
        {
            if (destination.Length < contents.Length)
            {
                bytesWritten = 0;
                return false;
            }

            contents.Span.CopyTo(destination);
            bytesWritten = contents.Length;
            return true;
        }

        return TryCopyConcatOctetLike(contents, Asn1Tag.OctetString, destination, out bytesWritten);
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

    public Asn1Oid ReadOid(Asn1Tag expected)
    {
        var contents = ReadValue(expected, allowConstructed: false);
        if (contents.Length == 0)
        {
            throw new Asn1Exception("OBJECT IDENTIFIER is empty.");
        }

        // Validate base-128 arcs (including overlong reject) without building a string.
        var span = contents.Span;
        var i = 0;
        while (i < span.Length)
        {
            _ = Asn1Oid.ReadArc(span, ref i, Options.RejectOverlongOidBase128);
        }

        return Asn1Oid.FromContents(contents);
    }

    public string ReadObjectIdentifier(Asn1Tag expected) => ReadOid(expected).ToString();

    public Asn1BitString ReadBitString(Asn1Tag expected)
    {
        var (tag, contents, constructed) = ReadTlv();
        if (!tag.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {tag}.");
        }

        if (!constructed)
        {
            return ParsePrimitiveBitString(contents, Options.RejectBitStringTrailingBits);
        }

        using (PushContentsWindow(contents))
        {
            var segments = new List<Asn1BitString>();
            var unusedBits = 0;
            while (!Eof)
            {
                var segment = ReadBitString(Asn1Tag.BitString);
                if (segments.Count > 0 && unusedBits != 0)
                {
                    throw new Asn1Exception("Only the last BIT STRING segment may have unused bits.");
                }

                segments.Add(segment);
                unusedBits = segment.UnusedBits;
            }

            if (segments.Count == 0)
            {
                throw new Asn1Exception("Constructed BIT STRING has no segments.");
            }

            return ConcatBitStringSegments(segments, unusedBits);
        }
    }

    private Asn1BitString ConcatBitStringSegments(List<Asn1BitString> segments, int unusedBits)
    {
        var total = 0;
        foreach (var segment in segments)
        {
            total += segment.Span.Length;
        }

        var concatenated = total == 0 ? Array.Empty<byte>() : new byte[total];
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.Span.CopyTo(concatenated.AsSpan(offset));
            offset += segment.Span.Length;
        }

        var value = new Asn1BitString(concatenated, unusedBits);
        if (Options.RejectBitStringTrailingBits)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(value.Span, value.UnusedBits);
        }

        return value;
    }

    public string ReadString(Asn1Tag expected, Asn1StringForm form)
    {
        var bytes = ReadOctetLike(expected);
        return Asn1TextCodec.DecodeString(bytes.Span, form);
    }

    public DateTimeOffset ReadTime(Asn1Tag expected, Asn1TimeForm form)
    {
        var bytes = ReadOctetLike(expected);
        return Asn1TextCodec.ParseTime(bytes.Span, form, Encoding);
    }

    public ReadOnlyMemory<byte> ReadValue(Asn1Tag expected, bool allowConstructed)
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

        value.Span.CopyTo(destination);
        bytesWritten = value.Length;
        return true;
    }

    /// <summary>
    /// Consumes the next complete TLV without decoding its contents; materialization runs
    /// <paramref name="decode"/> on first <see cref="Asn1Lazy{T}.Value"/> access.
    /// </summary>
    public Asn1Lazy<T> ReadLazy<T>(Func<Asn1Reader, T> decode)
    {
        if (decode is null)
        {
            throw new ArgumentNullException(nameof(decode));
        }

        var start = _offset;
        _ = ReadTlv();
        var encoded = _data.AsMemory(start, _offset - start);
        return Asn1Lazy<T>.Wrap(encoded, Encoding, Options, decode);
    }

    /// <summary>Reads the next complete TLV as ANY (full encoded TLV including tag and length).</summary>
    public Asn1Any ReadAny()
    {
        var start = _offset;
        var (tag, contents, _) = ReadTlv();
        var encoded = _data.AsMemory(start, _offset - start);
        return Asn1Any.Wrap(tag, encoded, contents);
    }

    /// <summary>Reads ANY expecting a specific tag (IMPLICIT); stores the wire TLV as-is.</summary>
    public Asn1Any ReadAny(Asn1Tag expected)
    {
        var start = _offset;
        var (tag, contents, _) = ReadTlv();
        if (!tag.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {tag}.");
        }

        var encoded = _data.AsMemory(start, _offset - start);
        return Asn1Any.Wrap(tag, encoded, contents);
    }

    public (Asn1Tag Tag, ReadOnlyMemory<byte> Contents, bool Constructed) ReadTlv()
    {
        var tag = ReadTag();
        var (length, indefinite) = ReadLength();
        ReadOnlyMemory<byte> contents;
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
                    contents = _data.AsMemory(start, _offset - start);
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

            contents = _data.AsMemory(_offset, length);
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
        if (Options.RejectNonMinimalLength && _data[_offset] == 0x00)
        {
            throw new Asn1Exception("Non-minimal length encoding.");
        }

        var length = 0;
        for (var i = 0; i < count; i++)
        {
            length = (length << 8) | _data[_offset++];
        }

        if (Options.RejectNonMinimalLength && length < 128)
        {
            throw new Asn1Exception("Non-minimal length encoding.");
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

    private ReadOnlyMemory<byte> ReadOctetLike(Asn1Tag expected)
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

        return ConcatOctetLike(contents, expected.AsPrimitive());
    }

    private ReadOnlyMemory<byte> ConcatOctetLike(ReadOnlyMemory<byte> constructedContents, Asn1Tag segmentTag)
    {
        var segments = CollectOctetLikeSegments(constructedContents, segmentTag, out var total);
        if (total == 0)
        {
            return Array.Empty<byte>();
        }

        var result = new byte[total];
        CopySegments(segments, result);
        return result;
    }

    private bool TryCopyConcatOctetLike(
        ReadOnlyMemory<byte> constructedContents,
        Asn1Tag segmentTag,
        Span<byte> destination,
        out int bytesWritten)
    {
        var segments = CollectOctetLikeSegments(constructedContents, segmentTag, out var total);
        if (destination.Length < total)
        {
            bytesWritten = 0;
            return false;
        }

        CopySegments(segments, destination);
        bytesWritten = total;
        return true;
    }

    private List<ReadOnlyMemory<byte>> CollectOctetLikeSegments(
        ReadOnlyMemory<byte> constructedContents,
        Asn1Tag segmentTag,
        out int totalLength)
    {
        using (PushContentsWindow(constructedContents))
        {
            var list = new List<ReadOnlyMemory<byte>>();
            var length = 0;
            while (!Eof)
            {
                var segment = ReadOctetLike(segmentTag);
                list.Add(segment);
                length += segment.Length;
            }

            totalLength = length;
            return list;
        }
    }

    private static void CopySegments(List<ReadOnlyMemory<byte>> segments, Span<byte> destination)
    {
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.Span.CopyTo(destination.Slice(offset));
            offset += segment.Length;
        }
    }

    private static Asn1BitString ParsePrimitiveBitString(ReadOnlyMemory<byte> contents, bool rejectTrailingBits)
    {
        if (contents.Length == 0)
        {
            throw new Asn1Exception("BIT STRING contents must not be empty.");
        }

        var unusedBits = contents.Span[0];
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

        var bytes = contents.Slice(1);
        if (rejectTrailingBits)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(bytes.Span, unusedBits);
        }

        return new Asn1BitString(bytes, unusedBits);
    }
}

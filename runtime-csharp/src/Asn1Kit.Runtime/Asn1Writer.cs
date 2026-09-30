using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>Callback that consumes encoded octets without allocating a copy.</summary>
public delegate TReturn Asn1EncodeFunc<TReturn>(ReadOnlySpan<byte> encoded);

/// <summary>Callback that consumes encoded octets without allocating a copy.</summary>
public delegate void Asn1EncodeAction(ReadOnlySpan<byte> encoded);

/// <summary>Public BER/DER encode facade.</summary>
public sealed class Asn1Writer
{
    /// <summary>INTEGER / string contents larger than this use a heap buffer instead of stackalloc.</summary>
    private const int StackEncodeThreshold = 64;

    private Asn1EncodeBuffer _buffer = new();

    /// <summary>Initializes a new instance of <c>Asn1Writer</c>.</summary>
    public Asn1Writer(Asn1Encoding encoding = Asn1Encoding.Der)
    {
        Encoding = encoding;
    }

    /// <summary>Gets the <c>Encoding</c> value.</summary>
    public Asn1Encoding Encoding { get; }

    /// <summary>Gets the <c>EncodedLength</c> value.</summary>
    public int EncodedLength => _buffer.Length;

    /// <summary>Ensures the internal buffer can hold at least <paramref name="capacity"/> octets without further growth.</summary>
    public void EnsureCapacity(int capacity) => _buffer.EnsureCapacity(capacity);

    /// <summary>Clears written bytes so the writer can be reused without reallocating the backing store.</summary>
    public void Reset() => _buffer.Reset();

    /// <summary>Encodes the ASN.1 value.</summary>
    public byte[] Encode() => _buffer.ToArray();

    /// <summary>Provides the encoded representation to <paramref name="encodeCallback"/> without allocating a copy.</summary>
    public T Encode<T>(Asn1EncodeFunc<T> encodeCallback)
    {
        if (encodeCallback is null)
        {
            throw new ArgumentNullException(nameof(encodeCallback));
        }

        return encodeCallback(_buffer.WrittenSpan);
    }

    /// <summary>Provides the encoded representation to <paramref name="encodeCallback"/> without allocating a copy.</summary>
    public void Encode(Asn1EncodeAction encodeCallback)
    {
        if (encodeCallback is null)
        {
            throw new ArgumentNullException(nameof(encodeCallback));
        }

        encodeCallback(_buffer.WrittenSpan);
    }

    /// <summary>Attempts to encode.</summary>
    public bool TryEncode(Span<byte> destination, out int bytesWritten) =>
        _buffer.TryCopyTo(destination, out bytesWritten);

    /// <summary>Writes boolean to the ASN.1 output.</summary>
    public void WriteBoolean(Asn1Tag tag, bool value)
    {
        Span<byte> contents = stackalloc byte[1];
        contents[0] = value ? (byte)0xFF : (byte)0x00;
        _buffer.WritePrimitive(tag, contents);
    }

    /// <summary>Writes integer to the ASN.1 output.</summary>
    public void WriteInteger(Asn1Tag tag, BigInteger value)
    {
        var byteCount = Asn1Integer.GetEncodedByteCount(value);
        if (byteCount <= StackEncodeThreshold)
        {
            Span<byte> contents = stackalloc byte[byteCount];
            var written = Asn1Integer.EncodeContents(value, contents);
            _buffer.WritePrimitive(tag, contents.Slice(0, written));
            return;
        }

        var contentsArray = new byte[byteCount];
        var writtenLarge = Asn1Integer.EncodeContents(value, contentsArray);
        _buffer.WritePrimitive(tag, contentsArray.AsSpan(0, writtenLarge));
    }

    /// <summary>Writes integer to the ASN.1 output.</summary>
    public void WriteInteger(Asn1Tag tag, int value)
    {
        Span<byte> contents = stackalloc byte[4];
        var written = Asn1Integer.EncodeContents(value, contents);
        _buffer.WritePrimitive(tag, contents.Slice(0, written));
    }

    /// <summary>Writes integer to the ASN.1 output.</summary>
    public void WriteInteger(Asn1Tag tag, uint value)
    {
        Span<byte> contents = stackalloc byte[5];
        var written = Asn1Integer.EncodeContents(value, contents);
        _buffer.WritePrimitive(tag, contents.Slice(0, written));
    }

    /// <summary>Writes integer to the ASN.1 output.</summary>
    public void WriteInteger(Asn1Tag tag, long value)
    {
        Span<byte> contents = stackalloc byte[8];
        var written = Asn1Integer.EncodeContents(value, contents);
        _buffer.WritePrimitive(tag, contents.Slice(0, written));
    }

    /// <summary>Writes integer to the ASN.1 output.</summary>
    public void WriteInteger(Asn1Tag tag, ulong value)
    {
        Span<byte> contents = stackalloc byte[9];
        var written = Asn1Integer.EncodeContents(value, contents);
        _buffer.WritePrimitive(tag, contents.Slice(0, written));
    }

    /// <summary>Writes owned INTEGER contents as-is (may be non-minimal).</summary>
    public void WriteInteger(Asn1Tag tag, Asn1Integer value)
        => _buffer.WritePrimitive(tag, value.Span);

    /// <summary>ENUMERATED uses the same contents encoding as INTEGER (X.690).</summary>
    public void WriteEnumerated(Asn1Tag tag, BigInteger value) => WriteInteger(tag, value);

    /// <summary>Writes octet string to the ASN.1 output.</summary>
    public void WriteOctetString(Asn1Tag tag, ReadOnlySpan<byte> value) =>
        _buffer.WritePrimitive(tag, value);

    /// <summary>Writes null to the ASN.1 output.</summary>
    public void WriteNull(Asn1Tag tag) =>
        _buffer.WritePrimitive(tag, ReadOnlySpan<byte>.Empty);

    /// <summary>Writes object identifier to the ASN.1 output.</summary>
    public void WriteObjectIdentifier(Asn1Tag tag, string oid)
    {
        var maxBytes = Asn1Oid.GetEncodeContentsMaxLength(oid);
        Span<byte> contents = maxBytes <= 128 ? stackalloc byte[maxBytes] : new byte[maxBytes];
        var written = Asn1Oid.EncodeContents(oid, contents);
        _buffer.WritePrimitive(tag, contents.Slice(0, written));
    }

    /// <summary>Writes object identifier to the ASN.1 output.</summary>
    public void WriteObjectIdentifier(Asn1Tag tag, Asn1Oid oid) =>
        _buffer.WritePrimitive(tag, oid.Span);

    /// <summary>Writes bit string to the ASN.1 output.</summary>
    public void WriteBitString(Asn1Tag tag, Asn1BitString value)
    {
        if (Encoding == Asn1Encoding.Der)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(value.Span, value.UnusedBits);
        }

        _buffer.WritePrimitive(tag, (byte)value.UnusedBits, value.Span);
    }

    /// <summary>Writes string to the ASN.1 output.</summary>
    public void WriteString(Asn1Tag tag, string value, Asn1StringForm form)
    {
        var byteCount = Asn1TextCodec.GetEncodedByteCount(value, form);
        if (byteCount <= StackEncodeThreshold)
        {
            Span<byte> contents = stackalloc byte[byteCount];
            Asn1TextCodec.EncodeString(value, form, contents);
            _buffer.WritePrimitive(tag, contents);
            return;
        }

        var contentsArray = new byte[byteCount];
        Asn1TextCodec.EncodeString(value, form, contentsArray);
        _buffer.WritePrimitive(tag, contentsArray);
    }

    /// <summary>Writes time to the ASN.1 output.</summary>
    public void WriteTime(Asn1Tag tag, DateTimeOffset value, Asn1TimeForm form, int fractionDigits = 3)
    {
        Span<byte> contents = stackalloc byte[Asn1TextCodec.MaxEncodedTimeBytes];
        var written = Asn1TextCodec.EncodeTime(value, form, fractionDigits, contents);
        _buffer.WritePrimitive(tag, contents.Slice(0, written));
    }

    /// <summary>Writes sequence to the ASN.1 output.</summary>
    public void WriteSequence(Asn1Tag tag, Action<Asn1Writer> content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        WriteConstructed(tag, content, sortDerSetOf: false);
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

    /// <summary>Writes set to the ASN.1 output.</summary>
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

        WriteConstructed(tag, content, sortDerSetOf: Encoding == Asn1Encoding.Der);
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

    /// <summary>Writes explicit to the ASN.1 output.</summary>
    public void WriteExplicit(Asn1Tag outer, Action<Asn1Writer> inner) =>
        WriteSequence(outer, inner);

    /// <summary>Writes raw to the ASN.1 output.</summary>
    public void WriteRaw(ReadOnlySpan<byte> tlv) => _buffer.WriteRaw(tlv);

    /// <summary>Writes ANY by appending the stored TLV as-is (bit-exact).</summary>
    public void WriteAny(Asn1Any value) => WriteRaw(value.EncodedMemory.Span);

    /// <summary>
    /// Writes ANY with an IMPLICIT outer tag: class/number from <paramref name="tag"/>,
    /// constructed flag preserved from <paramref name="value"/>; value octets taken from the stored TLV.
    /// </summary>
    public void WriteAny(Asn1Tag tag, Asn1Any value)
    {
        var wireTag = new Asn1Tag(tag.TagClass, tag.Number, value.Tag.Constructed);
        _buffer.WriteTlv(wireTag, value.ContentsMemory.Span);
    }

    private void WriteConstructed(Asn1Tag tag, Action<Asn1Writer> content, bool sortDerSetOf)
    {
        var frame = _buffer.BeginConstructed(tag);
        content(this);
        _buffer.EndConstructed(frame, sortDerSetOf);
    }
}

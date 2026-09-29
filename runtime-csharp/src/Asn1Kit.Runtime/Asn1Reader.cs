using System.Buffers;
using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>
/// BER/DER decode facade. Low-level TLV state is held inline in an allocation-free cursor.
/// </summary>
public sealed class Asn1Reader
{
    private Asn1DecodeCursor _cursor;

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

        if ((uint)offset > (uint)data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if ((uint)length > (uint)(data.Length - offset))
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        ValidateEncoding(encoding);
        var actualOptions = options ?? Asn1ReaderOptions.Default;
        _cursor = new Asn1DecodeCursor(data.AsMemory(offset, length), encoding, actualOptions);
    }

    public Asn1Reader(ReadOnlyMemory<byte> data, Asn1Encoding encoding = Asn1Encoding.Ber, Asn1ReaderOptions? options = null)
    {
        ValidateEncoding(encoding);
        var actualOptions = options ?? Asn1ReaderOptions.Default;
        _cursor = new Asn1DecodeCursor(data, encoding, actualOptions);
    }

    public Asn1Encoding Encoding => _cursor.Encoding;

    public Asn1ReaderOptions Options => _cursor.Options;

    public bool Eof => _cursor.Eof;

    public int Remaining => _cursor.Remaining;

    /// <summary>
    /// Peeks the next tag without advancing. Malformed tags throw without changing the reader position.
    /// </summary>
    public bool TryPeekTag(out Asn1Tag tag) => _cursor.TryPeekTag(out tag);

    /// <summary>Enters constructed SEQUENCE contents and restores the outer window on dispose.</summary>
    public Asn1ReaderScope EnterSequence(Asn1Tag expected) =>
        PushContentsWindow(ReadConstructedContents(expected));

    /// <summary>Enters constructed SET contents and restores the outer window on dispose.</summary>
    public Asn1ReaderScope EnterSet(Asn1Tag expected) =>
        PushContentsWindow(ReadConstructedContents(expected));

    /// <summary>Enters an EXPLICIT constructed wrapper and restores the outer window on dispose.</summary>
    public Asn1ReaderScope EnterExplicit(Asn1Tag expected) =>
        PushContentsWindow(ReadConstructedContents(expected));

    /// <summary>Consumes a TLV, eagerly decodes it, and retains its encoded bytes.</summary>
    public Asn1Retained<T> ReadRetained<T>(Func<Asn1Reader, T> decode)
    {
        if (decode is null)
        {
            throw new ArgumentNullException(nameof(decode));
        }

        var tlv = _cursor.ReadTlv();
        using (PushContentsWindow(tlv.Encoded))
        {
            var value = decode(this);
            return Asn1Retained<T>.Wrap(tlv.Encoded, value);
        }
    }

    public T[] ReadSequenceOf<T>(Asn1Tag expected, Func<Asn1Reader, T> decodeItem)
    {
        if (decodeItem is null)
        {
            throw new ArgumentNullException(nameof(decodeItem));
        }

        using var scope = EnterSequence(expected);
        if (Eof)
        {
            return Array.Empty<T>();
        }

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

    public T[] ReadSetOf<T>(Asn1Tag expected, Func<Asn1Reader, T> decodeItem) =>
        ReadSequenceOf(expected, decodeItem);

    public bool ReadBoolean(Asn1Tag expected) =>
        Asn1Boolean.DecodeContents(ReadPrimitiveContents(expected).Span, Encoding);

    public BigInteger ReadInteger(Asn1Tag expected) => ReadSignedIntegerContents(expected);

    /// <summary>Reads INTEGER contents without copying.</summary>
    public Asn1Integer ReadIntegerValue(Asn1Tag expected)
    {
        var contents = ReadPrimitiveContents(expected);
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

    public BigInteger ReadEnumerated(Asn1Tag expected) => ReadSignedIntegerContents(expected);

    public ReadOnlyMemory<byte> ReadOctetString(Asn1Tag expected) =>
        Asn1ConstructedDecoder.ReadOctetLike(ref _cursor, expected);

    /// <summary>
    /// Attempts to copy OCTET STRING contents. A short destination returns false without advancing.
    /// </summary>
    public bool TryReadOctetString(Asn1Tag expected, Span<byte> destination, out int bytesWritten)
    {
        var candidate = _cursor;
        if (!Asn1ConstructedDecoder.TryReadOctetLike(
                ref candidate,
                expected,
                destination,
                out bytesWritten))
        {
            return false;
        }

        _cursor = candidate;
        return true;
    }

    public void ReadNull(Asn1Tag expected)
    {
        var contents = ReadPrimitiveContents(expected);
        if (contents.Length != 0)
        {
            throw new Asn1Exception("NULL must have empty contents.");
        }
    }

    public Asn1Oid ReadOid(Asn1Tag expected)
    {
        var contents = ReadPrimitiveContents(expected);
        if (contents.Length == 0)
        {
            throw new Asn1Exception("OBJECT IDENTIFIER is empty.");
        }

        var span = contents.Span;
        var index = 0;
        while (index < span.Length)
        {
            _ = Asn1Oid.ReadArc(span, ref index, Options.RejectOverlongOidBase128);
        }

        return Asn1Oid.FromContents(contents);
    }

    public string ReadObjectIdentifier(Asn1Tag expected) => ReadOid(expected).ToString();

    public Asn1BitString ReadBitString(Asn1Tag expected) =>
        Asn1ConstructedDecoder.ReadBitString(
            ref _cursor,
            expected,
            Options.RejectBitStringTrailingBits);

    public string ReadString(Asn1Tag expected, Asn1StringForm form)
    {
        var bytes = Asn1ConstructedDecoder.ReadOctetLike(ref _cursor, expected);
        return Asn1TextCodec.DecodeString(bytes.Span, form);
    }

    public DateTimeOffset ReadTime(Asn1Tag expected, Asn1TimeForm form)
    {
        var bytes = Asn1ConstructedDecoder.ReadOctetLike(ref _cursor, expected);
        return Asn1TextCodec.ParseTime(bytes.Span, form, Encoding);
    }

    /// <summary>Consumes the next complete TLV and defers typed materialization.</summary>
    public Asn1Lazy<T> ReadLazy<T>(Func<Asn1Reader, T> decode)
    {
        if (decode is null)
        {
            throw new ArgumentNullException(nameof(decode));
        }

        var tlv = _cursor.ReadTlv();
        return Asn1Lazy<T>.Wrap(tlv.Encoded, Encoding, Options, decode);
    }

    /// <summary>Reads the next complete TLV as ANY.</summary>
    public Asn1Any ReadAny()
    {
        var tlv = _cursor.ReadTlv();
        return Asn1Any.Wrap(tlv.Tag, tlv.Encoded, tlv.Contents);
    }

    /// <summary>Reads ANY expecting a specific IMPLICIT tag.</summary>
    public Asn1Any ReadAny(Asn1Tag expected)
    {
        var tlv = _cursor.ReadTlv();
        EnsureExpectedTag(tlv.Tag, expected);
        return Asn1Any.Wrap(tlv.Tag, tlv.Encoded, tlv.Contents);
    }

    internal Asn1ReaderScope PushContentsWindow(ReadOnlyMemory<byte> contents)
    {
        var savedCursor = _cursor;
        _cursor = _cursor.CreateNested(contents);
        return Asn1ReaderScope.Create(this, savedCursor, _cursor.ScopeToken);
    }

    internal void PopContentsWindow(
        Asn1DecodeCursor savedCursor,
        Asn1ReaderScopeToken expectedScopeToken)
    {
        if (!_cursor.MatchesScope(expectedScopeToken))
        {
            throw new InvalidOperationException("ASN.1 reader scopes must be disposed once in LIFO order.");
        }

        _cursor = savedCursor;
    }

    private ReadOnlyMemory<byte> ReadPrimitiveContents(Asn1Tag expected)
    {
        var tlv = _cursor.ReadTlv();
        EnsureExpectedTag(tlv.Tag, expected);
        if (tlv.Tag.Constructed)
        {
            throw new Asn1Exception($"Tag {expected} must be primitive.");
        }

        return tlv.Contents;
    }

    private ReadOnlyMemory<byte> ReadConstructedContents(Asn1Tag expected)
    {
        var tlv = _cursor.ReadTlv();
        EnsureExpectedTag(tlv.Tag, expected);
        if (!tlv.Tag.Constructed)
        {
            throw new Asn1Exception($"Tag {expected} must be constructed.");
        }

        return tlv.Contents;
    }

    private BigInteger ReadSignedIntegerContents(Asn1Tag expected)
    {
        var contents = ReadPrimitiveContents(expected).Span;
        EnsureMinimalIntegerContents(contents);
        return Asn1Integer.ToBigInteger(contents);
    }

    private void EnsureMinimalIntegerContents(ReadOnlySpan<byte> contents)
    {
        if (Options.RejectNonMinimalInteger && !Asn1Integer.IsMinimalContents(contents))
        {
            throw new Asn1Exception("INTEGER contents are not minimally encoded.");
        }
    }

    private static void EnsureExpectedTag(Asn1Tag actual, Asn1Tag expected)
    {
        if (!actual.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {actual}.");
        }
    }

    private static void ValidateEncoding(Asn1Encoding encoding)
    {
        if (encoding is not (Asn1Encoding.Ber or Asn1Encoding.Der))
        {
            throw new ArgumentOutOfRangeException(nameof(encoding));
        }
    }
}

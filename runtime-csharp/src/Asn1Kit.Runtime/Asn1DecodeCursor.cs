namespace Asn1Kit.Runtime;

/// <summary>Parsed TLV slices over the cursor input.</summary>
internal readonly struct Asn1Tlv
{
    public Asn1Tlv(Asn1Tag tag, ReadOnlyMemory<byte> contents, ReadOnlyMemory<byte> encoded)
    {
        Tag = tag;
        Contents = contents;
        Encoded = encoded;
    }

    public Asn1Tag Tag { get; }

    public ReadOnlyMemory<byte> Contents { get; }

    public ReadOnlyMemory<byte> Encoded { get; }
}

/// <summary>Opaque identity of the decode window owned by one public reader scope.</summary>
internal readonly struct Asn1ReaderScopeToken
{
    private readonly ReadOnlyMemory<byte> _window;

    public Asn1ReaderScopeToken(ReadOnlyMemory<byte> window)
    {
        _window = window;
    }

    public bool Matches(ReadOnlyMemory<byte> window) => _window.Equals(window);
}

/// <summary>
/// Allocation-free TLV cursor over one decode window. Copies are independent bookmarks.
/// </summary>
internal struct Asn1DecodeCursor
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly Asn1Encoding _encoding;
    private readonly Asn1ReaderOptions _options;
    private int _offset;

    public Asn1DecodeCursor(
        ReadOnlyMemory<byte> data,
        Asn1Encoding encoding,
        Asn1ReaderOptions options)
    {
        _data = data;
        _encoding = encoding;
        _options = options;
        _offset = 0;
    }

    public Asn1Encoding Encoding => _encoding;

    public Asn1ReaderOptions Options => _options;

    public Asn1ReaderScopeToken ScopeToken => new(_data);

    public bool Eof => _offset >= _data.Length;

    public int Remaining => _data.Length - _offset;

    public bool MatchesScope(Asn1ReaderScopeToken token) => token.Matches(_data);

    public Asn1DecodeCursor CreateNested(ReadOnlyMemory<byte> contents) =>
        new(contents, _encoding, _options);

    public bool TryPeekTag(out Asn1Tag tag)
    {
        if (Eof)
        {
            tag = default;
            return false;
        }

        var copy = this;
        tag = copy.ReadTag();
        return true;
    }

    public Asn1Tlv ReadTlv()
    {
        var encodedStart = _offset;
        var tag = ReadTag();
        var (length, indefinite) = ReadLength();
        ReadOnlyMemory<byte> contents;

        if (indefinite)
        {
            if (_encoding == Asn1Encoding.Der)
            {
                throw new Asn1Exception("Indefinite length is not allowed in DER.");
            }

            if (!tag.Constructed)
            {
                throw new Asn1Exception("Indefinite length requires a constructed tag.");
            }

            contents = ReadIndefiniteContents();
        }
        else
        {
            if (length > Remaining)
            {
                throw new Asn1Exception("Length exceeds buffer.");
            }

            contents = _data.Slice(_offset, length);
            _offset += length;
        }

        return new Asn1Tlv(tag, contents, _data.Slice(encodedStart, _offset - encodedStart));
    }

    private ReadOnlyMemory<byte> ReadIndefiniteContents()
    {
        var contentsStart = _offset;
        while (true)
        {
            if (Remaining < 2)
            {
                throw new Asn1Exception("Unterminated indefinite length.");
            }

            var span = _data.Span;
            if (span[_offset] == 0x00 && span[_offset + 1] == 0x00)
            {
                var contents = _data.Slice(contentsStart, _offset - contentsStart);
                _offset += 2;
                return contents;
            }

            _ = ReadTlv();
        }
    }

    private Asn1Tag ReadTag()
    {
        EnsureAvailable(1);
        var span = _data.Span;
        var first = span[_offset++];
        var tagClass = (Asn1TagClass)((first & 0xC0) >> 6);
        var constructed = (first & 0x20) != 0;
        var number = first & 0x1F;

        if (number == 0x1F)
        {
            number = ReadHighTagNumber();
        }

        if (tagClass == Asn1TagClass.Universal && number == 0)
        {
            throw new Asn1Exception("Unexpected end-of-contents tag.");
        }

        return new Asn1Tag(tagClass, number, constructed);
    }

    private int ReadHighTagNumber()
    {
        EnsureAvailable(1);
        var span = _data.Span;
        var first = span[_offset];
        if (first == 0x80)
        {
            throw new Asn1Exception("High-tag-number form is not minimally encoded.");
        }

        var number = 0;
        byte current;
        do
        {
            EnsureAvailable(1);
            current = span[_offset++];
            var payload = current & 0x7F;
            if (number > (int.MaxValue - payload) / 128)
            {
                throw new Asn1Exception("Tag number exceeds Int32.");
            }

            number = (number * 128) + payload;
        } while ((current & 0x80) != 0);

        if (number < 31)
        {
            throw new Asn1Exception("High-tag-number form is not minimally encoded.");
        }

        return number;
    }

    private (int Length, bool Indefinite) ReadLength()
    {
        EnsureAvailable(1);
        var span = _data.Span;
        var first = span[_offset++];
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
        if (_options.RejectNonMinimalLength && span[_offset] == 0x00)
        {
            throw new Asn1Exception("Non-minimal length encoding.");
        }

        uint length = 0;
        for (var i = 0; i < count; i++)
        {
            length = (length << 8) | span[_offset++];
        }

        if (length > int.MaxValue)
        {
            throw new Asn1Exception("Length exceeds Int32.");
        }

        if (_options.RejectNonMinimalLength && length < 128)
        {
            throw new Asn1Exception("Non-minimal length encoding.");
        }

        return ((int)length, false);
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw new Asn1Exception("Unexpected end of ASN.1 data.");
        }
    }
}

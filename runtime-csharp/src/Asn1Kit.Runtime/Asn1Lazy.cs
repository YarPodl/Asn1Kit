namespace Asn1Kit.Runtime;

/// <summary>
/// Deferred decode of a complete TLV: the reader advances past the value immediately,
/// and <see cref="Value"/> runs the decoder only on first access.
/// </summary>
public sealed class Asn1Lazy<T>
{
    private readonly ReadOnlyMemory<byte> _encoded;
    private readonly Asn1Encoding _encoding;
    private readonly Asn1ReaderOptions _options;
    private readonly Func<Asn1Reader, T>? _decode;
    private readonly bool _hasEncoded;
    private T? _value;
    private bool _isMaterialized;

    private Asn1Lazy(
        ReadOnlyMemory<byte> encoded,
        Asn1Encoding encoding,
        Asn1ReaderOptions options,
        Func<Asn1Reader, T>? decode,
        T? value,
        bool hasEncoded,
        bool isMaterialized)
    {
        _encoded = encoded;
        _encoding = encoding;
        _options = options;
        _decode = decode;
        _value = value;
        _hasEncoded = hasEncoded;
        _isMaterialized = isMaterialized;
    }

    /// <summary>
    /// Builds a lazy value from a complete TLV (views may alias the reader buffer).
    /// </summary>
    public static Asn1Lazy<T> FromEncoded(
        ReadOnlyMemory<byte> encoded,
        Asn1Encoding encoding,
        Asn1ReaderOptions options,
        Func<Asn1Reader, T> decode)
    {
        if (decode is null)
        {
            throw new ArgumentNullException(nameof(decode));
        }

        if (encoded.Length == 0)
        {
            throw new Asn1Exception("Lazy encoded TLV must not be empty.");
        }

        return new Asn1Lazy<T>(
            encoded,
            encoding,
            options ?? Asn1ReaderOptions.Default,
            decode,
            value: default,
            hasEncoded: true,
            isMaterialized: false);
    }

    /// <summary>
    /// Builds a lazy value from an already-materialized object (encode-only; no stored TLV).
    /// </summary>
    public static Asn1Lazy<T> FromValue(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new Asn1Lazy<T>(
            ReadOnlyMemory<byte>.Empty,
            Asn1Encoding.Der,
            Asn1ReaderOptions.Default,
            decode: null,
            value,
            hasEncoded: false,
            isMaterialized: true);
    }

    /// <summary>Trusted wrap used by <see cref="Asn1Reader.ReadLazy{T}"/>.</summary>
    internal static Asn1Lazy<T> Wrap(
        ReadOnlyMemory<byte> encoded,
        Asn1Encoding encoding,
        Asn1ReaderOptions options,
        Func<Asn1Reader, T> decode) =>
        FromEncoded(encoded, encoding, options, decode);

    /// <summary>Whether a complete TLV is available for bit-exact re-encode.</summary>
    public bool HasEncoded => _hasEncoded;

    /// <summary>Complete TLV as memory (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlyMemory<byte> EncodedMemory =>
        _hasEncoded
            ? _encoded
            : throw new InvalidOperationException("Lazy value was constructed from an object and has no encoded TLV.");

    /// <summary>Whether <see cref="Value"/> has already been decoded (or was supplied via <see cref="FromValue"/>).</summary>
    public bool IsMaterialized => _isMaterialized;

    /// <summary>Decodes the stored TLV on first access and caches the result.</summary>
    public T Value
    {
        get
        {
            if (_isMaterialized)
            {
                return _value!;
            }

            if (_decode is null)
            {
                throw new InvalidOperationException("Lazy value has no decoder.");
            }

            var reader = new Asn1Reader(_encoded, _encoding, _options);
            var decoded = _decode(reader);
            if (!reader.Eof)
            {
                throw new Asn1Exception("Lazy decoder did not consume the complete TLV.");
            }

            _value = decoded;
            _isMaterialized = true;
            return decoded;
        }
    }

    /// <summary>Detaches the complete TLV into a new array.</summary>
    public byte[] ToArray() => EncodedMemory.ToArray();

    /// <summary>
    /// Returns a copy that does not alias an external buffer.
    /// If already materialized without encoded bytes, returns <see cref="FromValue"/>.
    /// </summary>
    public Asn1Lazy<T> Clone()
    {
        if (_hasEncoded)
        {
            return new Asn1Lazy<T>(
                _encoded.ToArray(),
                _encoding,
                _options,
                _decode,
                _value,
                hasEncoded: true,
                isMaterialized: _isMaterialized);
        }

        return FromValue(_value!);
    }

    /// <summary>
    /// Writes the stored TLV when present; otherwise throws — callers that used
    /// <see cref="FromValue"/> must encode via <see cref="Value"/> themselves.
    /// </summary>
    public void WriteTo(Asn1Writer writer)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (!_hasEncoded)
        {
            throw new InvalidOperationException(
                "Lazy value has no encoded TLV; encode Value explicitly.");
        }

        writer.WriteRaw(_encoded.Span);
    }
}

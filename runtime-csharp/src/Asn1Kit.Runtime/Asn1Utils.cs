namespace Asn1Kit.Runtime;

/// <summary>
/// Warm top-level helpers that wrap <see cref="Asn1Reader"/> / <see cref="Asn1Writer"/> for complete TLV values.
/// Mirrors C++ <c>asn1kit::encode</c> / <c>asn1kit::decode</c>; codegen does not emit calls here.
/// </summary>
public static class Asn1Utils
{
    /// <summary>
    /// Decodes one complete value from <paramref name="data"/> and rejects trailing bytes.
    /// </summary>
    public static T Decode<T>(
        ReadOnlyMemory<byte> data,
        Func<Asn1Reader, T> decode,
        Asn1Encoding encoding = Asn1Encoding.Ber,
        Asn1ReaderOptions? options = null)
    {
        if (decode is null)
        {
            throw new ArgumentNullException(nameof(decode));
        }

        var reader = new Asn1Reader(data, encoding, options);
        var value = decode(reader);
        reader.ThrowIfNotEmpty();
        return value;
    }

    /// <summary>
    /// Decodes one complete value, retains its original TLV encoding, and rejects trailing bytes.
    /// </summary>
    public static Asn1Value<T> DecodeRetained<T>(
        ReadOnlyMemory<byte> data,
        Func<Asn1Reader, T> decode,
        Asn1Encoding encoding = Asn1Encoding.Der,
        Asn1ReaderOptions? options = null)
        where T : notnull
    {
        if (decode is null)
        {
            throw new ArgumentNullException(nameof(decode));
        }

        var reader = new Asn1Reader(data, encoding, options);
        var value = reader.ReadWithOriginalEncoding(decode);
        reader.ThrowIfNotEmpty();
        return value;
    }

    /// <summary>
    /// Encodes <paramref name="value"/> as a complete TLV. Equivalent to
    /// <c>writer.Encode()</c> after <see cref="IAsn1Encodable.Encode"/>.
    /// </summary>
    public static byte[] Encode<T>(T value, Asn1Encoding encoding = Asn1Encoding.Der)
        where T : IAsn1Encodable
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var writer = new Asn1Writer(encoding);
        value.Encode(writer);
        return writer.Encode();
    }

    /// <summary>
    /// Encodes <paramref name="value"/> into <paramref name="destination"/>.
    /// Returns <see langword="false"/> when the span is too small; <paramref name="bytesWritten"/> is then unspecified.
    /// </summary>
    public static bool TryEncode<T>(
        Span<byte> destination,
        T value,
        out int bytesWritten,
        Asn1Encoding encoding = Asn1Encoding.Der)
        where T : IAsn1Encodable
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var writer = new Asn1Writer(encoding);
        value.Encode(writer);
        return writer.TryEncode(destination, out bytesWritten);
    }

    /// <summary>
    /// Encodes <paramref name="value"/> using an explicit callback (for types that do not implement
    /// <see cref="IAsn1Encodable"/>, such as primitives).
    /// </summary>
    public static byte[] Encode<T>(
        T value,
        Action<Asn1Writer, T> encode,
        Asn1Encoding encoding = Asn1Encoding.Der)
    {
        if (encode is null)
        {
            throw new ArgumentNullException(nameof(encode));
        }

        var writer = new Asn1Writer(encoding);
        encode(writer, value);
        return writer.Encode();
    }
}

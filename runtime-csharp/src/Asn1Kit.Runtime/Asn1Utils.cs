namespace Asn1Kit.Runtime;

/// <summary>Warm top-level helpers that wrap <see cref="Asn1Reader"/> for complete TLV values.</summary>
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
}

using System.Formats.Asn1;
using System.Numerics;
using System.Text;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

/// <summary>
/// Thin adapter around <see cref="System.Formats.Asn1"/> for cross-oracle tests.
/// Only BER/DER — CER is out of Asn1Kit profile.
/// </summary>
internal static class DotnetAsnOracle
{
    private static readonly Encoding Utf32Be = new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);
    public static byte[] EncodeBoolean(bool value, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteBoolean(value);
        return writer.Encode();
    }

    public static bool DecodeBoolean(ReadOnlyMemory<byte> encoded, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        var value = reader.ReadBoolean();
        reader.ThrowIfNotEmpty();
        return value;
    }

    public static byte[] EncodeInteger(BigInteger value, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteInteger(value);
        return writer.Encode();
    }

    public static BigInteger DecodeInteger(ReadOnlyMemory<byte> encoded, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        var value = reader.ReadInteger();
        reader.ThrowIfNotEmpty();
        return value;
    }

    public static byte[] EncodeNull(AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteNull();
        return writer.Encode();
    }

    public static void DecodeNull(ReadOnlyMemory<byte> encoded, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        reader.ReadNull();
        reader.ThrowIfNotEmpty();
    }

    public static byte[] EncodeOctetString(ReadOnlySpan<byte> value, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteOctetString(value);
        return writer.Encode();
    }

    public static byte[] DecodeOctetString(ReadOnlyMemory<byte> encoded, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        var value = reader.ReadOctetString();
        reader.ThrowIfNotEmpty();
        return value;
    }

    public static byte[] EncodeObjectIdentifier(string oid, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteObjectIdentifier(oid);
        return writer.Encode();
    }

    public static string DecodeObjectIdentifier(ReadOnlyMemory<byte> encoded, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        var value = reader.ReadObjectIdentifier();
        reader.ThrowIfNotEmpty();
        return value;
    }

    public static byte[] EncodeBitString(ReadOnlySpan<byte> value, int unusedBits, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteBitString(value, unusedBits);
        return writer.Encode();
    }

    public static (byte[] Bytes, int UnusedBits) DecodeBitString(
        ReadOnlyMemory<byte> encoded,
        AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        var unused = 0;
        var bytes = reader.ReadBitString(out unused);
        reader.ThrowIfNotEmpty();
        return (bytes, unused);
    }

    public static byte[] EncodeCharacterString(
        UniversalTagNumber tagNumber,
        string value,
        AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteCharacterString(tagNumber, value);
        return writer.Encode();
    }

    public static string DecodeCharacterString(
        ReadOnlyMemory<byte> encoded,
        UniversalTagNumber tagNumber,
        AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        var value = reader.ReadCharacterString(tagNumber);
        reader.ThrowIfNotEmpty();
        return value;
    }

    public static byte[] EncodeUtcTime(DateTimeOffset value, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteUtcTime(value);
        return writer.Encode();
    }

    public static DateTimeOffset DecodeUtcTime(ReadOnlyMemory<byte> encoded, AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        var value = reader.ReadUtcTime();
        reader.ThrowIfNotEmpty();
        return value;
    }

    public static byte[] EncodeGeneralizedTime(
        DateTimeOffset value,
        bool omitFractionalSeconds = false,
        AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var writer = new AsnWriter(rules);
        writer.WriteGeneralizedTime(value, omitFractionalSeconds);
        return writer.Encode();
    }

    public static DateTimeOffset DecodeGeneralizedTime(
        ReadOnlyMemory<byte> encoded,
        AsnEncodingRules rules = AsnEncodingRules.DER)
    {
        var reader = new AsnReader(encoded, rules);
        var value = reader.ReadGeneralizedTime();
        reader.ThrowIfNotEmpty();
        return value;
    }

    public static AsnEncodingRules ToDotnet(Asn1Encoding encoding) => encoding switch
    {
        Asn1Encoding.Der => AsnEncodingRules.DER,
        Asn1Encoding.Ber => AsnEncodingRules.BER,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding))
    };

    public static UniversalTagNumber? TryMapStringForm(Asn1StringForm form) => form switch
    {
        Asn1StringForm.Utf8 => UniversalTagNumber.UTF8String,
        Asn1StringForm.Numeric => UniversalTagNumber.NumericString,
        Asn1StringForm.Printable => UniversalTagNumber.PrintableString,
        Asn1StringForm.Ia5 => UniversalTagNumber.IA5String,
        Asn1StringForm.Visible => UniversalTagNumber.VisibleString,
        Asn1StringForm.Bmp => UniversalTagNumber.BMPString,
        // UniversalString: BCL charset API unsupported on net6 — see EncodeUniversalStringUtf32Be.
        // Teletex/T61/Videotex/Graphic/General: Asn1Kit uses Latin-1; BCL charset differs — exclude from oracle.
        _ => null
    };

    /// <summary>
    /// Reference UniversalString DER (tag 28 + UTF-32BE). BCL WriteCharacterString does not accept UniversalString on net6.
    /// </summary>
    public static byte[] EncodeUniversalStringUtf32Be(string value)
    {
        var contents = Utf32Be.GetBytes(value);
        if (contents.Length > 127)
        {
            throw new InvalidOperationException("Test helper supports short-form length only.");
        }

        var result = new byte[2 + contents.Length];
        result[0] = 0x1C;
        result[1] = (byte)contents.Length;
        contents.CopyTo(result.AsSpan(2));
        return result;
    }

    public static string DecodeUniversalStringUtf32Be(ReadOnlyMemory<byte> encoded)
    {
        var span = encoded.Span;
        if (span.Length < 2 || span[0] != 0x1C || (span[1] & 0x80) != 0)
        {
            throw new InvalidOperationException("Expected short-form UniversalString TLV.");
        }

        var length = span[1];
        if (span.Length != 2 + length)
        {
            throw new InvalidOperationException("UniversalString TLV length mismatch.");
        }

        return Utf32Be.GetString(span.Slice(2, length));
    }
}



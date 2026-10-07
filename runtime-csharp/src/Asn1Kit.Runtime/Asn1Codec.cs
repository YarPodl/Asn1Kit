using System.Numerics;

namespace Asn1Kit.Runtime;

/// <summary>Reusable encoder/decoder pair for one ASN.1 value type.</summary>
public sealed class Asn1Codec<T>
{
    /// <summary>Creates a codec from reader and writer operations.</summary>
    public Asn1Codec(Func<Asn1Reader, T> decode, Action<Asn1Writer, T> encode)
    {
        DecodeReader = decode ?? throw new ArgumentNullException(nameof(decode));
        EncodeWriter = encode ?? throw new ArgumentNullException(nameof(encode));
    }

    internal Func<Asn1Reader, T> DecodeReader { get; }
    internal Action<Asn1Writer, T> EncodeWriter { get; }

    /// <summary>Decodes one complete BER TLV and rejects trailing data.</summary>
    public T Decode(Asn1Any raw) => raw.DecodeValue(DecodeReader);

    /// <summary>Encodes one value as a complete DER TLV.</summary>
    public Asn1Any Encode(T value) => Asn1Any.FromValue(value, EncodeWriter);
}

/// <summary>Shared codecs for ASN.1 primitive types used by generated open-type tables.</summary>
public static class Asn1Codecs
{
    /// <summary>Shared BOOLEAN codec.</summary>
    public static Asn1Codec<bool> Boolean { get; } = new(
        static reader => reader.ReadBoolean(Asn1Tag.Boolean),
        static (writer, value) => writer.WriteBoolean(Asn1Tag.Boolean, value));

    /// <summary>Shared INTEGER codec preserving DER contents.</summary>
    public static Asn1Codec<Asn1Integer> Integer { get; } = new(
        static reader => reader.ReadIntegerValue(Asn1Tag.Integer),
        static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));

    /// <summary>Shared Int32 INTEGER codec.</summary>
    public static Asn1Codec<int> Int32 { get; } = new(
        static reader => reader.ReadInt32(Asn1Tag.Integer),
        static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));

    /// <summary>Shared UInt32 INTEGER codec.</summary>
    public static Asn1Codec<uint> UInt32 { get; } = new(
        static reader => reader.ReadUInt32(Asn1Tag.Integer),
        static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));

    /// <summary>Shared Int64 INTEGER codec.</summary>
    public static Asn1Codec<long> Int64 { get; } = new(
        static reader => reader.ReadInt64(Asn1Tag.Integer),
        static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));

    /// <summary>Shared UInt64 INTEGER codec.</summary>
    public static Asn1Codec<ulong> UInt64 { get; } = new(
        static reader => reader.ReadUInt64(Asn1Tag.Integer),
        static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));

    /// <summary>Shared arbitrary-precision INTEGER codec.</summary>
    public static Asn1Codec<BigInteger> BigInteger { get; } = new(
        static reader => reader.ReadInteger(Asn1Tag.Integer),
        static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));

    /// <summary>Shared OCTET STRING codec.</summary>
    public static Asn1Codec<ReadOnlyMemory<byte>> OctetString { get; } = new(
        static reader => reader.ReadOctetString(Asn1Tag.OctetString),
        static (writer, value) => writer.WriteOctetString(Asn1Tag.OctetString, value.Span));

    /// <summary>Shared NULL codec.</summary>
    public static Asn1Codec<Asn1Null> Null { get; } = new(
        static reader => Asn1Null.Decode(reader),
        static (writer, value) => Asn1Null.Encode(writer, value));

    /// <summary>Shared OBJECT IDENTIFIER codec.</summary>
    public static Asn1Codec<Asn1Oid> ObjectIdentifier { get; } = new(
        static reader => reader.ReadOid(Asn1Tag.ObjectIdentifier),
        static (writer, value) => writer.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, value));

    /// <summary>Shared BIT STRING codec.</summary>
    public static Asn1Codec<Asn1BitString> BitString { get; } = new(
        static reader => reader.ReadBitString(Asn1Tag.BitString),
        static (writer, value) => writer.WriteBitString(Asn1Tag.BitString, value));

    /// <summary>Shared ANY codec.</summary>
    public static Asn1Codec<Asn1Any> Any { get; } = new(
        static reader => reader.ReadAny(),
        static (writer, value) => writer.WriteAny(value));

    /// <summary>Shared UTF8String codec.</summary>
    public static Asn1Codec<string> Utf8String { get; } = String(Asn1Tag.Utf8String, Asn1StringForm.Utf8);
    /// <summary>Shared PrintableString codec.</summary>
    public static Asn1Codec<string> PrintableString { get; } = String(Asn1Tag.PrintableString, Asn1StringForm.Printable);
    /// <summary>Shared TeletexString codec.</summary>
    public static Asn1Codec<string> TeletexString { get; } = String(Asn1Tag.TeletexString, Asn1StringForm.Teletex);
    /// <summary>Shared IA5String codec.</summary>
    public static Asn1Codec<string> Ia5String { get; } = String(Asn1Tag.Ia5String, Asn1StringForm.Ia5);
    /// <summary>Shared NumericString codec.</summary>
    public static Asn1Codec<string> NumericString { get; } = String(Asn1Tag.NumericString, Asn1StringForm.Numeric);
    /// <summary>Shared VisibleString codec.</summary>
    public static Asn1Codec<string> VisibleString { get; } = String(Asn1Tag.VisibleString, Asn1StringForm.Visible);
    /// <summary>Shared BMPString codec.</summary>
    public static Asn1Codec<string> BmpString { get; } = String(Asn1Tag.BmpString, Asn1StringForm.Bmp);
    /// <summary>Shared UniversalString codec.</summary>
    public static Asn1Codec<string> UniversalString { get; } = String(Asn1Tag.UniversalString, Asn1StringForm.Universal);
    /// <summary>Shared GeneralString codec.</summary>
    public static Asn1Codec<string> GeneralString { get; } = String(Asn1Tag.GeneralString, Asn1StringForm.General);
    /// <summary>Shared GraphicString codec.</summary>
    public static Asn1Codec<string> GraphicString { get; } = String(Asn1Tag.GraphicString, Asn1StringForm.Graphic);
    /// <summary>Shared VideotexString codec.</summary>
    public static Asn1Codec<string> VideotexString { get; } = String(Asn1Tag.VideotexString, Asn1StringForm.Videotex);

    /// <summary>Shared UTCTime codec.</summary>
    public static Asn1Codec<DateTimeOffset> UtcTime { get; } = Time(Asn1Tag.UtcTime, Asn1TimeForm.Utc, 3);
    /// <summary>Shared GeneralizedTime codec with millisecond precision.</summary>
    public static Asn1Codec<DateTimeOffset> GeneralizedTime { get; } = Time(Asn1Tag.GeneralizedTime, Asn1TimeForm.Generalized, 3);

    /// <summary>Decodes a typed value from OCTET STRING or aligned BIT STRING contents.</summary>
    public static T DecodeContained<T>(Asn1Contained<Asn1Any> raw, Asn1Codec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (raw.UnusedBits != 0)
            throw new Asn1Exception("Typed contained value must be octet-aligned.");
        return codec.Decode(raw.HasValue ? raw.Value : new Asn1Any(raw.Contents));
    }

    /// <summary>Encodes a typed value for OCTET STRING or BIT STRING CONTAINING.</summary>
    public static Asn1Contained<Asn1Any> EncodeContained<T>(T value, Asn1Codec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        return Asn1Contained<Asn1Any>.FromValue(codec.Encode(value));
    }

    /// <summary>Decodes raw open-type collection elements with one shared element codec.</summary>
    public static T[] DecodeEach<T>(Asn1Any[] raw, Asn1Codec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(codec);
        if (raw.Length == 0) return Array.Empty<T>();
        var result = new T[raw.Length];
        for (var i = 0; i < raw.Length; i++) result[i] = codec.Decode(raw[i]);
        return result;
    }

    /// <summary>Encodes open-type collection elements with one shared element codec.</summary>
    public static Asn1Any[] EncodeEach<T>(T[] value, Asn1Codec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(codec);
        if (value.Length == 0) return Array.Empty<Asn1Any>();
        var result = new Asn1Any[value.Length];
        for (var i = 0; i < value.Length; i++) result[i] = codec.Encode(value[i]);
        return result;
    }

    /// <summary>
    /// Decodes one complete string TLV whose form is listed in <paramref name="allowed"/>.
    /// Used by generated decoder-only open-type flattenings that discard the wire alternative.
    /// </summary>
    public static string DecodeStringChoice(Asn1Any raw, ReadOnlySpan<Asn1StringForm> allowed)
    {
        var reader = new Asn1Reader(raw.EncodedMemory, Asn1Encoding.Ber);
        if (!reader.TryPeekTag(out var tag))
            throw new Asn1Exception("Missing open-type value.");
        foreach (var form in allowed)
        {
            var expected = Asn1String.DefaultTag(form);
            if (!tag.MatchesIgnoreConstructed(expected)) continue;
            var decoded = reader.ReadString(expected, form);
            reader.ThrowIfNotEmpty();
            return decoded;
        }

        throw new Asn1Exception("Tag does not match the selected open-type binding.");
    }

    private static Asn1Codec<string> String(Asn1Tag tag, Asn1StringForm form) => new(
        reader => reader.ReadString(tag, form),
        (writer, value) => writer.WriteString(tag, value, form));

    private static Asn1Codec<DateTimeOffset> Time(Asn1Tag tag, Asn1TimeForm form, int fractionDigits) => new(
        reader => reader.ReadTime(tag, form),
        (writer, value) => writer.WriteTime(tag, value, form, fractionDigits));
}

using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

/// <summary>
/// Layer 1: own hex matrix from fixtures/ber-der (encode/decode/reject/round-trip).
/// </summary>
public sealed class PrimitiveCodecTests
{
    public static IEnumerable<object[]> BooleanCases() => BerDerFixtures.AsTheoryData("fixtures/ber-der/boolean.json");
    public static IEnumerable<object[]> NullCases() => BerDerFixtures.AsTheoryData("fixtures/ber-der/null.json");
    public static IEnumerable<object[]> IntegerCases() => BerDerFixtures.AsTheoryData("fixtures/ber-der/integer.json");
    public static IEnumerable<object[]> OctetCases() => BerDerFixtures.AsTheoryData("fixtures/ber-der/octet-string.json");
    public static IEnumerable<object[]> OidCases() => BerDerFixtures.AsTheoryData("fixtures/ber-der/oid.json");
    public static IEnumerable<object[]> BitStringCases() => BerDerFixtures.AsTheoryData("fixtures/ber-der/bit-string.json");
    public static IEnumerable<object[]> StringCases() => BerDerFixtures.AsTheoryData("fixtures/ber-der/string.json");
    public static IEnumerable<object[]> TimeCases() => BerDerFixtures.AsTheoryData("fixtures/ber-der/time.json");

    [Theory]
    [MemberData(nameof(BooleanCases))]
    public void Boolean_Fixture(BerDerCase c) => Run(c, EncodeBoolean, DecodeBoolean);

    [Theory]
    [MemberData(nameof(NullCases))]
    public void Null_Fixture(BerDerCase c) => Run(c, EncodeNull, DecodeNull);

    [Theory]
    [MemberData(nameof(IntegerCases))]
    public void Integer_Fixture(BerDerCase c) => Run(c, EncodeInteger, DecodeInteger);

    [Theory]
    [MemberData(nameof(OctetCases))]
    public void OctetString_Fixture(BerDerCase c) => Run(c, EncodeOctet, DecodeOctet);

    [Theory]
    [MemberData(nameof(OidCases))]
    public void ObjectIdentifier_Fixture(BerDerCase c) => Run(c, EncodeOid, DecodeOid);

    [Theory]
    [MemberData(nameof(BitStringCases))]
    public void BitString_Fixture(BerDerCase c) => Run(c, EncodeBitString, DecodeBitString);

    [Theory]
    [MemberData(nameof(StringCases))]
    public void String_Fixture(BerDerCase c) => Run(c, EncodeString, DecodeString);

    [Theory]
    [MemberData(nameof(TimeCases))]
    public void Time_Fixture(BerDerCase c) => Run(c, EncodeTime, DecodeTime);

    [Fact]
    public void Wrappers_SmokeEncodeDecode()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Boolean.Encode(writer, true);
        Asn1Null.Encode(writer);
        Asn1Integer.Encode(writer, 42);
        var bytes = writer.Encode();
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        Assert.True(Asn1Boolean.Decode(reader));
        Assert.True(Asn1Null.Decode(reader));
        Assert.Equal(42, Asn1Integer.Decode(reader));
        Assert.True(reader.Eof);
    }

    [Fact]
    public void WriteRaw_AppendsCompleteTlv()
    {
        var inner = new Asn1Writer(Asn1Encoding.Der);
        Asn1Integer.Encode(inner, 7);
        var tlv = inner.Encode();

        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequence(Asn1Tag.Sequence, w => w.WriteRaw(tlv));
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x30, 0x03, 0x02, 0x01, 0x07 }, bytes);
    }

    [Fact]
    public void OctetString_LongFormLength_RoundTrips()
    {
        var payload = new byte[128];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i & 0xFF);
        }

        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteOctetString(Asn1Tag.OctetString, payload);
        var bytes = writer.Encode();
        Assert.Equal(0x04, bytes[0]);
        Assert.Equal(0x81, bytes[1]);
        Assert.Equal(0x80, bytes[2]);
        Assert.Equal(payload, bytes.AsSpan(3).ToArray());

        var decoded = new Asn1Reader(bytes, Asn1Encoding.Der).ReadOctetString(Asn1Tag.OctetString);
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void ObjectIdentifier_LargeSecondArc_MatchesDer()
    {
        const string oid = "2.999.3";
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, oid);
        var bytes = writer.Encode();
        Assert.Equal(Hex.Parse("0603883703"), bytes);
        Assert.Equal(oid, new Asn1Reader(bytes, Asn1Encoding.Der).ReadObjectIdentifier(Asn1Tag.ObjectIdentifier));
    }

    [Fact]
    public void WriteExplicit_WrapsAsConstructed()
    {
        var tag = new Asn1Tag(Asn1TagClass.ContextSpecific, 0, constructed: true);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteExplicit(tag, inner => Asn1Integer.Encode(inner, 1));
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0xA0, 0x03, 0x02, 0x01, 0x01 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        reader.ReadSequence(tag, inner => Assert.Equal(1, Asn1Integer.Decode(inner)));
    }

    [Fact]
    public void ReadTlv_ReturnsOwnedContents()
    {
        var bytes = Hex.Parse("02012A");
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var (tag, contents, constructed) = reader.ReadTlv();
        Assert.Equal(Asn1Tag.Integer, tag);
        Assert.False(constructed);
        Assert.Equal(new byte[] { 0x2A }, contents);
        Assert.True(reader.Eof);
    }

    private static void Run(BerDerCase c, Action<BerDerCase, Asn1Writer> encode, Action<BerDerCase, Asn1Reader, byte[]> decode)
    {
        var expected = c.GetBytes();
        if (c.Reject)
        {
            var reader = new Asn1Reader(expected, c.Encoding);
            Assert.Throws<Asn1Exception>(() => decode(c, reader, expected));
            return;
        }

        if (c.ShouldEncode)
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            encode(c, writer);
            var encoded = writer.Encode();
            Assert.Equal(Hex.Format(expected), Hex.Format(encoded));

            var roundTrip = new Asn1Writer(Asn1Encoding.Der);
            encode(c, roundTrip);
            Assert.Equal(encoded, roundTrip.Encode());
        }

        var decodeReader = new Asn1Reader(expected, c.Encoding);
        decode(c, decodeReader, expected);
        Assert.True(decodeReader.Eof);
    }

    private static void EncodeBoolean(BerDerCase c, Asn1Writer w) => w.WriteBoolean(Asn1Tag.Boolean, BerDerFixtures.GetBoolean(c));

    private static void DecodeBoolean(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var value = r.ReadBoolean(Asn1Tag.Boolean);
        if (c.HasValue)
        {
            Assert.Equal(BerDerFixtures.GetBoolean(c), value);
        }
    }

    private static void EncodeNull(BerDerCase _, Asn1Writer w) => w.WriteNull(Asn1Tag.Null);

    private static void DecodeNull(BerDerCase _, Asn1Reader r, byte[] __) => Assert.True(r.ReadNull(Asn1Tag.Null));

    private static void EncodeInteger(BerDerCase c, Asn1Writer w) => w.WriteInteger(Asn1Tag.Integer, BerDerFixtures.GetInteger(c));

    private static void DecodeInteger(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var value = r.ReadInteger(Asn1Tag.Integer);
        if (c.HasValue)
        {
            Assert.Equal(BerDerFixtures.GetInteger(c), value);
        }
    }

    private static void EncodeOctet(BerDerCase c, Asn1Writer w) =>
        w.WriteOctetString(Asn1Tag.OctetString, BerDerFixtures.GetOctetValue(c));

    private static void DecodeOctet(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var value = r.ReadOctetString(Asn1Tag.OctetString);
        if (c.HasValue)
        {
            Assert.Equal(BerDerFixtures.GetOctetValue(c), value);
        }
    }

    private static void EncodeOid(BerDerCase c, Asn1Writer w) =>
        w.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, BerDerFixtures.GetString(c)!);

    private static void DecodeOid(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var value = r.ReadObjectIdentifier(Asn1Tag.ObjectIdentifier);
        if (c.HasValue)
        {
            Assert.Equal(BerDerFixtures.GetString(c), value);
        }
    }

    private static void EncodeBitString(BerDerCase c, Asn1Writer w)
    {
        var payload = BerDerFixtures.GetOctetValue(c);
        var unused = c.UnusedBits ?? 0;
        w.WriteBitString(Asn1Tag.BitString, new Asn1BitString(payload, unused));
    }

    private static void DecodeBitString(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var decoded = r.ReadBitString(Asn1Tag.BitString);
        if (c.HasValue)
        {
            Assert.Equal(c.UnusedBits ?? 0, decoded.UnusedBits);
            Assert.Equal(BerDerFixtures.GetOctetValue(c), decoded.Span.ToArray());
        }
    }

    private static void EncodeString(BerDerCase c, Asn1Writer w)
    {
        var form = ParseStringForm(c.Form!);
        w.WriteString(Asn1String.DefaultTag(form), BerDerFixtures.GetString(c)!, form);
    }

    private static void DecodeString(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var form = ParseStringForm(c.Form!);
        var value = r.ReadString(Asn1String.DefaultTag(form), form);
        if (c.HasValue)
        {
            Assert.Equal(BerDerFixtures.GetString(c), value);
        }
    }

    private static void EncodeTime(BerDerCase c, Asn1Writer w)
    {
        var form = ParseTimeForm(c.Form!);
        var digits = c.FractionDigits ?? 3;
        w.WriteTime(Asn1Time.DefaultTag(form), BerDerFixtures.GetTime(c), form, digits);
    }

    private static void DecodeTime(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var form = ParseTimeForm(c.Form!);
        var value = r.ReadTime(Asn1Time.DefaultTag(form), form);
        if (c.HasValue)
        {
            Assert.Equal(BerDerFixtures.GetTime(c), value);
        }
    }

    private static Asn1StringForm ParseStringForm(string form) =>
        Enum.Parse<Asn1StringForm>(form, ignoreCase: true);

    private static Asn1TimeForm ParseTimeForm(string form) =>
        Enum.Parse<Asn1TimeForm>(form, ignoreCase: true);
}

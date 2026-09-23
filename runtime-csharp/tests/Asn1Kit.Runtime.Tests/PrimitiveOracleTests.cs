using System.Formats.Asn1;
using System.Numerics;
using Asn1Kit.Runtime;
using Asn1Tag = Asn1Kit.Runtime.Asn1Tag;

namespace Asn1Kit.Tests;

/// <summary>
/// Layer 2: cross-oracle with System.Formats.Asn1 (Asn1Kit encode ↔ BCL decode and reverse).
/// </summary>
public sealed class PrimitiveOracleTests
{
    public static IEnumerable<object[]> Booleans() =>
        new object[] { false }.Concat(new object[] { true }).Select(v => new[] { v });

    public static IEnumerable<object[]> Integers()
    {
        BigInteger[] values =
        {
            0, 1, -1, 127, 128, -128, -129, 255, 256,
            BigInteger.Parse("170141183460469231731687303715884105728")
        };
        return values.Select(v => new object[] { v });
    }

    public static IEnumerable<object[]> Octets()
    {
        yield return new object[] { Array.Empty<byte>() };
        yield return new object[] { new byte[] { 0x41, 0x6E, 0x6E } };
        yield return new object[] { Enumerable.Repeat((byte)0x5A, 128).ToArray() };
    }

    public static IEnumerable<object[]> Oids()
    {
        yield return new object[] { "1.2.3" };
        yield return new object[] { "1.2.840.113549.1.1.1" };
        yield return new object[] { "2.16.840.1.101.3.4.2.1" };
        yield return new object[] { "2.999.3" };
    }

    public static IEnumerable<object[]> BitStrings()
    {
        yield return new object[] { Array.Empty<byte>(), 0 };
        yield return new object[] { new byte[] { 0xAA }, 0 };
        yield return new object[] { new byte[] { 0xA8 }, 3 };
        yield return new object[] { new byte[] { 0x80 }, 7 };
    }

    public static IEnumerable<object[]> CompatibleStrings()
    {
        yield return new object[] { Asn1StringForm.Utf8, "Hello" };
        yield return new object[] { Asn1StringForm.Printable, "ABC12" };
        yield return new object[] { Asn1StringForm.Ia5, "a@b.c" };
        yield return new object[] { Asn1StringForm.Numeric, "123 4" };
        yield return new object[] { Asn1StringForm.Visible, "Abc" };
        yield return new object[] { Asn1StringForm.Bmp, "AB" };
    }

    public static IEnumerable<object[]> UtcTimePivots()
    {
        yield return new object[] { new DateTimeOffset(2049, 1, 2, 3, 4, 5, TimeSpan.Zero) };
        yield return new object[] { new DateTimeOffset(1950, 1, 2, 3, 4, 5, TimeSpan.Zero) };
    }

    [Theory]
    [MemberData(nameof(Booleans))]
    public void Boolean_Der_BothDirections(bool value)
    {
        CrossDer(
            () => EncodeUs(w => w.WriteBoolean(Asn1Tag.Boolean, value)),
            () => DotnetAsnOracle.EncodeBoolean(value),
            bytes => Assert.Equal(value, new Asn1Reader(bytes, Asn1Encoding.Der).ReadBoolean(Asn1Tag.Boolean)),
            bytes => Assert.Equal(value, DotnetAsnOracle.DecodeBoolean(bytes)));
    }

    [Fact]
    public void Null_Der_BothDirections()
    {
        CrossDer(
            () => EncodeUs(w => w.WriteNull(Asn1Tag.Null)),
            () => DotnetAsnOracle.EncodeNull(),
            bytes => Assert.True(new Asn1Reader(bytes, Asn1Encoding.Der).ReadNull(Asn1Tag.Null)),
            bytes => DotnetAsnOracle.DecodeNull(bytes));
    }

    [Theory]
    [MemberData(nameof(Integers))]
    public void Integer_Der_BothDirections(BigInteger value)
    {
        CrossDer(
            () => EncodeUs(w => w.WriteInteger(Asn1Tag.Integer, value)),
            () => DotnetAsnOracle.EncodeInteger(value),
            bytes => Assert.Equal(value, new Asn1Reader(bytes, Asn1Encoding.Der).ReadInteger(Asn1Tag.Integer)),
            bytes => Assert.Equal(value, DotnetAsnOracle.DecodeInteger(bytes)));
    }

    [Theory]
    [MemberData(nameof(Octets))]
    public void OctetString_Der_BothDirections(byte[] value)
    {
        CrossDer(
            () => EncodeUs(w => w.WriteOctetString(Asn1Tag.OctetString, value)),
            () => DotnetAsnOracle.EncodeOctetString(value),
            bytes => Assert.Equal(value, new Asn1Reader(bytes, Asn1Encoding.Der).ReadOctetString(Asn1Tag.OctetString).ToArray()),
            bytes => Assert.Equal(value, DotnetAsnOracle.DecodeOctetString(bytes)));
    }

    [Theory]
    [MemberData(nameof(Oids))]
    public void ObjectIdentifier_Der_BothDirections(string oid)
    {
        CrossDer(
            () => EncodeUs(w => w.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, oid)),
            () => DotnetAsnOracle.EncodeObjectIdentifier(oid),
            bytes => Assert.Equal(oid, new Asn1Reader(bytes, Asn1Encoding.Der).ReadObjectIdentifier(Asn1Tag.ObjectIdentifier)),
            bytes => Assert.Equal(oid, DotnetAsnOracle.DecodeObjectIdentifier(bytes)));
    }

    [Theory]
    [MemberData(nameof(BitStrings))]
    public void BitString_Der_BothDirections(byte[] payload, int unusedBits)
    {
        CrossDer(
            () => EncodeUs(w => w.WriteBitString(Asn1Tag.BitString, new Asn1BitString(payload, unusedBits))),
            () => DotnetAsnOracle.EncodeBitString(payload, unusedBits),
            bytes =>
            {
                var decoded = new Asn1Reader(bytes, Asn1Encoding.Der).ReadBitString(Asn1Tag.BitString);
                Assert.Equal(unusedBits, decoded.UnusedBits);
                Assert.Equal(payload, decoded.Span.ToArray());
            },
            bytes =>
            {
                var (decoded, unused) = DotnetAsnOracle.DecodeBitString(bytes);
                Assert.Equal(unusedBits, unused);
                Assert.Equal(payload, decoded);
            });
    }

    [Theory]
    [MemberData(nameof(CompatibleStrings))]
    public void String_Der_BothDirections_CompatibleForms(Asn1StringForm form, string value)
    {
        var tagNumber = DotnetAsnOracle.TryMapStringForm(form)
            ?? throw new InvalidOperationException($"Form {form} is not BCL-compatible.");
        var tag = Asn1String.DefaultTag(form);

        CrossDer(
            () => EncodeUs(w => w.WriteString(tag, value, form)),
            () => DotnetAsnOracle.EncodeCharacterString(tagNumber, value),
            bytes => Assert.Equal(value, new Asn1Reader(bytes, Asn1Encoding.Der).ReadString(tag, form)),
            bytes => Assert.Equal(value, DotnetAsnOracle.DecodeCharacterString(bytes, tagNumber)));
    }

    [Fact]
    public void UniversalString_Der_MatchesUtf32BeReference_AndBclRawTlv()
    {
        // System.Formats.Asn1 (net6) has no UniversalString charset; oracle against X.690 UTF-32BE
        // contents and BCL raw TLV round-trip via ReadEncodedValue.
        const string value = "AB";
        var us = EncodeUs(w => w.WriteString(Asn1Tag.UniversalString, value, Asn1StringForm.Universal));
        var reference = DotnetAsnOracle.EncodeUniversalStringUtf32Be(value);
        Assert.Equal(Hex.Format(reference), Hex.Format(us));

        Assert.Equal(value, new Asn1Reader(us, Asn1Encoding.Der)
            .ReadString(Asn1Tag.UniversalString, Asn1StringForm.Universal));
        Assert.Equal(value, DotnetAsnOracle.DecodeUniversalStringUtf32Be(us));

        var bclReader = new AsnReader(us, AsnEncodingRules.DER);
        var raw = bclReader.ReadEncodedValue();
        bclReader.ThrowIfNotEmpty();
        Assert.Equal(Hex.Format(us), Hex.Format(raw.Span));
    }

    [Fact]
    public void UtcTime_Der_BothDirections()
    {
        var value = new DateTimeOffset(2017, 1, 2, 3, 4, 5, TimeSpan.Zero);
        CrossDer(
            () => EncodeUs(w => w.WriteTime(Asn1Tag.UtcTime, value, Asn1TimeForm.Utc)),
            () => DotnetAsnOracle.EncodeUtcTime(value),
            bytes => Assert.Equal(value, new Asn1Reader(bytes, Asn1Encoding.Der).ReadTime(Asn1Tag.UtcTime, Asn1TimeForm.Utc)),
            bytes => Assert.Equal(value, DotnetAsnOracle.DecodeUtcTime(bytes)));
    }

    [Theory]
    [MemberData(nameof(UtcTimePivots))]
    public void UtcTime_Der_YearPivot_BothDirections(DateTimeOffset value)
    {
        CrossDer(
            () => EncodeUs(w => w.WriteTime(Asn1Tag.UtcTime, value, Asn1TimeForm.Utc)),
            () => DotnetAsnOracle.EncodeUtcTime(value),
            bytes => Assert.Equal(value, new Asn1Reader(bytes, Asn1Encoding.Der).ReadTime(Asn1Tag.UtcTime, Asn1TimeForm.Utc)),
            bytes => Assert.Equal(value, DotnetAsnOracle.DecodeUtcTime(bytes)));
    }

    [Fact]
    public void GeneralizedTime_Der_BothDirections_WholeSeconds()
    {
        var value = new DateTimeOffset(2017, 1, 2, 3, 4, 5, TimeSpan.Zero);
        CrossDer(
            () => EncodeUs(w => w.WriteTime(Asn1Tag.GeneralizedTime, value, Asn1TimeForm.Generalized, fractionDigits: 0)),
            () => DotnetAsnOracle.EncodeGeneralizedTime(value, omitFractionalSeconds: true),
            bytes => Assert.Equal(value, new Asn1Reader(bytes, Asn1Encoding.Der).ReadTime(Asn1Tag.GeneralizedTime, Asn1TimeForm.Generalized)),
            bytes => Assert.Equal(value, DotnetAsnOracle.DecodeGeneralizedTime(bytes)));
    }

    [Fact]
    public void GeneralizedTime_Der_BothDirections_WithFraction()
    {
        // BCL WriteGeneralizedTime keeps up to millisecond precision; match with fractionDigits=3.
        var value = new DateTimeOffset(2017, 1, 2, 3, 4, 5, 120, TimeSpan.Zero);
        CrossDer(
            () => EncodeUs(w => w.WriteTime(Asn1Tag.GeneralizedTime, value, Asn1TimeForm.Generalized, fractionDigits: 3)),
            () => DotnetAsnOracle.EncodeGeneralizedTime(value, omitFractionalSeconds: false),
            bytes => Assert.Equal(value, new Asn1Reader(bytes, Asn1Encoding.Der).ReadTime(Asn1Tag.GeneralizedTime, Asn1TimeForm.Generalized)),
            bytes => Assert.Equal(value, DotnetAsnOracle.DecodeGeneralizedTime(bytes)));
    }

    [Fact]
    public void Ber_ConstructedOctetString_DecodableByBoth()
    {
        var ber = Hex.Parse("24800403416E6E0000");
        var us = new Asn1Reader(ber, Asn1Encoding.Ber).ReadOctetString(Asn1Tag.OctetString);
        var bcl = DotnetAsnOracle.DecodeOctetString(ber, AsnEncodingRules.BER);
        Assert.Equal(bcl, us.ToArray());
        Assert.Equal(new byte[] { 0x41, 0x6E, 0x6E }, us.ToArray());
    }

    [Fact]
    public void SetOf_Der_EncodeMatchesBclLexicographicOrder()
    {
        var us = EncodeUs(w =>
        {
            w.WriteSetOf(Asn1Tag.Set, inner =>
            {
                inner.WriteInteger(Asn1Tag.Integer, 2);
                inner.WriteInteger(Asn1Tag.Integer, 1);
            });
        });

        var bclWriter = new AsnWriter(AsnEncodingRules.DER);
        bclWriter.PushSetOf();
        bclWriter.WriteInteger(2);
        bclWriter.WriteInteger(1);
        bclWriter.PopSetOf();
        var bcl = bclWriter.Encode();

        Assert.Equal(Hex.Format(bcl), Hex.Format(us));
    }

    [Fact]
    public void Sequence_Der_BothDirections_NestedInteger()
    {
        var us = EncodeUs(w =>
        {
            w.WriteSequence(Asn1Tag.Sequence, inner =>
            {
                inner.WriteInteger(Asn1Tag.Integer, 1);
                inner.WriteInteger(Asn1Tag.Integer, 2);
            });
        });

        var bclWriter = new AsnWriter(AsnEncodingRules.DER);
        bclWriter.PushSequence();
        bclWriter.WriteInteger(1);
        bclWriter.WriteInteger(2);
        bclWriter.PopSequence();
        var bcl = bclWriter.Encode();

        Assert.Equal(Hex.Format(bcl), Hex.Format(us));

        var reader = new Asn1Reader(bcl, Asn1Encoding.Der);
        using (reader.EnterSequence(Asn1Tag.Sequence))
        {
            Assert.Equal(1, reader.ReadInteger(Asn1Tag.Integer));
            Assert.Equal(2, reader.ReadInteger(Asn1Tag.Integer));
        }
    }

    private static byte[] EncodeUs(Action<Asn1Writer> write)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        write(writer);
        return writer.Encode();
    }

    private static void CrossDer(
        Func<byte[]> encodeUs,
        Func<byte[]> encodeDotnet,
        Action<byte[]> decodeUs,
        Action<byte[]> decodeDotnet)
    {
        var usBytes = encodeUs();
        var dotnetBytes = encodeDotnet();
        Assert.Equal(Hex.Format(dotnetBytes), Hex.Format(usBytes));

        decodeUs(usBytes);
        decodeUs(dotnetBytes);
        decodeDotnet(usBytes);
        decodeDotnet(dotnetBytes);
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

/// <summary>
/// Layer 1: own hex matrix from fixtures/ber-der (encode/decode/reject/round-trip).
/// </summary>
public sealed class PrimitiveCodecTests
{
    public static IEnumerable<object[]> BooleanCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/boolean.json");
    public static IEnumerable<object[]> NullCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/null.json");
    public static IEnumerable<object[]> IntegerCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/integer.json");
    public static IEnumerable<object[]> EnumeratedCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/enumerated.json");
    public static IEnumerable<object[]> OctetCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/octet-string.json");
    public static IEnumerable<object[]> OidCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/oid.json");
    public static IEnumerable<object[]> BitStringCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/bit-string.json");
    public static IEnumerable<object[]> StringCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/string.json");
    public static IEnumerable<object[]> TimeCases() => BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/time.json");

    public static IEnumerable<object[]> Int32EncodingCases()
    {
        yield return new object[] { 127, "02017F" };
        yield return new object[] { 128, "02020080" };
        yield return new object[] { -128, "020180" };
        yield return new object[] { -129, "0202FF7F" };
        yield return new object[] { 255, "020200FF" };
        yield return new object[] { 256, "02020100" };
        yield return new object[] { int.MinValue, "020480000000" };
        yield return new object[] { int.MaxValue, "02047FFFFFFF" };
    }

    public static IEnumerable<object[]> UInt32EncodingCases()
    {
        yield return new object[] { 0u, "020100" };
        yield return new object[] { 127u, "02017F" };
        yield return new object[] { 128u, "02020080" };
        yield return new object[] { 255u, "020200FF" };
        yield return new object[] { 256u, "02020100" };
        yield return new object[] { uint.MaxValue, "020500FFFFFFFF" };
    }

    public static IEnumerable<object[]> Int64EncodingCases()
    {
        yield return new object[] { 127L, "02017F" };
        yield return new object[] { 128L, "02020080" };
        yield return new object[] { -128L, "020180" };
        yield return new object[] { -129L, "0202FF7F" };
        yield return new object[] { 255L, "020200FF" };
        yield return new object[] { 256L, "02020100" };
        yield return new object[] { long.MinValue, "02088000000000000000" };
        yield return new object[] { long.MaxValue, "02087FFFFFFFFFFFFFFF" };
    }

    public static IEnumerable<object[]> UInt64EncodingCases()
    {
        yield return new object[] { 0UL, "020100" };
        yield return new object[] { 127UL, "02017F" };
        yield return new object[] { 128UL, "02020080" };
        yield return new object[] { 255UL, "020200FF" };
        yield return new object[] { 256UL, "02020100" };
        yield return new object[] { ulong.MaxValue, "020900FFFFFFFFFFFFFFFF" };
    }

    public static IEnumerable<object[]> ConstructedLengthCases()
    {
        yield return new object[] { 0, "3000" };
        yield return new object[] { 1, "3001" };
        yield return new object[] { 127, "307F" };
        yield return new object[] { 128, "308180" };
        yield return new object[] { 254, "3081FE" };
        yield return new object[] { 255, "3081FF" };
        yield return new object[] { 256, "30820100" };
        yield return new object[] { 65_535, "3082FFFF" };
        yield return new object[] { 65_536, "3083010000" };
    }

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
    [MemberData(nameof(EnumeratedCases))]
    public void Enumerated_Fixture(BerDerCase c) => Run(c, EncodeEnumerated, DecodeEnumerated);

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
        Asn1Enumerated.Encode(writer, 3);
        var bytes = writer.Encode();
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        Assert.True(Asn1Boolean.Decode(reader));
        Assert.Equal(Asn1Null.Value, Asn1Null.Decode(reader));
        Assert.Equal(42, Asn1Integer.Decode(reader).GetInt32());
        Assert.Equal(3, Asn1Enumerated.Decode(reader));
        Assert.True(reader.Eof);
    }

    [Fact]
    public void IntegerValue_PreservesNonMinimalContents_OnWrite()
    {
        var soft = Asn1Integer.FromContents(new byte[] { 0x00, 0x01 });
        Assert.Equal(new byte[] { 0x00, 0x01 }, soft.Span.ToArray());
        Assert.Equal(1, soft.GetInt32());

        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteInteger(Asn1Tag.Integer, soft);
        Assert.Equal(new byte[] { 0x02, 0x02, 0x00, 0x01 }, writer.Encode());
    }

    [Fact]
    public void SoftInteger_DefaultAccepts_StrictRejects_EncodeStaysMinimal()
    {
        var softBytes = Hex.Parse("02020001");
        Assert.Equal(1, new Asn1Reader(softBytes, Asn1Encoding.Der).ReadInteger(Asn1Tag.Integer));
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(softBytes, Asn1Encoding.Der, Asn1ReaderOptions.Strict).ReadInteger(Asn1Tag.Integer));

        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteInteger(Asn1Tag.Integer, 1);
        Assert.Equal(Hex.Parse("020101"), writer.Encode());
    }

    [Fact]
    public void SoftBitStringTrailing_DefaultAccepts_StrictRejects()
    {
        var softBytes = Hex.Parse("030203A9");
        var decoded = new Asn1Reader(softBytes, Asn1Encoding.Der).ReadBitString(Asn1Tag.BitString);
        Assert.Equal(3, decoded.UnusedBits);
        Assert.Equal(new byte[] { 0xA9 }, decoded.Span.ToArray());

        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(softBytes, Asn1Encoding.Der, Asn1ReaderOptions.Strict)
                .ReadBitString(Asn1Tag.BitString));
    }

    [Fact]
    public void NonMinimalLength_DefaultRejects_AllowProfileAccepts()
    {
        var bytes = Hex.Parse("02810101");
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(bytes, Asn1Encoding.Der).ReadInteger(Asn1Tag.Integer));
        Assert.Equal(1, new Asn1Reader(bytes, Asn1Encoding.Der, Asn1ReaderOptions.AllowNonMinimalLength)
            .ReadInteger(Asn1Tag.Integer));
    }

    [Fact]
    public void OverlongOid_DefaultRejects_AllowProfileAccepts()
    {
        var bytes = Hex.Parse("06032A8001");
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(bytes, Asn1Encoding.Der).ReadObjectIdentifier(Asn1Tag.ObjectIdentifier));
        Assert.Equal("1.2.1",
            new Asn1Reader(bytes, Asn1Encoding.Der, Asn1ReaderOptions.AllowOverlongOidBase128)
                .ReadObjectIdentifier(Asn1Tag.ObjectIdentifier));
    }

    [Fact]
    public void IntegerValue_FromBigInteger_IsCanonical()
    {
        var value = Asn1Integer.FromBigInteger(1);
        Assert.Equal(new byte[] { 0x01 }, value.Span.ToArray());
    }

    [Fact]
    public void IntegerValue_DefaultAndZero_AreCanonicalZero()
    {
        Assert.Equal(default(Asn1Integer), Asn1Integer.FromInt32(0));
        Assert.Equal(Asn1Integer.Zero, Asn1Integer.FromInt32(0));
        Assert.Equal(Asn1Integer.Zero, Asn1Integer.FromBigInteger(0));
        Assert.Equal(new byte[] { 0x00 }, Asn1Integer.Zero.Span.ToArray());
        Assert.Equal(0, Asn1Integer.Zero.GetInt32());

        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteInteger(Asn1Tag.Integer, default(Asn1Integer));
        Assert.Equal(new byte[] { 0x02, 0x01, 0x00 }, writer.Encode());
    }

    [Fact]
    public void IntegerValue_SingleOctetFactories_DoNotAllocate()
    {
        _ = Asn1Integer.FromInt32(-128).Span;
        _ = Asn1Integer.FromUInt32(127).Span;
        _ = Asn1Integer.FromInt64(-1).Span;
        _ = Asn1Integer.FromUInt64(1).Span;
        _ = Asn1Integer.FromBigInteger(new BigInteger(42)).Span;

        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = -128; i <= 127; i++)
        {
            checksum += Asn1Integer.FromInt32(i).GetInt32();
            checksum += Asn1Integer.FromInt64(i).GetInt32();
            checksum += Asn1Integer.FromBigInteger(new BigInteger(i)).GetInt32();
            if (i >= 0)
            {
                checksum += Asn1Integer.FromUInt32((uint)i).GetInt32();
                checksum += Asn1Integer.FromUInt64((ulong)i).GetInt32();
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(15_872, checksum);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void IntegerValue_SingleOctetMemoryIsStable_AndCopiesDetach()
    {
        var negative = Asn1Integer.FromInt32(-128);
        var positive = Asn1Integer.FromInt64(127);
        Assert.True(MemoryMarshal.TryGetArray(negative.Memory, out ArraySegment<byte> negativeSegment));
        Assert.True(MemoryMarshal.TryGetArray(positive.Memory, out ArraySegment<byte> positiveSegment));
        Assert.Same(negativeSegment.Array, positiveSegment.Array);

        var detached = negative.ToArray();
        var clone = negative.Clone();
        detached[0] = 0;
        Assert.Equal(new byte[] { 0x80 }, negative.Span.ToArray());
        Assert.Equal(new byte[] { 0x80 }, clone.Span.ToArray());
    }

    [Fact]
    public void ReadInt32_RejectsOutOfRange()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteInteger(Asn1Tag.Integer, (long)int.MaxValue + 1);
        var bytes = writer.Encode();
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        Assert.Throws<Asn1Exception>(() => reader.ReadInt32(Asn1Tag.Integer));
    }

    [Fact]
    public void ReadUInt32_RejectsNegative()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteInteger(Asn1Tag.Integer, -1);
        var bytes = writer.Encode();
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        Assert.Throws<Asn1Exception>(() => reader.ReadUInt32(Asn1Tag.Integer));
    }

    [Fact]
    public void Integer_FixedWidth_RoundTrips()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteInteger(Asn1Tag.Integer, -7);
        writer.WriteInteger(Asn1Tag.Integer, 3u);
        writer.WriteInteger(Asn1Tag.Integer, long.MinValue);
        writer.WriteInteger(Asn1Tag.Integer, ulong.MaxValue);
        var bytes = writer.Encode();
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        Assert.Equal(-7, reader.ReadInt32(Asn1Tag.Integer));
        Assert.Equal(3u, reader.ReadUInt32(Asn1Tag.Integer));
        Assert.Equal(long.MinValue, reader.ReadInt64(Asn1Tag.Integer));
        Assert.Equal(ulong.MaxValue, reader.ReadUInt64(Asn1Tag.Integer));
    }

    [Theory]
    [MemberData(nameof(Int32EncodingCases))]
    public void Integer_Int32Encoding_IsCanonical(int value, string expectedHex) =>
        AssertIntegerEncoding(
            new BigInteger(value),
            Asn1Integer.FromInt32(value),
            writer => writer.WriteInteger(Asn1Tag.Integer, value),
            expectedHex);

    [Theory]
    [MemberData(nameof(UInt32EncodingCases))]
    public void Integer_UInt32Encoding_IsCanonical(uint value, string expectedHex) =>
        AssertIntegerEncoding(
            new BigInteger(value),
            Asn1Integer.FromUInt32(value),
            writer => writer.WriteInteger(Asn1Tag.Integer, value),
            expectedHex);

    [Theory]
    [MemberData(nameof(Int64EncodingCases))]
    public void Integer_Int64Encoding_IsCanonical(long value, string expectedHex) =>
        AssertIntegerEncoding(
            new BigInteger(value),
            Asn1Integer.FromInt64(value),
            writer => writer.WriteInteger(Asn1Tag.Integer, value),
            expectedHex);

    [Theory]
    [MemberData(nameof(UInt64EncodingCases))]
    public void Integer_UInt64Encoding_IsCanonical(ulong value, string expectedHex) =>
        AssertIntegerEncoding(
            new BigInteger(value),
            Asn1Integer.FromUInt64(value),
            writer => writer.WriteInteger(Asn1Tag.Integer, value),
            expectedHex);

    [Theory]
    [InlineData(new byte[] { 0x00 }, 0)]
    [InlineData(new byte[] { 0x7F }, 127)]
    [InlineData(new byte[] { 0x00, 0x80 }, 128)]
    [InlineData(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF }, int.MaxValue)]
    [InlineData(new byte[] { 0x80, 0x00, 0x00, 0x00 }, int.MinValue)]
    [InlineData(new byte[] { 0xFF, 0x80, 0x00, 0x00, 0x00 }, int.MinValue)]
    public void Integer_TryGetInt32_MatchesContents(byte[] contents, int expected)
    {
        var value = Asn1Integer.FromContents(contents);
        Assert.True(value.TryGetInt32(out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x80, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0x7F, 0xFF, 0xFF, 0xFF })]
    public void Integer_TryGetInt32_RejectsOutOfRangeContents(byte[] contents)
    {
        Assert.False(Asn1Integer.FromContents(contents).TryGetInt32(out _));
    }

    [Fact]
    public void Integer_TryGetUInt32_AcceptsMaxValue()
    {
        var value = Asn1Integer.FromContents(new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.True(value.TryGetUInt32(out var number));
        Assert.Equal(uint.MaxValue, number);
    }

    [Fact]
    public void Integer_TryGetInt32_DoesNotAllocate()
    {
        var value = Asn1Integer.FromContents(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
        // Warm up
        Assert.True(value.TryGetInt32(out _));

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(value.TryGetInt32(out _));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WriteRaw_AppendsCompleteTlv()
    {
        var inner = new Asn1Writer(Asn1Encoding.Der);
        Asn1Integer.Encode(inner, 7);
        var tlv = inner.Encode();

        var writer = new Asn1Writer(Asn1Encoding.Der);
        using (writer.EnterSequence(Asn1Tag.Sequence))
        {
            writer.WriteRaw(tlv);
        }
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x30, 0x03, 0x02, 0x01, 0x07 }, bytes);
    }

    [Fact]
    public void Writer_Reset_ReusesCapacityAndClearsOutput()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteInteger(Asn1Tag.Integer, 1);
        var first = writer.Encode();
        Assert.Equal(new byte[] { 0x02, 0x01, 0x01 }, first);

        writer.Reset();
        Assert.Equal(0, writer.EncodedLength);
        writer.WriteInteger(Asn1Tag.Integer, 2);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x02 }, writer.Encode());
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
        Assert.Equal(payload, decoded.ToArray());
    }

    [Theory]
    [MemberData(nameof(ConstructedLengthCases))]
    public void ConstructedLength_UsesMinimalDefiniteForm(int contentLength, string expectedHeaderHex)
    {
        var contents = new byte[contentLength];
        var writer = new Asn1Writer(Asn1Encoding.Der);
        using (writer.EnterSequence(Asn1Tag.Sequence))
        {
            writer.WriteRaw(contents);
        }
        var encoded = writer.Encode();
        var expectedHeader = Hex.Parse(expectedHeaderHex);

        Assert.Equal(expectedHeader, encoded.AsSpan(0, expectedHeader.Length).ToArray());
        Assert.Equal(expectedHeader.Length + contentLength, encoded.Length);
        Assert.True(encoded.AsSpan(expectedHeader.Length).SequenceEqual(contents));
    }

    [Fact]
    public void ConstructedLength_NestedAndAdjacentValuesRemainIntact()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        using (writer.EnterSequence(Asn1Tag.Sequence))
        {
            using (writer.EnterSequence(Asn1Tag.Sequence))
            {
                writer.WriteInteger(Asn1Tag.Integer, 1);
            }
            using (writer.EnterSequence(Asn1Tag.Sequence))
            {
                writer.WriteInteger(Asn1Tag.Integer, 2);
            }
        }

        Assert.Equal(Hex.Parse("300A30030201013003020102"), writer.Encode());
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
        using (writer.EnterExplicit(tag))
        {
            Asn1Integer.Encode(writer, 1);
        }
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0xA0, 0x03, 0x02, 0x01, 0x01 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        using (reader.EnterExplicit(tag))
        {
            Assert.Equal(1, Asn1Integer.Decode(reader).GetInt32());
        }
    }

    [Fact]
    public void ReadAny_ReturnsContentsView()
    {
        var bytes = Hex.Parse("02012A");
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var any = reader.ReadAny();
        Assert.Equal(Asn1Tag.Integer, any.Tag);
        Assert.Equal(new byte[] { 0x2A }, any.ContentsMemory.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(any.ContentsMemory, out ArraySegment<byte> segment));
        Assert.Same(bytes, segment.Array);
        Assert.True(reader.Eof);
    }

    private static void Run(BerDerCase c, Action<BerDerCase, Asn1Writer> encode, Action<BerDerCase, Asn1Reader, byte[]> decode)
    {
        var expected = c.GetBytes();
        var options = c.GetReaderOptions();
        if (c.Reject)
        {
            var reader = new Asn1Reader(expected, c.Encoding, options);
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

        var decodeReader = new Asn1Reader(expected, c.Encoding, options);
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

    private static void DecodeNull(BerDerCase _, Asn1Reader r, byte[] __) => r.ReadNull(Asn1Tag.Null);

    private static void EncodeInteger(BerDerCase c, Asn1Writer w) => w.WriteInteger(Asn1Tag.Integer, BerDerFixtures.GetInteger(c));

    private static void DecodeInteger(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var value = r.ReadInteger(Asn1Tag.Integer);
        if (c.HasValue)
        {
            Assert.Equal(BerDerFixtures.GetInteger(c), value);
        }
    }

    private static void EncodeEnumerated(BerDerCase c, Asn1Writer w) =>
        w.WriteEnumerated(Asn1Tag.Enumerated, BerDerFixtures.GetInteger(c));

    private static void DecodeEnumerated(BerDerCase c, Asn1Reader r, byte[] _)
    {
        var value = r.ReadEnumerated(Asn1Tag.Enumerated);
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
            Assert.Equal(BerDerFixtures.GetOctetValue(c), value.ToArray());
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

    private static void AssertIntegerEncoding(
        BigInteger numericValue,
        Asn1Integer integerValue,
        Action<Asn1Writer> write,
        string expectedHex)
    {
        var expected = Hex.Parse(expectedHex);
        Assert.Equal(expected.AsSpan(2).ToArray(), integerValue.Span.ToArray());

        var writer = new Asn1Writer(Asn1Encoding.Der);
        write(writer);
        var actual = writer.Encode();

        Assert.Equal(expected, actual);
        Assert.Equal(DotnetAsnOracle.EncodeInteger(numericValue), actual);
    }
}

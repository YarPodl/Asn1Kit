using System.Globalization;
using System.Numerics;
using Asn1Kit.Runtime;
using Xunit;

namespace Asn1Kit.Tests;

public sealed class FormattingTests
{
    [Fact]
    public void ValuesUseInvariantCultureAndDoNotDumpBytes()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.Equal("0", default(Asn1Integer).ToString());
            Assert.Equal("-42", Asn1Integer.FromInt32(-42).ToString());
            var large = BigInteger.Pow(10, 80) + 123;
            Assert.Equal(large.ToString(CultureInfo.InvariantCulture), Asn1Integer.FromBigInteger(large).ToString());
            Assert.Equal("1.25", Asn1Formatting.Format(1.25m));
            Assert.Equal("True", Asn1Formatting.Format(true));
            Assert.Equal("hello", Asn1Formatting.Format("hello"));
            Assert.Equal("", Asn1Formatting.Format(""));
            Assert.Equal("null", Asn1Formatting.Format(null));
            Assert.Equal("NULL", default(Asn1Null).ToString());
            Assert.Equal("0 bytes", Asn1Formatting.Format(ReadOnlyMemory<byte>.Empty));
            var bytes = new byte[] { 0xA0, 0xFF };
            Assert.Equal("2 bytes", Asn1Formatting.Format(bytes.AsMemory()));
            Assert.Equal("2 bytes", Asn1Formatting.Format((ReadOnlyMemory<byte>)bytes));
            Assert.Equal("2 bytes", Asn1Formatting.Format(bytes));
            Assert.Equal("3 bits", new Asn1BitString(new byte[] { 0xA0 }, 5).ToString());
            Assert.Equal("0 bits", default(Asn1BitString).ToString());
            var time = new DateTimeOffset(2026, 10, 3, 12, 34, 56, TimeSpan.FromHours(3));
            Assert.Equal("2026-10-03T12:34:56.0000000+03:00", Asn1Formatting.Format(time));
            Assert.Equal("1.2.3", Asn1Formatting.Format(Asn1Oid.Parse("1.2.3")));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("02012A", "Universal-2P (3 bytes)")]
    [InlineData("3000", "Universal-16C (2 bytes)")]
    [InlineData("308002012A0000", "Universal-16C (7 bytes)")]
    public void AnyShowsTagAndCompleteLengthAndPreservesEncoding(string hex, string expected)
    {
        var bytes = Convert.FromHexString(hex);
        var value = new Asn1Reader(bytes, Asn1Encoding.Ber).ReadAny();
        Assert.Equal(expected, value.ToString());
        var writer = new Asn1Writer(Asn1Encoding.Ber);
        writer.WriteAny(value);
        Assert.Equal(bytes, writer.Encode());
    }

    [Fact]
    public void WrappersFormatValuesWithoutMaterializingLazyValues()
    {
        Assert.Equal("<empty>", default(Asn1Any).ToString());
        Assert.Equal("<unset>", default(Asn1Value<string>).ToString());
        Assert.Equal("0", default(Asn1Value<int>).ToString());
        Assert.Equal("hello", new Asn1Value<string>("hello").ToString());
        Assert.Equal("2 bytes", new Asn1Value<ReadOnlyMemory<byte>>(new byte[2]).ToString());
        Assert.Equal("42", Asn1Lazy<int>.FromValue(42).ToString());
        var calls = 0;
        var lazy = Asn1Lazy<Asn1Integer>.FromEncoded(new byte[] { 2, 1, 42 }, Asn1Encoding.Der,
            Asn1ReaderOptions.Default, reader =>
            {
                calls++;
                return reader.ReadIntegerValue(Asn1Tag.Integer);
            });
        Assert.Equal("<not decoded: 3 bytes>", lazy.ToString());
        Assert.Equal("<not decoded: 3 bytes>", Asn1Formatting.Format(lazy));
        Assert.False(lazy.IsMaterialized);
        Assert.Equal(0, calls);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        lazy.WriteTo(writer);
        Assert.Equal(new byte[] { 2, 1, 42 }, writer.Encode());
        Assert.Equal(Asn1Integer.FromInt32(42), lazy.Value);
        Assert.Equal("42", lazy.ToString());
        Assert.Equal(1, calls);
        var invalid = Asn1Lazy<int>.FromEncoded(new byte[] { 0xFF }, Asn1Encoding.Ber,
            Asn1ReaderOptions.Default, _ => throw new InvalidOperationException("Decoder must not run."));
        Assert.Equal("<not decoded: 1 bytes>", invalid.ToString());
    }
}

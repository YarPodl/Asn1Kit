using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class Asn1CodecTests
{
    [Fact]
    public void SharedPrimitiveCodecEncodesAndDecodesOneTlv()
    {
        var codec = Asn1Codecs.Int32;
        Assert.Same(codec, Asn1Codecs.Int32);

        var raw = codec.Encode(42);

        Assert.Equal("02012A", Convert.ToHexString(raw.Span));
        Assert.Equal(42, codec.Decode(raw));
    }

    [Fact]
    public void OpenTypeAdaptersReuseLeafCodec()
    {
        var encoded = Asn1Codecs.EncodeContained(42, Asn1Codecs.Int32);
        Assert.Equal(42, Asn1Codecs.DecodeContained(encoded, Asn1Codecs.Int32));

        var raw = Asn1Codecs.EncodeEach(new[] { 1, 2 }, Asn1Codecs.Int32);
        Assert.Equal(new[] { 1, 2 }, Asn1Codecs.DecodeEach(raw, Asn1Codecs.Int32));
    }

    [Theory]
    [InlineData("0C0141", "A")]
    [InlineData("130141", "A")]
    [InlineData("1E020041", "A")]
    public void DecodeStringChoiceAcceptsAllowedForms(string encoded, string expected)
    {
        var raw = new Asn1Any(Convert.FromHexString(encoded));
        var text = Asn1Codecs.DecodeStringChoice(raw, new[]
        {
            Asn1StringForm.Utf8,
            Asn1StringForm.Printable,
            Asn1StringForm.Bmp
        });
        Assert.Equal(expected, text);
    }

    [Fact]
    public void DecodeStringChoiceRejectsDisallowedTag()
    {
        var raw = new Asn1Any(Convert.FromHexString("0C0141"));
        Assert.Throws<Asn1Exception>(() =>
            Asn1Codecs.DecodeStringChoice(raw, new[] { Asn1StringForm.Printable }));
    }

    [Fact]
    public void ContainedAdapterRejectsUnalignedBitString()
    {
        var raw = Asn1Contained<Asn1Any>.FromEncoded(new byte[] { 0x02, 0x01, 0x2A }, unusedBits: 1);
        Assert.Throws<Asn1Exception>(() => Asn1Codecs.DecodeContained(raw, Asn1Codecs.Int32));
    }
}

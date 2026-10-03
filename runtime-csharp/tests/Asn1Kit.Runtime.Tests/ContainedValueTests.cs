using Asn1Kit.Runtime;

namespace Asn1Kit.Runtime.Tests;

public sealed class ContainedValueTests
{
    [Fact]
    public void WritesTypedContentAndRetainsDecodedOctets()
    {
        var writer = new Asn1Writer();
        writer.WriteContained(Asn1Tag.OctetString, false, Asn1Contained<int>.FromValue(42), static (w, v) => w.WriteInteger(Asn1Tag.Integer, v));
        Assert.Equal("040302012A", Convert.ToHexString(writer.Encode()));
        var reader = new Asn1Reader(writer.Encode(), Asn1Encoding.Der);
        var value = reader.ReadContained(Asn1Tag.OctetString, false, true, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer));
        Assert.Equal(42, value.Value);
        Assert.Equal("02012A", Convert.ToHexString(value.Contents.Span));
        reader.ThrowIfNotEmpty();
    }

    [Fact]
    public void ReadsBerConstructedContentAndOpaqueOctets()
    {
        var reader = new Asn1Reader(Convert.FromHexString("24800401020402012A0000"), Asn1Encoding.Ber);
        Assert.Equal(42, reader.ReadContained(Asn1Tag.OctetString, false, true, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer)).Value);
        reader = new Asn1Reader(Convert.FromHexString("0402FFFF"));
        var opaque = reader.ReadContained(Asn1Tag.OctetString, false, false, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer));
        Assert.False(opaque.HasValue);
        Assert.Equal("FFFF", Convert.ToHexString(opaque.Contents.Span));
    }

    [Theory]
    [InlineData("04020201")]
    [InlineData("040402010100")]
    [InlineData("040105")]
    public void RejectsCorruptKnownContent(string hex)
    {
        var reader = new Asn1Reader(Convert.FromHexString(hex));
        Assert.Throws<Asn1Exception>(() => reader.ReadContained(Asn1Tag.OctetString, false, true, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer)));
    }

    [Fact]
    public void BitStringContentMustBeOctetAlignedWhenKnown()
    {
        var writer = new Asn1Writer();
        writer.WriteContained(Asn1Tag.BitString, true, Asn1Contained<int>.FromValue(42), static (w, v) => w.WriteInteger(Asn1Tag.Integer, v));
        Assert.Equal("03040002012A", Convert.ToHexString(writer.Encode()));
        var reader = new Asn1Reader(Convert.FromHexString("030203A0"));
        Assert.Throws<Asn1Exception>(() => reader.ReadContained(Asn1Tag.BitString, true, true, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer)));
    }

    [Fact]
    public void CollectionDecoderReceivesStateForEveryElement()
    {
        var reader = new Asn1Reader(Convert.FromHexString("3106020101020102"));
        Assert.Equal(new[] { 11, 12 }, reader.ReadSetOf(Asn1Tag.Set, 10, static (r, state) => r.ReadInt32(Asn1Tag.Integer) + state));
        reader.ThrowIfNotEmpty();
    }

    [Fact]
    public void CollectionDecoderRejectsCallbackThatDoesNotConsumeInput()
    {
        var reader = new Asn1Reader(Convert.FromHexString("3103020101"));
        Assert.Throws<Asn1Exception>(() => reader.ReadSetOf(Asn1Tag.Set, 7, static (_, state) => state));
    }

    [Fact]
    public void TypedAnyFactoryRequiresOneCompleteValue()
    {
        var value = Asn1Any.FromValue(42, static (writer, v) => writer.WriteInteger(Asn1Tag.Integer, v));
        Assert.Equal("02012A", Convert.ToHexString(value.Span));
        Assert.Throws<Asn1Exception>(() => Asn1Any.FromValue(42, static (writer, v) =>
        {
            writer.WriteInteger(Asn1Tag.Integer, v); writer.WriteNull(Asn1Tag.Null);
        }));
        Assert.Throws<ArgumentNullException>(() => Asn1Any.FromValue(42, null!));
    }
}

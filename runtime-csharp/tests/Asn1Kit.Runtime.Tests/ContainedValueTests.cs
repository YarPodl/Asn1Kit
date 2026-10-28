using Asn1Kit.Runtime;

namespace Asn1Kit.Runtime.Tests;

public sealed class ContainedValueTests
{
    [Fact]
    public void WritesAndReadsTypedOctetStringContent()
    {
        var writer = new Asn1Writer();
        writer.WriteOctetStringContaining(Asn1Tag.OctetString, 42, static (w, v) => w.WriteInteger(Asn1Tag.Integer, v));
        Assert.Equal("040302012A", Convert.ToHexString(writer.Encode()));
        var reader = new Asn1Reader(writer.Encode(), Asn1Encoding.Der);
        var value = reader.ReadOctetStringContaining(Asn1Tag.OctetString, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer));
        Assert.Equal(42, value);
        reader.ThrowIfNotEmpty();
    }

    [Fact]
    public void ReadsBerConstructedContentAndOpaqueOctets()
    {
        var reader = new Asn1Reader(Convert.FromHexString("24800401020402012A0000"), Asn1Encoding.Ber);
        Assert.Equal(42, reader.ReadOctetStringContaining(Asn1Tag.OctetString, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer)));
        reader = new Asn1Reader(Convert.FromHexString("0402FFFF"));
        var opaque = reader.ReadOctetString(Asn1Tag.OctetString);
        Assert.Equal("FFFF", Convert.ToHexString(opaque.Span));
    }

    [Theory]
    [InlineData("04020201")]
    [InlineData("040402010100")]
    [InlineData("040105")]
    public void RejectsCorruptKnownContent(string hex)
    {
        var reader = new Asn1Reader(Convert.FromHexString(hex));
        Assert.Throws<Asn1Exception>(() => reader.ReadOctetStringContaining(Asn1Tag.OctetString, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer)));
    }

    [Fact]
    public void WritesAndReadsTypedBitStringContent()
    {
        var writer = new Asn1Writer();
        writer.WriteBitStringContaining(Asn1Tag.BitString, 42, static (w, v) => w.WriteInteger(Asn1Tag.Integer, v));
        Assert.Equal("03040002012A", Convert.ToHexString(writer.Encode()));

        var reader = new Asn1Reader(writer.Encode(), Asn1Encoding.Der);
        var value = reader.ReadBitStringContaining(
            Asn1Tag.BitString,
            0,
            static (r, _) => r.ReadInt32(Asn1Tag.Integer));
        Assert.Equal(42, value);
        reader.ThrowIfNotEmpty();
    }

    [Fact]
    public void BitStringContentMustBeOctetAlignedWhenKnown()
    {
        var reader = new Asn1Reader(Convert.FromHexString("030203A0"));
        Assert.Throws<Asn1Exception>(() => reader.ReadBitStringContaining(Asn1Tag.BitString, 0, static (r, _) => r.ReadInt32(Asn1Tag.Integer)));
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

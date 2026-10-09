using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class Asn1UtilsTests
{
    [Fact]
    public void Decode_RoundTripsPrimitiveAndRejectsTrailingBytes()
    {
        var encoded = Asn1Codecs.Int32.Encode(42).ToArray();

        Assert.Equal(42, Asn1Utils.Decode(encoded, static reader => reader.ReadInt32(Asn1Tag.Integer)));

        var withTrailing = new byte[encoded.Length + 1];
        encoded.CopyTo(withTrailing, 0);
        withTrailing[^1] = 0x00;

        var ex = Assert.Throws<Asn1Exception>(() =>
            Asn1Utils.Decode(withTrailing, static reader => reader.ReadInt32(Asn1Tag.Integer)));
        Assert.Contains("trailing data", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_NullDecode_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Asn1Utils.Decode<int>(ReadOnlyMemory<byte>.Empty, null!));
    }

    [Fact]
    public void DecodeRetained_PreservesOriginalEncodingAndRejectsTrailingBytes()
    {
        var encoded = Asn1Codecs.Int32.Encode(7).ToArray();

        var retained = Asn1Utils.DecodeRetained(
            encoded,
            static reader => reader.ReadInt32(Asn1Tag.Integer));

        Assert.Equal(7, retained.Value);
        Assert.True(retained.OriginalEncoding.Span.SequenceEqual(encoded));

        var withTrailing = new byte[encoded.Length + 1];
        encoded.CopyTo(withTrailing, 0);
        withTrailing[^1] = 0x00;

        Assert.Throws<Asn1Exception>(() =>
            Asn1Utils.DecodeRetained(withTrailing, static reader => reader.ReadInt32(Asn1Tag.Integer)));
    }

    [Fact]
    public void DecodeRetained_NullDecode_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Asn1Utils.DecodeRetained<int>(ReadOnlyMemory<byte>.Empty, null!));
    }
}

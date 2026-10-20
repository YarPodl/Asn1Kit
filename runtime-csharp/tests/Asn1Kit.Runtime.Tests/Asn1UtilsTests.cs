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

    [Fact]
    public void Encode_RoundTripsEncodableAndTryEncodeReportsLength()
    {
        var value = new SampleEncodable { Number = 42 };
        var encoded = Asn1Utils.Encode(value);

        var again = Asn1Utils.Decode(encoded, SampleEncodable.Decode);
        Assert.Equal(42, again.Number);
        Assert.Equal(encoded, Asn1Utils.Encode(again));

        Span<byte> buffer = stackalloc byte[encoded.Length];
        Assert.True(Asn1Utils.TryEncode(buffer, value, out var written));
        Assert.Equal(encoded.Length, written);
        Assert.True(buffer.SequenceEqual(encoded));

        Span<byte> tooSmall = stackalloc byte[Math.Max(0, encoded.Length - 1)];
        Assert.False(Asn1Utils.TryEncode(tooSmall, value, out _));
    }

    [Fact]
    public void Encode_NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Asn1Utils.Encode<SampleEncodable>(null!));
    }

    [Fact]
    public void Encode_WithCallback_RoundTripsPrimitive()
    {
        var encoded = Asn1Utils.Encode(
            99,
            static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));

        Assert.Equal(99, Asn1Utils.Decode(encoded, static reader => reader.ReadInt32(Asn1Tag.Integer)));
    }

    [Fact]
    public void WriteSequenceOf_TypeDriven_MatchesCallbackForm()
    {
        var items = new List<SampleEncodable>
        {
            new() { Number = 1 },
            new() { Number = 2 },
        };

        var viaInterface = new Asn1Writer(Asn1Encoding.Der);
        viaInterface.WriteSequenceOf(Asn1Tag.Sequence, items);

        var viaCallback = new Asn1Writer(Asn1Encoding.Der);
        viaCallback.WriteSequenceOf(
            Asn1Tag.Sequence,
            items,
            static (writer, item) => item.Encode(writer));

        Assert.Equal(viaCallback.Encode(), viaInterface.Encode());

        var decoded = Asn1Utils.Decode(
            viaInterface.Encode(),
            static reader => reader.ReadSequenceOf(Asn1Tag.Sequence, SampleEncodable.Decode));
        Assert.Equal(new[] { 1, 2 }, decoded.Select(item => item.Number));
    }

    [Fact]
    public void WriteSetOf_TypeDriven_MatchesCallbackForm()
    {
        var items = new List<SampleEncodable>
        {
            new() { Number = 3 },
            new() { Number = 1 },
        };

        var viaInterface = new Asn1Writer(Asn1Encoding.Der);
        viaInterface.WriteSetOf(Asn1Tag.Set, items);

        var viaCallback = new Asn1Writer(Asn1Encoding.Der);
        viaCallback.WriteSetOf(
            Asn1Tag.Set,
            items,
            static (writer, item) => item.Encode(writer));

        Assert.Equal(viaCallback.Encode(), viaInterface.Encode());
    }

    private sealed class SampleEncodable : IAsn1Encodable
    {
        public int Number { get; set; }

        public void Encode(Asn1Writer writer) =>
            writer.WriteInteger(Asn1Tag.Integer, Number);

        public static SampleEncodable Decode(Asn1Reader reader) =>
            new() { Number = reader.ReadInt32(Asn1Tag.Integer) };
    }
}

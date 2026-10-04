using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class Asn1AnyDecodeValueTests
{
    [Fact]
    public void DecodeValue_ReadsOneCompleteTlv()
    {
        var any = Asn1Any.FromValue(42, static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));
        Assert.Equal(42, any.DecodeValue(static reader => reader.ReadInt32(Asn1Tag.Integer), Asn1Encoding.Der));
        Assert.Equal(new byte[] { 0x02, 0x01, 0x2A }, any.ToArray());
    }

    [Fact]
    public void DecodeValue_RejectsWrongTagIncompleteConsumptionAndNullDecoder()
    {
        var any = Asn1Any.FromValue(42, static (writer, value) => writer.WriteInteger(Asn1Tag.Integer, value));
        Assert.Throws<Asn1Exception>(() => any.DecodeValue(static reader => reader.ReadOid(Asn1Tag.ObjectIdentifier)));
        Assert.Throws<Asn1Exception>(() => any.DecodeValue(static reader => 0));
        Assert.Throws<ArgumentNullException>(() => any.DecodeValue<int>(null!));
    }
}

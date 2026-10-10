using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class Asn1TagTests
{
    [Fact]
    public void Factories_MatchConstructor()
    {
        Assert.Equal(new Asn1Tag(Asn1TagClass.ContextSpecific, 2, false), Asn1Tag.Context(2));
        Assert.Equal(new Asn1Tag(Asn1TagClass.ContextSpecific, 0, true), Asn1Tag.Context(0, true));
        Assert.Equal(new Asn1Tag(Asn1TagClass.Application, 1, false), Asn1Tag.Application(1));
        Assert.Equal(new Asn1Tag(Asn1TagClass.Private, 3, true), Asn1Tag.Private(3, true));
        Assert.Equal(Asn1Tag.Context(5, true), Asn1Tag.Context(5).AsConstructed());
    }
}

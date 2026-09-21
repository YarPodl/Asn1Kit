using Asn1Kit.Pkix;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class PkixNegativeCodecTests
{
    [Theory]
    [InlineData("TrustAnchorRootCertificate.crt")]
    [InlineData("GoodCACert.crt")]
    [InlineData("ValidCertificatePathTest1EE.crt")]
    public void TruncatedCertificate_Throws(string fileName)
    {
        var der = PkixFixtures.ReadDer(fileName);
        var truncated = der.AsSpan(0, Math.Max(8, der.Length / 2)).ToArray();
        Assert.ThrowsAny<Asn1Exception>(() => Certificate.Decode(new Asn1Reader(truncated, Asn1Encoding.Der)));
    }

    [Fact]
    public void TruncatedCrl_Throws()
    {
        var der = PkixFixtures.ReadDer("GoodCACRL.crl");
        var truncated = der.AsSpan(0, Math.Max(8, der.Length / 2)).ToArray();
        Assert.ThrowsAny<Asn1Exception>(() => CertificateList.Decode(new Asn1Reader(truncated, Asn1Encoding.Der)));
    }

    [Fact]
    public void WrongTag_OnCertificate_Throws()
    {
        var der = PkixFixtures.ReadDer("GoodCACert.crt");
        // Flip SEQUENCE (0x30) to SET (0x31) at the root.
        der[0] = 0x31;
        Assert.ThrowsAny<Asn1Exception>(() => Certificate.Decode(new Asn1Reader(der, Asn1Encoding.Der)));
    }

    [Fact]
    public void WrongTag_OnCrl_Throws()
    {
        var der = PkixFixtures.ReadDer("GoodCACRL.crl");
        der[0] = 0x31;
        Assert.ThrowsAny<Asn1Exception>(() => CertificateList.Decode(new Asn1Reader(der, Asn1Encoding.Der)));
    }
}

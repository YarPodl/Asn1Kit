using Asn1Kit.Pkix;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class CertificateListCodecTests
{
    [Fact]
    public void GoodCACRL_Decode_MatchesExternalExpectedAndRoundTrips()
    {
        const string fileName = "GoodCACRL.crl";
        var der = PkixFixtures.ReadDer(fileName);
        var expected = PkixFixtures.LoadExpected().Crls[fileName];

        var crl = CertificateList.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        var tbs = crl.TbsCertList;

        Assert.Equal(expected.Asn1Version, tbs.Version);
        Assert.Equal(expected.SignatureAlgorithm, crl.SignatureAlgorithm.Algorithm);
        Assert.Equal(expected.SignatureAlgorithm, tbs.Signature.Algorithm);
        Assert.Equal(expected.ThisUpdateUtc, tbs.ThisUpdate.Value);
        Assert.NotNull(tbs.NextUpdate);
        Assert.Equal(expected.NextUpdateUtc, tbs.NextUpdate!.Value);
        AssertDn(expected.Issuer, tbs.Issuer);

        Assert.NotNull(tbs.RevokedCertificates);
        Assert.Equal(
            expected.RevokedSerialNumbers.Select(Asn1Integer.FromInt32).ToList(),
            tbs.RevokedCertificates!.Select(e => e.UserCertificate).ToList());

        Assert.NotNull(tbs.CrlExtensions);
        Assert.Equal(expected.CrlExtensionOids, tbs.CrlExtensions!.Select(e => e.ExtnID).ToList());

        var writer = new Asn1Writer(Asn1Encoding.Der);
        crl.Encode(writer);
        var encoded = writer.Encode();
        Assert.Equal(der, encoded);

        var again = CertificateList.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(tbs.NextUpdate!.Value, again.TbsCertList.NextUpdate!.Value);
        Assert.Equal(
            tbs.RevokedCertificates!.Select(e => e.UserCertificate).ToList(),
            again.TbsCertList.RevokedCertificates!.Select(e => e.UserCertificate).ToList());
    }

    private static void AssertDn(List<ExpectedDnAttribute> expected, List<List<AttributeTypeAndValue>> actual)
    {
        var flat = PkixFixtures.FlattenName(actual);
        Assert.Equal(expected.Count, flat.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Oid, flat[i].Oid);
            Assert.Equal(expected[i].Value, flat[i].Value);
        }
    }
}

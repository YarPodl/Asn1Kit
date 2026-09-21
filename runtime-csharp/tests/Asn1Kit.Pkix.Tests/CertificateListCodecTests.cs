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
        Assert.Equal(expected.SignatureUnusedBits, crl.Signature.UnusedBits);
        Assert.Equal(expected.SignatureByteLength, crl.Signature.Span.Length);

        Assert.NotNull(tbs.RevokedCertificates);
        Assert.Equal(expected.RevokedCertificates.Count, tbs.RevokedCertificates!.Count);
        for (var i = 0; i < expected.RevokedCertificates.Count; i++)
        {
            var want = expected.RevokedCertificates[i];
            var got = tbs.RevokedCertificates[i];
            Assert.Equal(Asn1Integer.FromInt32(want.SerialNumber), got.UserCertificate);
            Assert.Equal(want.RevocationDateUtc, got.RevocationDate.Value);
            Assert.NotNull(got.CrlEntryExtensions);
            var reasonExt = PkixFixtures.RequireExtension(got.CrlEntryExtensions!, "2.5.29.21");
            Assert.False(reasonExt.Critical ?? false);
            var reason = (CRLReason)(int)PkixFixtures.ExtnValueReader(reasonExt).ReadEnumerated(Asn1Tag.Enumerated);
            Assert.Equal(PkixFixtures.ParseCrlReason(want.CrlReason!), reason);
            PkixFixtures.AssertExtnValueRoundTrip(reasonExt, writer =>
            {
                writer.WriteEnumerated(Asn1Tag.Enumerated, (int)reason);
            });
        }

        Assert.NotNull(tbs.CrlExtensions);
        Assert.Equal(expected.CrlExtensionOids, tbs.CrlExtensions!.Select(e => e.ExtnID).ToList());
        foreach (var expectedExt in expected.CrlExtensions)
        {
            var extension = PkixFixtures.RequireExtension(tbs.CrlExtensions!, expectedExt.Oid);
            Assert.Equal(expectedExt.Critical, extension.Critical ?? false);
            if (expectedExt.Oid == "2.5.29.35")
            {
                var aki = AuthorityKeyIdentifier.Decode(PkixFixtures.ExtnValueReader(extension));
                Assert.Equal(expectedExt.AuthorityKeyIdentifierHex, PkixFixtures.ToHex(aki.KeyIdentifier!.Value.Span));
                PkixFixtures.AssertExtnValueRoundTrip(extension, aki.Encode);
            }
            else if (expectedExt.Oid == "2.5.29.20")
            {
                var number = PkixFixtures.ExtnValueReader(extension).ReadIntegerValue(Asn1Tag.Integer);
                Assert.Equal(expectedExt.CrlNumber, number.GetInt32());
                PkixFixtures.AssertExtnValueRoundTrip(extension, writer =>
                {
                    writer.WriteInteger(Asn1Tag.Integer, number);
                });
            }
        }

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

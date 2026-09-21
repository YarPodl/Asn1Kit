using System.Security.Cryptography.X509Certificates;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class CertificateCodecTests
{
    [Theory]
    [InlineData("TrustAnchorRootCertificate.crt")]
    [InlineData("GoodCACert.crt")]
    [InlineData("ValidCertificatePathTest1EE.crt")]
    [InlineData("nameConstraintsDNS1CACert.crt")]
    public void Decode_MatchesExternalExpectedAndRoundTrips(string fileName)
    {
        var der = PkixFixtures.ReadDer(fileName);
        var expected = PkixFixtures.LoadExpected().Certificates[fileName];

        var certificate = Certificate.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        var tbs = certificate.TbsCertificate;

        Assert.Equal(expected.Asn1Version, tbs.Version);
        Assert.Equal(Asn1Integer.FromInt32(expected.SerialNumber), tbs.SerialNumber);
        Assert.Equal(expected.SignatureAlgorithm, certificate.SignatureAlgorithm.Algorithm);
        Assert.Equal(expected.SignatureAlgorithm, tbs.Signature.Algorithm);
        Assert.Equal(expected.NotBeforeUtc, tbs.Validity.NotBefore.Value);
        Assert.Equal(expected.NotAfterUtc, tbs.Validity.NotAfter.Value);
        AssertDn(expected.Subject, tbs.Subject);
        AssertDn(expected.Issuer, tbs.Issuer);

        Assert.Equal(expected.SubjectPublicKeyAlgorithm, tbs.SubjectPublicKeyInfo.Algorithm.Algorithm);
        if (expected.SubjectPublicKeyParametersNull)
        {
            Assert.NotNull(tbs.SubjectPublicKeyInfo.Algorithm.Parameters);
            Assert.NotNull(tbs.SubjectPublicKeyInfo.Algorithm.Parameters!.Null);
        }

        Assert.Equal(expected.SubjectPublicKeyUnusedBits, tbs.SubjectPublicKeyInfo.SubjectPublicKey.UnusedBits);
        Assert.Equal(expected.SubjectPublicKeyByteLength, tbs.SubjectPublicKeyInfo.SubjectPublicKey.Span.Length);
        Assert.Equal(expected.SignatureUnusedBits, certificate.Signature.UnusedBits);
        Assert.Equal(expected.SignatureByteLength, certificate.Signature.Span.Length);

        Assert.NotNull(tbs.Extensions);
        Assert.Equal(expected.ExtensionOids, tbs.Extensions!.Select(e => e.ExtnID).ToList());
        foreach (var expectedExt in expected.Extensions)
        {
            var actual = PkixFixtures.RequireExtension(tbs.Extensions!, expectedExt.Oid);
            Assert.Equal(expectedExt.Critical, actual.Critical ?? false);
        }

        using (var bcl = new X509Certificate2(der))
        {
            Assert.Equal(expected.SerialNumber, int.Parse(bcl.SerialNumber, System.Globalization.NumberStyles.HexNumber));
            Assert.Equal(expected.NotBeforeUtc, bcl.NotBefore.ToUniversalTime());
            Assert.Equal(expected.NotAfterUtc, bcl.NotAfter.ToUniversalTime());
            Assert.Equal(expected.SignatureAlgorithm, bcl.SignatureAlgorithm.Value);
            Assert.Equal(expected.Asn1Version + 1, bcl.Version);
            Assert.Equal(expected.SubjectPublicKeyAlgorithm, bcl.PublicKey.Oid.Value);
            Assert.Equal(expected.SubjectPublicKeyByteLength, bcl.PublicKey.EncodedKeyValue.RawData.Length);
        }

        var writer = new Asn1Writer(Asn1Encoding.Der);
        certificate.Encode(writer);
        var encoded = writer.Encode();
        Assert.Equal(der, encoded);

        var again = Certificate.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(certificate.SignatureAlgorithm.Algorithm, again.SignatureAlgorithm.Algorithm);
        Assert.Equal(tbs.SerialNumber, again.TbsCertificate.SerialNumber);
        Assert.Equal(tbs.SubjectPublicKeyInfo.SubjectPublicKey.Span.Length, again.TbsCertificate.SubjectPublicKeyInfo.SubjectPublicKey.Span.Length);
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

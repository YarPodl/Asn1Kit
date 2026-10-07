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
        var tbs = certificate.TbsCertificate.Value;
        var spki = tbs.SubjectPublicKeyInfo.Value;
        var extensions = tbs.Extensions!.Value.Value;

        Assert.Equal(expected.Asn1Version, tbs.Version);
        Assert.Equal(Asn1Integer.FromInt32(expected.SerialNumber), tbs.SerialNumber);
        Assert.Equal(expected.SignatureAlgorithm, certificate.SignatureAlgorithm.Algorithm.ToString());
        Assert.Equal(expected.SignatureAlgorithm, tbs.Signature.Algorithm.ToString());
        Assert.Equal(expected.NotBeforeUtc, tbs.Validity.NotBefore.Value);
        Assert.Equal(expected.NotAfterUtc, tbs.Validity.NotAfter.Value);
        AssertDn(expected.Subject, tbs.Subject.Value);
        AssertDn(expected.Issuer, tbs.Issuer.Value);

        Assert.Equal(expected.SubjectPublicKeyAlgorithm, spki.Algorithm.Algorithm.ToString());
        if (expected.SubjectPublicKeyParametersNull)
        {
            Assert.NotNull(spki.Algorithm.Parameters);
            Assert.NotNull(spki.Algorithm.Parameters!.Null);
        }

        Assert.Equal(expected.SubjectPublicKeyUnusedBits, spki.SubjectPublicKey.UnusedBits);
        Assert.Equal(expected.SubjectPublicKeyByteLength, spki.SubjectPublicKey.Span.Length);
        Assert.Equal(expected.SignatureUnusedBits, certificate.Signature.UnusedBits);
        Assert.Equal(expected.SignatureByteLength, certificate.Signature.Span.Length);

        Assert.NotNull(tbs.Extensions);
        Assert.Equal(expected.ExtensionOids, extensions.Select(e => e.ExtnID.ToString()).ToList());
        foreach (var expectedExt in expected.Extensions)
        {
            var actual = PkixFixtures.RequireExtension(extensions, expectedExt.Oid);
            Assert.Equal(expectedExt.Critical, actual.Critical);
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
        Assert.Equal(tbs.SerialNumber, again.TbsCertificate.Value.SerialNumber);
        Assert.Equal(spki.SubjectPublicKey.Span.Length, again.TbsCertificate.Value.SubjectPublicKeyInfo.Value.SubjectPublicKey.Span.Length);
    }

    private static void AssertDn(List<ExpectedDnAttribute> expected, AttributeTypeAndValue[][] actual)
    {
        var actualAttributes = actual.SelectMany(rdn => rdn).ToArray();
        var flat = PkixFixtures.FlattenName(actual);
        Assert.Equal(expected.Count, flat.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Oid, flat[i].Oid);
            Assert.Equal(expected[i].Value, flat[i].Value);
            Assert.Null(actualAttributes[i].Value.Unknown);
            switch (expected[i].Oid)
            {
                case "2.5.4.3":
                    Assert.Equal(AttributeTypeAndValue_ValueKind.PrintableString, actualAttributes[i].Value.Kind);
                    Assert.NotNull(actualAttributes[i].Value.Value);
                    break;
                case "2.5.4.6":
                    Assert.Equal(AttributeTypeAndValue_ValueKind.Printable, actualAttributes[i].Value.Kind);
                    Assert.NotNull(actualAttributes[i].Value.Value);
                    break;
                case "2.5.4.10":
                    Assert.Equal(AttributeTypeAndValue_ValueKind.PrintableString, actualAttributes[i].Value.Kind);
                    Assert.NotNull(actualAttributes[i].Value.Value);
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"Unexpected test-fixture DN OID '{expected[i].Oid}'.");
            }
        }
    }
}

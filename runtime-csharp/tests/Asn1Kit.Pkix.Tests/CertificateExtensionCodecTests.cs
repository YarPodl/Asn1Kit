using Asn1Kit.Pkix;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class CertificateExtensionCodecTests
{
    [Theory]
    [InlineData("TrustAnchorRootCertificate.crt")]
    [InlineData("GoodCACert.crt")]
    [InlineData("ValidCertificatePathTest1EE.crt")]
    [InlineData("nameConstraintsDNS1CACert.crt")]
    public void TypedExtensions_MatchExternalExpectedAndRoundTripExtnValue(string fileName)
    {
        var der = PkixFixtures.ReadDer(fileName);
        var expectedCert = PkixFixtures.LoadExpected().Certificates[fileName];
        var certificate = Certificate.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        Assert.NotNull(certificate.TbsCertificate.Extensions);

        foreach (var expected in expectedCert.Extensions)
        {
            var extension = PkixFixtures.RequireExtension(certificate.TbsCertificate.Extensions!, expected.Oid);
            Assert.Equal(expected.Critical, extension.Critical ?? false);

            switch (expected.Oid)
            {
                case "2.5.29.14":
                    AssertSubjectKeyIdentifier(extension, expected);
                    break;
                case "2.5.29.15":
                    AssertKeyUsage(extension, expected);
                    break;
                case "2.5.29.19":
                    AssertBasicConstraints(extension, expected);
                    break;
                case "2.5.29.35":
                    AssertAuthorityKeyIdentifier(extension, expected);
                    break;
                case "2.5.29.32":
                    AssertCertificatePolicies(extension, expected);
                    break;
                case "2.5.29.30":
                    AssertNameConstraints(extension, expected);
                    break;
                case "2.5.29.17":
                    AssertSubjectAltName(extension, expected);
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"Unhandled extension OID {expected.Oid} in test.");
            }
        }
    }

    [Theory]
    [InlineData("ValidDNSnameConstraintsTest30EE.crt")]
    [InlineData("ValidRFC822nameConstraintsTest21EE.crt")]
    [InlineData("ValidURInameConstraintsTest34EE.crt")]
    public void SubjectAltName_MatchesExternalExpectedAndRoundTrips(string fileName)
    {
        var der = PkixFixtures.ReadDer(fileName);
        var expectedCert = PkixFixtures.LoadExpected().Certificates[fileName];
        var certificate = Certificate.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        var expected = expectedCert.Extensions.Single(e => e.Oid == "2.5.29.17");
        var extension = PkixFixtures.RequireExtension(certificate.TbsCertificate.Extensions!, expected.Oid);
        AssertSubjectAltName(extension, expected);

        var writer = new Asn1Writer(Asn1Encoding.Der);
        certificate.Encode(writer);
        Assert.Equal(der, writer.Encode());
    }

    private static void AssertSubjectKeyIdentifier(Extension extension, ExpectedExtension expected)
    {
        Assert.False(string.IsNullOrEmpty(expected.SubjectKeyIdentifierHex));
        var keyId = PkixFixtures.ExtnValueReader(extension).ReadOctetString(Asn1Tag.OctetString);
        Assert.Equal(expected.SubjectKeyIdentifierHex, PkixFixtures.ToHex(keyId.Span));
        PkixFixtures.AssertExtnValueRoundTrip(extension, writer => writer.WriteOctetString(Asn1Tag.OctetString, keyId.Span));
    }

    private static void AssertAuthorityKeyIdentifier(Extension extension, ExpectedExtension expected)
    {
        Assert.False(string.IsNullOrEmpty(expected.AuthorityKeyIdentifierHex));
        var aki = AuthorityKeyIdentifier.Decode(PkixFixtures.ExtnValueReader(extension));
        Assert.NotNull(aki.KeyIdentifier);
        Assert.Equal(expected.AuthorityKeyIdentifierHex, PkixFixtures.ToHex(aki.KeyIdentifier!.Value.Span));
        PkixFixtures.AssertExtnValueRoundTrip(extension, aki.Encode);
    }

    private static void AssertKeyUsage(Extension extension, ExpectedExtension expected)
    {
        Assert.NotNull(expected.KeyUsage);
        var keyUsage = KeyUsage.Decode(PkixFixtures.ExtnValueReader(extension));
        Assert.Equal(PkixFixtures.ParseKeyUsage(expected.KeyUsage!), keyUsage.Flags);
        PkixFixtures.AssertExtnValueRoundTrip(extension, keyUsage.Encode);
    }

    private static void AssertBasicConstraints(Extension extension, ExpectedExtension expected)
    {
        Assert.NotNull(expected.BasicConstraints);
        var bc = BasicConstraints.Decode(PkixFixtures.ExtnValueReader(extension));
        Assert.Equal(expected.BasicConstraints!.Ca, bc.CA ?? false);
        if (expected.BasicConstraints.PathLenPresent)
        {
                    Assert.NotNull(bc.PathLenConstraint);
                    Assert.Equal(expected.BasicConstraints.PathLen!.Value, bc.PathLenConstraint!.Value.GetInt32());
        }
        else
        {
            Assert.Null(bc.PathLenConstraint);
        }

        PkixFixtures.AssertExtnValueRoundTrip(extension, bc.Encode);
    }

    private static void AssertCertificatePolicies(Extension extension, ExpectedExtension expected)
    {
        Assert.NotNull(expected.CertificatePolicies);
        var policies = PkixFixtures.ExtnValueReader(extension)
            .ReadSequenceOf(Asn1Tag.Sequence, static reader => PolicyInformation.Decode(reader, Asn1Tag.Sequence));
        Assert.Equal(expected.CertificatePolicies!, policies.Select(p => p.PolicyIdentifier.ToString()).ToList());
        PkixFixtures.AssertExtnValueRoundTrip(extension, writer =>
        {
            writer.WriteSequenceOf(Asn1Tag.Sequence, policies, static (inner, item) => item.Encode(inner, Asn1Tag.Sequence));
        });
    }

    private static void AssertNameConstraints(Extension extension, ExpectedExtension expected)
    {
        Assert.NotNull(expected.NameConstraints);
        var nc = NameConstraints.Decode(PkixFixtures.ExtnValueReader(extension));
        Assert.NotNull(nc.PermittedSubtrees);
        var dns = nc.PermittedSubtrees!
            .Where(s => s.Base.Kind == GeneralNameKind.DNSName)
            .Select(s => s.Base.DNSName!)
            .ToList();
        Assert.Equal(expected.NameConstraints!.PermittedDns, dns);
        if (expected.NameConstraints.ExcludedPresent)
        {
            Assert.NotNull(nc.ExcludedSubtrees);
        }
        else
        {
            Assert.Null(nc.ExcludedSubtrees);
        }

        PkixFixtures.AssertExtnValueRoundTrip(extension, nc.Encode);
    }

    private static void AssertSubjectAltName(Extension extension, ExpectedExtension expected)
    {
        Assert.NotNull(expected.SubjectAltName);
        var names = PkixFixtures.ExtnValueReader(extension)
            .ReadSequenceOf(Asn1Tag.Sequence, static reader => GeneralName.Decode(reader));
        Assert.Equal(expected.SubjectAltName!.Count, names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            var want = expected.SubjectAltName[i];
            var got = names[i];
            switch (want.Kind.ToLowerInvariant())
            {
                case "dnsname":
                    Assert.Equal(GeneralNameKind.DNSName, got.Kind);
                    Assert.Equal(want.Value, got.DNSName);
                    break;
                case "rfc822name":
                    Assert.Equal(GeneralNameKind.Rfc822Name, got.Kind);
                    Assert.Equal(want.Value, got.Rfc822Name);
                    break;
                case "uniformresourceidentifier":
                    Assert.Equal(GeneralNameKind.UniformResourceIdentifier, got.Kind);
                    Assert.Equal(want.Value, got.UniformResourceIdentifier);
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"Unhandled GeneralName kind '{want.Kind}'.");
            }
        }

        PkixFixtures.AssertExtnValueRoundTrip(extension, writer =>
        {
            writer.WriteSequenceOf(Asn1Tag.Sequence, names, static (inner, item) => item.Encode(inner));
        });
    }
}

using Asn1Kit.Pkix;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class GeneralNameCodecTests
{
    [Fact]
    public void SyntheticGeneralNames_MatchExternalHexAndRoundTrip()
    {
        var document = PkixFixtures.LoadExpected().GeneralNames
            ?? throw new InvalidOperationException("expected.json missing generalNames.");

        foreach (var testCase in document.Cases)
        {
            var der = PkixFixtures.ParseHex(testCase.Hex);
            var name = GeneralName.Decode(new Asn1Reader(der, Asn1Encoding.Der));

            switch (testCase.Kind.ToLowerInvariant())
            {
                case "ipaddress":
                    Assert.Equal(GeneralNameKind.IPAddress, name.Kind);
                    Assert.Equal(testCase.IpAddressHex, PkixFixtures.ToHex(name.IPAddress!.Value.Span));
                    break;
                case "registeredid":
                    Assert.Equal(GeneralNameKind.RegisteredID, name.Kind);
                    Assert.Equal(testCase.Oid, name.RegisteredID!.Value.ToString());
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"Unhandled synthetic GeneralName kind '{testCase.Kind}'.");
            }

            var writer = new Asn1Writer(Asn1Encoding.Der);
            name.Encode(writer);
            Assert.Equal(PkixFixtures.ToHex(der), PkixFixtures.ToHex(writer.Encode()));
        }
    }

    [Theory]
    [InlineData("ValidDNSnameConstraintsTest30EE.crt", GeneralNameKind.DNSName, "testserver.testcertificates.gov")]
    [InlineData("ValidRFC822nameConstraintsTest21EE.crt", GeneralNameKind.Rfc822Name, "Test21EE@mailserver.testcertificates.gov")]
    [InlineData("ValidURInameConstraintsTest34EE.crt", GeneralNameKind.UniformResourceIdentifier, "http://testserver.testcertificates.gov/index.html")]
    public void SubjectAltName_FromPkits_MatchesCertutil(string fileName, GeneralNameKind kind, string value)
    {
        var certificate = Certificate.Decode(new Asn1Reader(PkixFixtures.ReadDer(fileName), Asn1Encoding.Der));
        var san = PkixFixtures.RequireExtension(certificate.TbsCertificate.Extensions!, "2.5.29.17");
        var names = PkixFixtures.ExtnValueReader(san)
            .ReadSequenceOf(Asn1Tag.Sequence, static reader => GeneralName.Decode(reader));
        Assert.Single(names);
        Assert.Equal(kind, names[0].Kind);
        var actual = kind switch
        {
            GeneralNameKind.DNSName => names[0].DNSName,
            GeneralNameKind.Rfc822Name => names[0].Rfc822Name,
            GeneralNameKind.UniformResourceIdentifier => names[0].UniformResourceIdentifier,
            _ => throw new Xunit.Sdk.XunitException($"Unexpected kind {kind}."),
        };
        Assert.Equal(value, actual);
    }
}

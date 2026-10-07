using Asn1Kit.Modern.PKIX1Explicit2009;
using Asn1Kit.Modern.CryptographicMessageSyntax2009;
using Asn1Kit.Runtime;
using Common = Asn1Kit.Modern.PKIXCommonTypes2009;
using Implicit = Asn1Kit.Modern.PKIX1Implicit2009;

namespace Asn1Kit.Tests;

public sealed class ModernOpenTypeBindingTests
{
    [Fact]
    public void KeyIdentifierBindingsEncodeAndKeepRawCriticality()
    {
        var raw = new Common.Extension {Critical = true};
        raw.SetExtnValueAuthorityKeyIdentifier(
            new Implicit.AuthorityKeyIdentifier {KeyIdentifier = new byte[] {0x11, 0x22}});
        var writer = new Asn1Writer(); raw.Encode(writer);
        Assert.Equal("30100603551D230101FF0406300480021122", Convert.ToHexString(writer.Encode()));
        var certificate = new TBSCertificate { Extensions = new[] {raw} };
        Assert.True(certificate.TryGetExtensions(
            CertExtensionsBindings.AuthorityKeyIdentifier, out var actual, out var matched));
        Assert.True(matched.Critical);
        Assert.Equal(new byte[] {0x11, 0x22}, actual.KeyIdentifier!.Value.ToArray());
        Assert.False(certificate.TryGetExtensionsSubjectKeyIdentifier(out _));
        Assert.False(raw.TryDecodeExtnValue(
            CertExtensionsBindings.SubjectKeyIdentifier, out _));
        var decoded = Common.Extension.Decode(new Asn1Reader(writer.Encode()));
        Assert.True(decoded.TryDecodeExtnValue(
            CertExtensionsBindings.AuthorityKeyIdentifier, out actual));
        Assert.Equal(new byte[] {0x11, 0x22}, actual.KeyIdentifier!.Value.ToArray());
    }

    [Fact]
    public void IdenticalDataTypesWithDifferentOidsRemainSeparate()
    {
        var names = new[] {Implicit.GeneralName.FromDNSName("example.test")};
        var subject = new Common.Extension();
        subject.SetExtnValueSubjectAltName(names);
        var issuer = new Common.Extension();
        issuer.SetExtnValueIssuerAltName(names);
        Assert.NotEqual(subject.ExtnID, issuer.ExtnID);
        Assert.False(issuer.TryDecodeExtnValue(
            CertExtensionsBindings.SubjectAltName, out _));
        var certificate = new TBSCertificate {Extensions = new[] {subject, issuer}};
        Assert.True(certificate.TryGetExtensionsSubjectAltName(out var decodedSubject));
        Assert.True(certificate.TryGetExtensions(
            CertExtensionsBindings.IssuerAltName, out var decodedIssuer));
        Assert.Equal("example.test", Assert.Single(decodedSubject).DNSName);
        Assert.Equal("example.test", Assert.Single(decodedIssuer).DNSName);
    }

    [Fact]
    public void SignedAndUnsignedTablesDoNotBorrowEachOthersBindings()
    {
        var digest = new Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute();
        digest.SetAttrValuesMessageDigest(
            new ReadOnlyMemory<byte>[] {new byte[] {0xAA}});
        var counter = new Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute();
        counter.SetAttrValuesCountersignature(Array.Empty<SignerInfo>());
        var signer = new SignerInfo
        {
            SignedAttrs = new Asn1Value<Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute[]>(new[] {digest}),
            UnsignedAttrs = new[] {counter, digest}
        };
        Assert.True(signer.TryGetSignedAttrs(
            SignedAttributesSetBindings.MessageDigest, out var actual));
        Assert.Equal(new byte[] {0xAA}, Assert.Single(actual).ToArray());
        Assert.True(signer.TryGetUnsignedAttrsCountersignature(out var empty));
        Assert.Empty(empty);
        signer.SignedAttrs = new Asn1Value<Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute[]>(new[] {digest, digest});
        Assert.Throws<Asn1Exception>(() => signer.TryGetSignedAttrsMessageDigest(out _));
    }

    [Theory]
    [InlineData("0C0141")]
    [InlineData("130141")]
    [InlineData("1E020041")]
    public void NameAttributesUseContextualStringDecoder(string encoded)
    {
        var attribute = new Common.SingleAttribute {Type = Asn1Oid.Parse("2.5.4.3"),
            Value = new Asn1Any(Convert.FromHexString(encoded))};
        Assert.True(attribute.TryDecodeValue(
            Asn1Kit.Modern.PKIX1Explicit2009.SupportedAttributesValueBindings.X520CommonNameStringValue, out var text));
        Assert.Equal("A", text);
        attribute.Type = Asn1Oid.Parse("1.2.99");
        Assert.False(attribute.TryDecodeValue(
            Asn1Kit.Modern.PKIX1Explicit2009.SupportedAttributesValueBindings.X520CommonNameStringValue, out _));
    }

    [Fact]
    public void AlgorithmParametersUseInstanceDescriptorsForDecodeAndEncode()
    {
        var builtIn = SignatureAlgorithmsParametersBindings.SaRsaWithSHA1;
        var certificate = new TBSCertificate
        {
            Signature = new Asn1Kit.Modern.AlgorithmInformation2009.AlgorithmIdentifier
            {
                Algorithm = builtIn.Oid,
                Parameters = new Asn1Any(Convert.FromHexString("0500"))
            }
        };
        Assert.True(certificate.TryDecodeSignatureParameters(builtIn, out _));
        certificate.SetSignatureParametersSaRsaWithSHA1();

        var customOid = Asn1Oid.Parse("1.2.3.4");
        var custom = SignatureAlgorithmsParametersBindings.Create<int>(customOid,
            static source =>
            {
                var reader = new Asn1Reader(source.Parameters!.Value.EncodedMemory);
                var value = reader.ReadInt32(Asn1Tag.Integer);
                reader.ThrowIfNotEmpty();
                return value;
            },
            static value =>
            {
                var writer = new Asn1Writer();
                writer.WriteInteger(Asn1Tag.Integer, value);
                return new Asn1Any(writer.Encode());
            });
        certificate.Signature.Algorithm = customOid;
        certificate.SetSignatureParameters(custom, 42);
        Assert.True(certificate.TryDecodeSignatureParameters(custom, out var value));
        Assert.Equal(42, value);
        Assert.False(certificate.TryDecodeSignatureParameters(builtIn, out _));
        // Container Set assigns selector from the binding (does not require a prior matching OID).
        certificate.SetSignatureParametersSaRsaWithSHA1();
        Assert.Equal(builtIn.Oid, certificate.Signature.Algorithm);
        Assert.True(certificate.TryDecodeSignatureParameters(builtIn, out _));
        Assert.False(certificate.TryDecodeSignatureParameters(custom, out _));
    }

    [Theory]
    [InlineData("TrustAnchorRootCertificate.crt")]
    [InlineData("ValidCertificatePathTest1EE.crt")]
    public void ReadingExistingCertificatesDoesNotChangeTheirDer(string name)
    {
        var bytes = File.ReadAllBytes(TestData.RepoPath("runtime-csharp/fixtures/pkix/" + name));
        var certificate = Certificate.Decode(new Asn1Reader(bytes));
        Assert.True(certificate.ToBeSigned.TryGetExtensionsSubjectKeyIdentifier(out var ski));
        Assert.False(ski.IsEmpty);
        certificate.ToBeSigned.TryGetExtensionsAuthorityKeyIdentifier(out _);
        var writer = new Asn1Writer(); certificate.Encode(writer);
        Assert.Equal(bytes, writer.Encode());
    }
}

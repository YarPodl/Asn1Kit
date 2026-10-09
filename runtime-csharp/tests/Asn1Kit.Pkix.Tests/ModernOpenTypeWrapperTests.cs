using Asn1Kit.Modern.AlgorithmInformation2009;
using Asn1Kit.Modern.PKIX1Explicit2009;
using Asn1Kit.Modern.PKIXCommonTypes2009;
using Asn1Kit.Modern.CryptographicMessageSyntax2009;
using Asn1Kit.Runtime;
using Common = Asn1Kit.Modern.PKIXCommonTypes2009;
using Implicit = Asn1Kit.Modern.PKIX1Implicit2009;
using CertExtensionsBindings = Asn1Kit.Modern.PKIXCommonTypes2009.CertExtensionsBindings;
using SignatureAlgorithmsParametersBindings = Asn1Kit.Modern.AlgorithmInformation2009.SignatureAlgorithmsParametersBindings;

namespace Asn1Kit.Tests;

public sealed class ModernOpenTypeBindingTests
{
    [Fact]
    public void KeyIdentifierBindingsEncodeAndKeepRawCriticality()
    {
        var raw = new Common.Extension {Critical = true};
        raw.SetExtnValue(
            CertExtensionsBindings.AuthorityKeyIdentifier,
            new Implicit.AuthorityKeyIdentifier {KeyIdentifier = new byte[] {0x11, 0x22}});
        var writer = new Asn1Writer(); raw.Encode(writer);
        Assert.Equal("30100603551D230101FF0406300480021122", Convert.ToHexString(writer.Encode()));
        var certificate = new TBSCertificate { Extensions = new[] {raw} };
        Assert.True(certificate.Extensions.TryGet(
            CertExtensionsBindings.AuthorityKeyIdentifier, out var actual, out var matched));
        Assert.True(matched.Critical);
        Assert.Equal(new byte[] {0x11, 0x22}, actual.KeyIdentifier!.Value.ToArray());
        Assert.False(certificate.Extensions.TryGet(
            CertExtensionsBindings.SubjectKeyIdentifier, out _));
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
        subject.SetExtnValue(CertExtensionsBindings.SubjectAltName, names);
        var issuer = new Common.Extension();
        issuer.SetExtnValue(CertExtensionsBindings.IssuerAltName, names);
        Assert.NotEqual(subject.ExtnID, issuer.ExtnID);
        Assert.False(issuer.TryDecodeExtnValue(
            CertExtensionsBindings.SubjectAltName, out _));
        var certificate = new TBSCertificate {Extensions = new[] {subject, issuer}};
        Assert.True(certificate.Extensions.TryGet(
            CertExtensionsBindings.SubjectAltName, out var decodedSubject));
        Assert.True(certificate.Extensions.TryGet(
            CertExtensionsBindings.IssuerAltName, out var decodedIssuer));
        Assert.Equal("example.test", Assert.Single(decodedSubject).DNSName);
        Assert.Equal("example.test", Assert.Single(decodedIssuer).DNSName);
    }

    [Fact]
    public void SignedAndUnsignedTablesDoNotBorrowEachOthersBindings()
    {
        var digest = new Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute();
        digest.SetAttrValues(
            SignedAttributesSetBindings.MessageDigest,
            new ReadOnlyMemory<byte>[] {new byte[] {0xAA}});
        var counter = new Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute();
        counter.SetAttrValues(
            UnsignedAttributesBindings.Countersignature, Array.Empty<SignerInfo>());
        var signer = new SignerInfo
        {
            SignedAttrs = new Asn1Value<Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute[]>(new[] {digest}),
            UnsignedAttrs = new[] {counter, digest}
        };
        Assert.NotNull(signer.SignedAttrs);
        Assert.True(signer.SignedAttrs.Value.Value.TryGet(
            SignedAttributesSetBindings.MessageDigest, out var actual));
        Assert.Equal(new byte[] {0xAA}, Assert.Single(actual).ToArray());
        Assert.True(signer.UnsignedAttrs.TryGet(
            UnsignedAttributesBindings.Countersignature, out var empty));
        Assert.Empty(empty);
        signer.SignedAttrs = new Asn1Value<Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute[]>(new[] {digest, digest});
        Assert.Throws<Asn1Exception>(() =>
            _ = signer.SignedAttrs.Value.Value.TryGet(SignedAttributesSetBindings.MessageDigest, out _));
    }

    [Theory]
    [InlineData("0C0141")]
    [InlineData("130141")]
    [InlineData("1E020041")]
    public void NameAttributesUseDirectoryStringBinding(string encoded)
    {
        var attribute = new Common.SingleAttribute {Type = Asn1Oid.Parse("2.5.4.3"),
            Value = new Asn1Any(Convert.FromHexString(encoded))};
        Assert.True(attribute.TryDecodeValue(
            Asn1Kit.Modern.PKIX1Explicit2009.SupportedAttributesValueBindings.X520CommonName, out var name));
        Assert.IsType<Asn1Kit.Modern.PKIX1Explicit2009.DirectoryString>(name);
        Assert.Equal("A", name.Value);
        var locality = new Common.SingleAttribute
        {
            Type = Asn1Oid.Parse("2.5.4.7"),
            Value = new Asn1Any(Convert.FromHexString(encoded))
        };
        Assert.True(locality.TryDecodeValue(
            Asn1Kit.Modern.PKIX1Explicit2009.SupportedAttributesValueBindings.X520LocalityName,
            out Asn1Kit.Modern.PKIX1Explicit2009.DirectoryString localityName));
        Assert.Equal("A", localityName.Value);
        attribute.Type = Asn1Oid.Parse("1.2.99");
        Assert.False(attribute.TryDecodeValue(
            Asn1Kit.Modern.PKIX1Explicit2009.SupportedAttributesValueBindings.X520CommonName, out _));
    }

    [Fact]
    public void AlgorithmParametersUseCarrierDescriptorsForDecodeAndEncode()
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
        Assert.True(certificate.Signature.TryDecodeParameters(builtIn, out _));
        certificate.Signature.SetParameters(builtIn, Asn1Null.Value);

        var customOid = Asn1Oid.Parse("1.2.3.4");
        var custom = new Asn1Kit.Modern.AlgorithmInformation2009.ParametersBinding<int>(customOid,
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
        certificate.Signature.SetParameters(custom, 42);
        Assert.True(certificate.Signature.TryDecodeParameters(custom, out var value));
        Assert.Equal(42, value);
        Assert.False(certificate.Signature.TryDecodeParameters(builtIn, out _));
        // Carrier Set assigns selector from the binding (does not require a prior matching OID).
        certificate.Signature.SetParameters(builtIn, Asn1Null.Value);
        Assert.Equal(builtIn.Oid, certificate.Signature.Algorithm);
        Assert.True(certificate.Signature.TryDecodeParameters(builtIn, out _));
        Assert.False(certificate.Signature.TryDecodeParameters(custom, out _));
    }

    [Theory]
    [InlineData("TrustAnchorRootCertificate.crt")]
    [InlineData("ValidCertificatePathTest1EE.crt")]
    public void ReadingExistingCertificatesDoesNotChangeTheirDer(string name)
    {
        var bytes = File.ReadAllBytes(TestData.RepoPath("runtime-csharp/fixtures/pkix/" + name));
        var certificate = Certificate.Decode(new Asn1Reader(bytes));
        Assert.True(certificate.ToBeSigned.Extensions.TryGet(
            CertExtensionsBindings.SubjectKeyIdentifier, out var ski));
        Assert.False(ski.IsEmpty);
        certificate.ToBeSigned.Extensions.TryGet(
            CertExtensionsBindings.AuthorityKeyIdentifier, out _);
        Assert.True(certificate.ToBeSigned.TryGetSubject(
            Asn1Kit.Modern.PKIX1Explicit2009.SupportedAttributesValueBindings.X520CommonName, out _));
        var writer = new Asn1Writer(); certificate.Encode(writer);
        Assert.Equal(bytes, writer.Encode());
    }
}

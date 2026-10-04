using Asn1Kit.Cms;
using Asn1Kit.Cms.Demo;
using Asn1Kit.Cms.Demo.Modern;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using CmsAttribute = Asn1Kit.Cms.Attribute;

namespace Asn1Kit.Tests;

public sealed class CmsDemoTests
{
    private static byte[] Fixture => File.ReadAllBytes(TestData.RepoPath("runtime-csharp/fixtures/cms/attached-signeddata.p7m"));

    [Fact]
    public void AttachedFixture_PassesStructureAndSuppliesContentWithoutSignedAttributes()
    {
        var verifier = new RecordingVerifier();
        var results = CmsSignedDataInspector.Inspect(Fixture, verifier);
        Assert.Single(results);
        Assert.True(results[0].Accepted, results[0].Reason);
        Assert.Empty(verifier.Content);
        Assert.Empty(verifier.Digest);
        var info = ContentInfo.Decode(new Asn1Reader(Fixture, Asn1Encoding.Ber));
        Assert.Equal(info.Content.SignedData!.EncapContentInfo.EContent!.Value.ToArray(), verifier.SignedBytes);
        Assert.NotEmpty(verifier.Signature);
    }

    [Fact]
    public void SignedAttributes_SupplyClaimedDigestAndCanonicalSetToStub()
    {
        var encoded = WithSignedAttrs();
        var verifier = new RecordingVerifier();
        var results = CmsSignedDataInspector.Inspect(encoded, verifier);
        Assert.True(results[0].Accepted, results[0].Reason);
        Assert.Equal(new byte[] { 0xAA }, verifier.Digest);
        Assert.Equal(0x31, verifier.SignedBytes[0]);

        var bench = Asn1Kit.Cms.Bench.ContentInfo.Decode(new Asn1Reader(encoded, Asn1Encoding.Ber));
        var retained = bench.Content.SignedData!.SignerInfos[0].SignedAttrs!.Value.OriginalEncoding;
        Assert.False(retained.IsEmpty);
        Assert.Equal(new Asn1Any(retained).ContentsMemory.ToArray(), new Asn1Any(verifier.SignedBytes).ContentsMemory.ToArray());
    }

    [Fact]
    public void IssuerNames_RetainOriginalTlvForMatching()
    {
        var bench = Asn1Kit.Cms.Bench.ContentInfo.Decode(new Asn1Reader(Fixture, Asn1Encoding.Ber));
        var signedData = bench.Content.SignedData!;
        var signerName = signedData.SignerInfos[0].Sid.IssuerAndSerialNumber!.Issuer.OriginalEncoding;
        var certName = signedData.Certificates!.Single(choice => choice.Certificate is not null)
            .Certificate!.Value.TbsCertificate.Value.Issuer.OriginalEncoding;
        Assert.False(signerName.IsEmpty);
        Assert.Equal(signerName.ToArray(), certName.ToArray());
    }

    [Fact]
    public void BadEncodingAndTrailingData_AreRejected()
    {
        Assert.Throws<Asn1Exception>(() => CmsSignedDataInspector.Inspect(new byte[] { 0x30, 0x02, 0x06 }, new RecordingVerifier()));
        Assert.Throws<Asn1Exception>(() => CmsSignedDataInspector.Inspect(Fixture.Concat(new byte[] { 0x00 }).ToArray(), new RecordingVerifier()));
    }

    [Fact]
    public void SubjectKeyIdentifier_MatchesEmbeddedCertificateExtension()
    {
        var encoded = Mutate(info =>
        {
            var keyIdentifier = new byte[] { 0xA1, 0xB2 };
            var certificate = info.Content.SignedData!.Certificates!.Single(choice => choice.Certificate is not null).Certificate!;
            var extension = new Extension
            {
                ExtnID = PKIX1Implicit88Oids.IdCeSubjectKeyIdentifier,
                ExtnValue = Asn1Any.FromValue(keyIdentifier,
                    static (writer, value) => writer.WriteOctetString(Asn1Tag.OctetString, value)).EncodedMemory
            };
            certificate.TbsCertificate.Extensions = (certificate.TbsCertificate.Extensions ?? Array.Empty<Extension>()).Append(extension).ToArray();
            GetSigner(info).Sid = SignerIdentifier.FromSubjectKeyIdentifier(keyIdentifier);
        });
        Assert.True(CmsSignedDataInspector.Inspect(encoded, new RecordingVerifier())[0].Accepted);
        AssertRejected(Mutate(encoded, info => GetSigner(info).Sid = SignerIdentifier.FromSubjectKeyIdentifier(new byte[] { 0x01 })), "certificate");
    }

    [Fact]
    public void Command_UsesDocumentedExitCodesAndLabelsStub()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, CmsDemoCommand.Run(new[] { TestData.RepoPath("runtime-csharp/fixtures/cms/attached-signeddata.p7m") }, output, error));
        Assert.Contains("educational stub", output.ToString());
        Assert.Contains("no cryptographic verification", output.ToString());
        Assert.Equal(2, CmsDemoCommand.Run(Array.Empty<string>(), new StringWriter(), new StringWriter()));
        Assert.Equal(2, CmsDemoCommand.Run(new[] { "missing-file.p7m" }, new StringWriter(), new StringWriter()));

        var malformedPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(malformedPath, new byte[] { 0x00 });
            Assert.Equal(1, CmsDemoCommand.Run(new[] { malformedPath }, new StringWriter(), new StringWriter()));
        }
        finally
        {
            File.Delete(malformedPath);
        }
    }

    [Fact]
    public void TrustedRoot_EnablesRealSignatureAndChainVerification()
    {
        var info = Asn1Kit.Cms.Bench.ContentInfo.Decode(new Asn1Reader(Fixture, Asn1Encoding.Ber));
        var certificate = info.Content.SignedData!.Certificates!.Single(choice => choice.Certificate is not null).Certificate!.Value;
        Assert.False(certificate.TbsCertificate.OriginalEncoding.IsEmpty);
        Assert.False(certificate.TbsCertificate.Value.SubjectPublicKeyInfo.OriginalEncoding.IsEmpty);
        var rootEncoding = info.Content.SignedData.Certificates!.Single(choice => choice.Certificate is not null)
            .Certificate!.EncodedMemory;
        var rootPath = Path.GetTempFileName();
        var invalidCmsPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(rootPath, rootEncoding.ToArray());
            File.WriteAllBytes(invalidCmsPath, Mutate(value => GetSigner(value).Signature = new byte[] { 1 }));
            var output = new StringWriter();
            Assert.Equal(0, CmsDemoCommand.Run(new[] { "--trusted-root", rootPath,
                TestData.RepoPath("runtime-csharp/fixtures/cms/attached-signeddata.p7m") }, output, new StringWriter()));
            Assert.Contains("RSA/SHA-256 verifier", output.ToString());
            Assert.Contains("Certificate chain", output.ToString());
            Assert.Equal(1, CmsDemoCommand.Run(new[] { "--trusted-root", rootPath, invalidCmsPath },
                new StringWriter(), new StringWriter()));

            certificate.Signature = Asn1BitString.CopyFrom(new byte[] { 1 }, 0);
            var invalidCertificateWriter = new Asn1Writer(Asn1Encoding.Der);
            certificate.Encode(invalidCertificateWriter);
            File.WriteAllBytes(rootPath, invalidCertificateWriter.Encode());
            info.Content.SignedData.Certificates = new[] { Asn1Kit.Cms.Bench.CertificateChoices.FromCertificate(
                Asn1Lazy<Asn1Kit.Pkix.Bench.Certificate>.FromValue(certificate)) };
            var invalidChainWriter = new Asn1Writer(Asn1Encoding.Der);
            info.Encode(invalidChainWriter);
            File.WriteAllBytes(invalidCmsPath, invalidChainWriter.Encode());
            var invalidChainOutput = new StringWriter();
            Assert.Equal(1, CmsDemoCommand.Run(new[] { "--trusted-root", rootPath, invalidCmsPath },
                invalidChainOutput, new StringWriter()));
            Assert.Contains("chain signature", invalidChainOutput.ToString());
        }
        finally { File.Delete(rootPath); File.Delete(invalidCmsPath); }
    }

    [Fact]
    public void ChainUsesSuppliedIntermediateAndRejectsMissingLink()
    {
        var info = Asn1Kit.Cms.Bench.ContentInfo.Decode(new Asn1Reader(Fixture, Asn1Encoding.Ber));
        var root = info.Content.SignedData!.Certificates!.Single(choice => choice.Certificate is not null).Certificate!.Value;
        var intermediate = CloneCertificate(root);
        intermediate.TbsCertificate.Value.Subject = BuildName("Intermediate");
        intermediate = CloneCertificate(intermediate);

        var leaf = CloneCertificate(root);
        leaf.TbsCertificate.Value.Subject = BuildName("Leaf");
        leaf.TbsCertificate.Value.Issuer = intermediate.TbsCertificate.Value.Subject.Value;
        info.Content.SignedData.Certificates = new[] { Asn1Kit.Cms.Bench.CertificateChoices.FromCertificate(Asn1Lazy<Asn1Kit.Pkix.Bench.Certificate>.FromValue(leaf)) };
        info.Content.SignedData.SignerInfos[0].Sid.IssuerAndSerialNumber!.Issuer = intermediate.TbsCertificate.Value.Subject.Value;
        var writer = new Asn1Writer(Asn1Encoding.Der);
        info.Encode(writer);
        var encoded = writer.Encode();

        var success = CmsSignedDataInspector.Inspect(encoded, new RecordingVerifier(),
            new[] { RetainCertificate(intermediate) }, new[] { RetainCertificate(root) });
        Assert.True(Assert.Single(success).Accepted, success[0].Reason);
        var failure = CmsSignedDataInspector.Inspect(encoded, new RecordingVerifier(),
            Array.Empty<Asn1Value<Asn1Kit.Pkix.Bench.Certificate>>(), new[] { RetainCertificate(root) });
        Assert.Contains("issuer is unavailable", Assert.Single(failure).Reason);
    }

    [Fact]
    public void AuthorityKeyIdentifier_SelectsIssuerWithMatchingSubjectKeyIdentifier()
    {
        var info = Asn1Kit.Cms.Bench.ContentInfo.Decode(new Asn1Reader(Fixture, Asn1Encoding.Ber));
        var root = info.Content.SignedData!.Certificates!.Single(choice => choice.Certificate is not null).Certificate!.Value;
        var matching = CloneCertificate(root);
        matching.TbsCertificate.Value.Subject = BuildName("Intermediate");
        SetSubjectKeyIdentifier(matching, new byte[] { 0x11 });
        matching = CloneCertificate(matching);

        var other = CloneCertificate(root);
        other.TbsCertificate.Value.Subject = BuildName("Intermediate");
        SetSubjectKeyIdentifier(other, new byte[] { 0x22 });
        other = CloneCertificate(other);

        var leaf = CloneCertificate(root);
        leaf.TbsCertificate.Value.Subject = BuildName("Leaf");
        leaf.TbsCertificate.Value.Issuer = matching.TbsCertificate.Value.Subject.Value;
        SetAuthorityKeyIdentifier(leaf, new byte[] { 0x11 });
        info.Content.SignedData.Certificates = new[] { Asn1Kit.Cms.Bench.CertificateChoices.FromCertificate(
            Asn1Lazy<Asn1Kit.Pkix.Bench.Certificate>.FromValue(leaf)) };
        info.Content.SignedData.SignerInfos[0].Sid.IssuerAndSerialNumber!.Issuer = matching.TbsCertificate.Value.Subject.Value;
        var writer = new Asn1Writer(Asn1Encoding.Der);
        info.Encode(writer);
        var encoded = writer.Encode();

        var benchAvailable = new[] { RetainCertificate(other), RetainCertificate(matching) };
        var benchRoot = new[] { RetainCertificate(root) };
        var benchResult = CmsSignedDataInspector.Inspect(encoded, new RecordingVerifier(), benchAvailable, benchRoot);
        Assert.True(Assert.Single(benchResult).Accepted, benchResult[0].Reason);

        var modernAvailable = benchAvailable.Select(item => RetainModernCertificate(item.OriginalEncoding)).ToArray();
        var modernRoot = new[] { RetainModernCertificate(benchRoot[0].OriginalEncoding) };
        var modernResult = CmsModernSignedDataInspector.Inspect(encoded, new ModernRecordingVerifier(), modernAvailable, modernRoot);
        Assert.True(Assert.Single(modernResult).Accepted, modernResult[0].Reason);

        SetAuthorityKeyIdentifier(leaf, new byte[] { 0x33 });
        writer = new Asn1Writer(Asn1Encoding.Der);
        info.Encode(writer);
        encoded = writer.Encode();
        Assert.Contains("issuer is unavailable", Assert.Single(CmsSignedDataInspector.Inspect(
            encoded, new RecordingVerifier(), benchAvailable, benchRoot)).Reason);
        Assert.Contains("issuer is unavailable", Assert.Single(CmsModernSignedDataInspector.Inspect(
            encoded, new ModernRecordingVerifier(), modernAvailable, modernRoot)).Reason);

        matching.TbsCertificate.Value.SerialNumber = Asn1Integer.FromInt32(101);
        other.TbsCertificate.Value.SerialNumber = Asn1Integer.FromInt32(202);
        benchAvailable = new[] { RetainCertificate(other), RetainCertificate(matching) };
        modernAvailable = benchAvailable.Select(item => RetainModernCertificate(item.OriginalEncoding)).ToArray();
        SetAuthorityKeyIdentifier(leaf, new Asn1Kit.Pkix.Bench.AuthorityKeyIdentifier
        {
            AuthorityCertIssuer = new[] { Asn1Kit.Pkix.Bench.GeneralName.FromDirectoryName(
                matching.TbsCertificate.Value.Issuer.Value) },
            AuthorityCertSerialNumber = matching.TbsCertificate.Value.SerialNumber
        });
        writer = new Asn1Writer(Asn1Encoding.Der);
        info.Encode(writer);
        encoded = writer.Encode();
        benchResult = CmsSignedDataInspector.Inspect(encoded, new RecordingVerifier(), benchAvailable, benchRoot);
        Assert.True(Assert.Single(benchResult).Accepted, benchResult[0].Reason);
        modernResult = CmsModernSignedDataInspector.Inspect(encoded, new ModernRecordingVerifier(), modernAvailable, modernRoot);
        Assert.True(Assert.Single(modernResult).Accepted, modernResult[0].Reason);

        SetAuthorityKeyIdentifier(leaf, new Asn1Kit.Pkix.Bench.AuthorityKeyIdentifier
        {
            AuthorityCertIssuer = new[] { Asn1Kit.Pkix.Bench.GeneralName.FromDirectoryName(
                matching.TbsCertificate.Value.Issuer.Value) },
            AuthorityCertSerialNumber = Asn1Integer.FromInt32(303)
        });
        writer = new Asn1Writer(Asn1Encoding.Der);
        info.Encode(writer);
        encoded = writer.Encode();
        Assert.Contains("issuer is unavailable", Assert.Single(CmsSignedDataInspector.Inspect(
            encoded, new RecordingVerifier(), benchAvailable, benchRoot)).Reason);
        Assert.Contains("issuer is unavailable", Assert.Single(CmsModernSignedDataInspector.Inspect(
            encoded, new ModernRecordingVerifier(), modernAvailable, modernRoot)).Reason);
    }

    private static void SetSubjectKeyIdentifier(Asn1Kit.Pkix.Bench.Certificate certificate, byte[] keyIdentifier)
    {
        var encoded = Asn1Any.FromValue(keyIdentifier,
            static (writer, value) => writer.WriteOctetString(Asn1Tag.OctetString, value)).EncodedMemory;
        SetExtension(certificate, PKIX1Implicit88Oids.IdCeSubjectKeyIdentifier, encoded);
    }

    private static void SetAuthorityKeyIdentifier(Asn1Kit.Pkix.Bench.Certificate certificate, byte[] keyIdentifier)
        => SetAuthorityKeyIdentifier(certificate, new Asn1Kit.Pkix.Bench.AuthorityKeyIdentifier { KeyIdentifier = keyIdentifier });

    private static void SetAuthorityKeyIdentifier(Asn1Kit.Pkix.Bench.Certificate certificate,
        Asn1Kit.Pkix.Bench.AuthorityKeyIdentifier identifier)
    {
        var encoded = Asn1Any.FromValue(identifier,
            static (writer, value) => value.Encode(writer)).EncodedMemory;
        SetExtension(certificate, PKIX1Implicit88Oids.IdCeAuthorityKeyIdentifier, encoded);
    }

    private static void SetExtension(Asn1Kit.Pkix.Bench.Certificate certificate, Asn1Oid oid, ReadOnlyMemory<byte> value)
    {
        var extensions = certificate.TbsCertificate.Value.Extensions is { } existing
            ? existing.Value : Array.Empty<Asn1Kit.Pkix.Bench.Extension>();
        certificate.TbsCertificate.Value.Extensions = extensions
            .Where(extension => extension.ExtnID != oid)
            .Append(new Asn1Kit.Pkix.Bench.Extension { ExtnID = oid, ExtnValue = value }).ToArray();
    }

    private static Asn1Value<Asn1Kit.Modern.PKIX1Explicit2009.Certificate> RetainModernCertificate(ReadOnlyMemory<byte> encoded) =>
        new Asn1Reader(encoded, Asn1Encoding.Der)
            .ReadWithOriginalEncoding(Asn1Kit.Modern.PKIX1Explicit2009.Certificate.Decode);

    private static Asn1Kit.Pkix.Bench.Certificate CloneCertificate(Asn1Kit.Pkix.Bench.Certificate certificate)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        certificate.Encode(writer);
        return Asn1Kit.Pkix.Bench.Certificate.Decode(new Asn1Reader(writer.Encode(), Asn1Encoding.Der));
    }

    private static Asn1Value<Asn1Kit.Pkix.Bench.Certificate> RetainCertificate(Asn1Kit.Pkix.Bench.Certificate certificate)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        certificate.Encode(writer);
        return new Asn1Reader(writer.Encode(), Asn1Encoding.Der)
            .ReadWithOriginalEncoding(Asn1Kit.Pkix.Bench.Certificate.Decode);
    }

    private static Asn1Kit.Pkix.Bench.AttributeTypeAndValue[][] BuildName(string commonName) =>
        new[] { new[] { new Asn1Kit.Pkix.Bench.AttributeTypeAndValue
        {
            Type = Asn1Oid.Parse("2.5.4.3"),
            Value = Asn1Kit.Pkix.Bench.AttributeTypeAndValue_Value.FromUtf8String(commonName)
        } } };

    private static SignerInfo GetSigner(ContentInfo info) => info.Content.SignedData!.SignerInfos[0];

    private static byte[] WithSignedAttrs() => Mutate(info => GetSigner(info).SignedAttrs = new[]
    {
        new CmsAttribute
        {
            AttrType = CryptographicMessageSyntax2004Oids.IdContentType,
            AttrValues = new[] { Asn1Any.FromValue(info.Content.SignedData!.EncapContentInfo.EContentType,
                static (writer, value) => writer.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, value)) }
        },
        new CmsAttribute
        {
            AttrType = CryptographicMessageSyntax2004Oids.IdMessageDigest,
            AttrValues = new[] { Asn1Any.FromValue(new byte[] { 0xAA },
                static (writer, value) => writer.WriteOctetString(Asn1Tag.OctetString, value)) }
        }
    });

    private static byte[] Mutate(Action<ContentInfo> mutate) => Mutate(Fixture, mutate);

    private static byte[] Mutate(byte[] encoded, Action<ContentInfo> mutate)
    {
        var info = ContentInfo.Decode(new Asn1Reader(encoded, Asn1Encoding.Ber));
        mutate(info);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        info.Encode(writer);
        return writer.Encode();
    }

    private static void AssertRejected(byte[] encoded, string reason)
    {
        var result = CmsSignedDataInspector.Inspect(encoded, new RecordingVerifier());
        Assert.Single(result);
        Assert.False(result[0].Accepted);
        Assert.Contains(reason, result[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingVerifier : ICmsCryptoVerifier<Asn1Kit.Pkix.Bench.Certificate>
    {
        public byte[] Content { get; private set; } = Array.Empty<byte>();
        public byte[] Digest { get; private set; } = Array.Empty<byte>();
        public byte[] SignedBytes { get; private set; } = Array.Empty<byte>();
        public byte[] Signature { get; private set; } = Array.Empty<byte>();

        public bool VerifyDigest(Asn1Oid algorithm, ReadOnlyMemory<byte> content, ReadOnlyMemory<byte> claimedDigest, Asn1Kit.Pkix.Bench.Certificate certificate)
        {
            Content = content.ToArray();
            Digest = claimedDigest.ToArray();
            return true;
        }

        public bool VerifySignature(Asn1Oid algorithm, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature, Asn1Kit.Pkix.Bench.Certificate certificate)
        {
            SignedBytes = signedBytes.ToArray();
            Signature = signature.ToArray();
            return true;
        }
    }

    private sealed class ModernRecordingVerifier : ICmsCryptoVerifier<Asn1Kit.Modern.PKIX1Explicit2009.Certificate>
    {
        public bool VerifyDigest(Asn1Oid algorithm, ReadOnlyMemory<byte> content, ReadOnlyMemory<byte> claimedDigest,
            Asn1Kit.Modern.PKIX1Explicit2009.Certificate certificate) => true;

        public bool VerifySignature(Asn1Oid algorithm, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature,
            Asn1Kit.Modern.PKIX1Explicit2009.Certificate certificate) => true;
    }
}

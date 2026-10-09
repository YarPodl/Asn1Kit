using Asn1Kit.Cms;
using Asn1Kit.Cms.Demo;
using Asn1Kit.Cms.Demo.Modern;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using Asn1Kit.Modern.CryptographicMessageSyntax2009;
using CmsAttribute = Asn1Kit.Cms.Attribute;
using ModernCms = Asn1Kit.Modern.CryptographicMessageSyntax2009;
using ModernCertificate = Asn1Kit.Modern.PKIX1Explicit2009.Certificate;
using CmsContentInfo = Asn1Kit.Cms.ContentInfo;
using CmsSignedData = Asn1Kit.Cms.SignedData;

namespace Asn1Kit.Tests;

public sealed class CmsModernDemoTests
{
    private static byte[] Fixture => File.ReadAllBytes(TestData.RepoPath("runtime-csharp/fixtures/cms/attached-signeddata.p7m"));
    private static byte[] WithSignedAttrs() => Mutate((info, signedData) =>
    {
        signedData.SignerInfos[0].SignedAttrs = new[]
        {
            new CmsAttribute
            {
                AttrType = CryptographicMessageSyntax2004Oids.IdContentType,
                AttrValues = new[] { Asn1Any.FromValue(signedData.EncapContentInfo.EContentType,
                    static (writer, value) => writer.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, value)) }
            },
            new CmsAttribute
            {
                AttrType = CryptographicMessageSyntax2004Oids.IdMessageDigest,
                AttrValues = new[] { Asn1Any.FromValue(new byte[] { 0xAA },
                    static (writer, value) => writer.WriteOctetString(Asn1Tag.OctetString, value)) }
            }
        };
        info.SetContent(ContentInfoContentBindings.SignedData, signedData);
    });

    [Fact]
    public void AttachedFixture_UsesModernContainedContent()
    {
        var verifier = new RecordingVerifier();
        var result = CmsModernSignedDataInspector.Inspect(Fixture, verifier);
        Assert.Single(result);
        Assert.True(result[0].Accepted, result[0].Reason);

        var modern = ModernCms.ContentInfo.Decode(new Asn1Reader(Fixture));
        Assert.True(modern.TryDecodeContent<ModernCms.SignedData>(
            ModernCms.ContentSetContentBindings.CtSignedData, out var signedData));
        Assert.Equal(signedData.EncapContentInfo.EContent!.Value.Contents.ToArray(), verifier.SignedBytes);
        var signerIssuer = signedData.SignerInfos[0].Sid.IssuerAndSerialNumber!.Issuer.OriginalEncoding;
        var certificateIssuer = signedData.Certificates!.Single(choice => choice.Certificate is not null)
            .Certificate!.Value.Value.ToBeSigned.Issuer.OriginalEncoding;
        Assert.False(signerIssuer.IsEmpty);
        Assert.Equal(certificateIssuer.ToArray(), signerIssuer.ToArray());
        Assert.Empty(verifier.Digest);
        Assert.NotEmpty(verifier.Signature);
    }

    [Fact]
    public void SignedAttributes_UseUniversalSetAndPassDigestToStub()
    {
        var verifier = new RecordingVerifier();
        var encoded = WithSignedAttrs();
        var result = CmsModernSignedDataInspector.Inspect(encoded, verifier);
        Assert.True(result[0].Accepted, result[0].Reason);
        Assert.Equal(new byte[] { 0xAA }, verifier.Digest);
        Assert.Equal(0x31, verifier.SignedBytes[0]);
        var modern = ModernCms.ContentInfo.Decode(new Asn1Reader(encoded));
        Assert.True(modern.TryDecodeContent<ModernCms.SignedData>(
            ModernCms.ContentSetContentBindings.CtSignedData, out var signedData));
        var retained = signedData.SignerInfos[0].SignedAttrs!.Value.OriginalEncoding;
        Assert.False(retained.IsEmpty);
        Assert.Equal(new Asn1Any(retained).ContentsMemory.ToArray(), new Asn1Any(verifier.SignedBytes).ContentsMemory.ToArray());
    }

    [Fact]
    public void MalformedInputAndMissingRequiredContainerFields_AreRejected()
    {
        Assert.Throws<Asn1Exception>(() => CmsModernSignedDataInspector.Inspect(new byte[] { 0x30, 0x02, 0x06 }, new RecordingVerifier()));
        Assert.Throws<Asn1Exception>(() => CmsModernSignedDataInspector.Inspect(Fixture.Concat(new byte[] { 0 }).ToArray(), new RecordingVerifier()));
        Assert.Throws<InvalidDataException>(() => CmsModernSignedDataInspector.Inspect(Mutate((info, _) =>
            info.ContentType = CryptographicMessageSyntax2004Oids.IdData), new RecordingVerifier()));
        Assert.Throws<InvalidDataException>(() => CmsModernSignedDataInspector.Inspect(Mutate((info, signedData) =>
        {
            signedData.EncapContentInfo.EContent = null;
            info.SetContent(ContentInfoContentBindings.SignedData, signedData);
        }), new RecordingVerifier()));
        Assert.Throws<InvalidDataException>(() => CmsModernSignedDataInspector.Inspect(Mutate((info, signedData) =>
        {
            signedData.SignerInfos = Array.Empty<Asn1Kit.Cms.SignerInfo>();
            info.SetContent(ContentInfoContentBindings.SignedData, signedData);
        }), new RecordingVerifier()));
    }

    [Fact]
    public void DigestAlgorithmAndSignerCertificate_AreChecked()
    {
        AssertRejected(Mutate((info, signedData) =>
        {
            signedData.DigestAlgorithms = Array.Empty<Asn1Kit.Pkix.AlgorithmIdentifier>();
            info.SetContent(ContentInfoContentBindings.SignedData, signedData);
        }), "digest algorithm");
        AssertRejected(Mutate((info, signedData) =>
        {
            signedData.Certificates = null;
            info.SetContent(ContentInfoContentBindings.SignedData, signedData);
        }), "certificate");
        AssertRejected(Mutate((info, signedData) =>
        {
            signedData.SignerInfos[0].Sid.IssuerAndSerialNumber!.SerialNumber = Asn1Integer.FromInt32(12345);
            info.SetContent(ContentInfoContentBindings.SignedData, signedData);
        }), "certificate");
    }

    [Fact]
    public void SubjectKeyIdentifier_MatchesCertificateExtension()
    {
        var encoded = Mutate((info, signedData) =>
        {
            var keyIdentifier = new byte[] { 0xA1, 0xB2 };
            var certificate = signedData.Certificates!.Single(choice => choice.Certificate is not null).Certificate!.Value;
            var extensions = certificate.TbsCertificate.Value.Extensions is { } existing
                ? existing.Value : Array.Empty<Asn1Kit.Pkix.Extension>();
            certificate.TbsCertificate.Value.Extensions = extensions
                .Where(extension => extension.ExtnID != PKIX1Implicit88Oids.IdCeSubjectKeyIdentifier)
                .Append(new Asn1Kit.Pkix.Extension
                {
                    ExtnID = PKIX1Implicit88Oids.IdCeSubjectKeyIdentifier,
                    ExtnValue = Asn1Any.FromValue(keyIdentifier,
                        static (writer, value) => writer.WriteOctetString(Asn1Tag.OctetString, value)).EncodedMemory
                }).ToArray();
            signedData.Certificates = new[]
            {
                Asn1Kit.Cms.CertificateChoices.FromCertificate(Asn1Lazy<Asn1Kit.Pkix.Certificate>.FromValue(certificate))
            };
            signedData.SignerInfos[0].Sid = Asn1Kit.Cms.SignerIdentifier.FromSubjectKeyIdentifier(keyIdentifier);
            info.SetContent(ContentInfoContentBindings.SignedData, signedData);
        });
        Assert.True(CmsModernSignedDataInspector.Inspect(encoded, new RecordingVerifier())[0].Accepted);
        AssertRejected(Mutate(encoded, (info, signedData) =>
        {
            signedData.SignerInfos[0].Sid = Asn1Kit.Cms.SignerIdentifier.FromSubjectKeyIdentifier(new byte[] { 1 });
            info.SetContent(ContentInfoContentBindings.SignedData, signedData);
        }), "certificate");
    }

    [Fact]
    public void ModernTrustedRoot_VerifiesChainAndCmsSignature()
    {
        var info = ModernCms.ContentInfo.Decode(new Asn1Reader(Fixture, Asn1Encoding.Ber));
        Assert.True(info.TryDecodeContent<ModernCms.SignedData>(
            ModernCms.ContentSetContentBindings.CtSignedData, out var signedData));
        var certificate = signedData.Certificates!.Single(choice => choice.Certificate is not null).Certificate!.Value;
        Assert.False(certificate.OriginalEncoding.IsEmpty);
        Assert.False(certificate.Value.ToBeSignedOriginalEncoding.IsEmpty);
        Assert.False(certificate.Value.ToBeSigned.SubjectPublicKeyInfo.OriginalEncoding.IsEmpty);
        var rootPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(rootPath, certificate.OriginalEncoding.ToArray());
            var output = new StringWriter();
            Assert.Equal(0, CmsDemoCommand.Run(new[] { "--modern", "--trusted-root", rootPath,
                TestData.RepoPath("runtime-csharp/fixtures/cms/attached-signeddata.p7m") }, output, new StringWriter()));
            Assert.Contains("RSA/SHA-256 verifier", output.ToString());
        }
        finally { File.Delete(rootPath); }
    }

    private static byte[] Mutate(Action<CmsContentInfo, CmsSignedData> mutate) => Mutate(Fixture, mutate);

    private static byte[] Mutate(byte[] encoded, Action<CmsContentInfo, CmsSignedData> mutate)
    {
        var info = CmsContentInfo.Decode(new Asn1Reader(encoded, Asn1Encoding.Ber));
        Assert.True(info.TryDecodeContent(ContentInfoContentBindings.SignedData, out CmsSignedData signedData));
        mutate(info, signedData);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        info.Encode(writer);
        return writer.Encode();
    }

    private static void AssertRejected(byte[] encoded, string reason)
    {
        var result = CmsModernSignedDataInspector.Inspect(encoded, new RecordingVerifier());
        Assert.Single(result);
        Assert.False(result[0].Accepted);
        Assert.Contains(reason, result[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingVerifier : ICmsCryptoVerifier<ModernCertificate>
    {
        public byte[] Digest { get; private set; } = Array.Empty<byte>();
        public byte[] SignedBytes { get; private set; } = Array.Empty<byte>();
        public byte[] Signature { get; private set; } = Array.Empty<byte>();

        public bool VerifyDigest(Asn1Oid algorithm, ReadOnlyMemory<byte> content, ReadOnlyMemory<byte> claimedDigest, ModernCertificate certificate)
        {
            Digest = claimedDigest.ToArray();
            return true;
        }

        public bool VerifySignature(Asn1Oid algorithm, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature, ModernCertificate certificate)
        {
            SignedBytes = signedBytes.ToArray();
            Signature = signature.ToArray();
            return true;
        }
    }
}

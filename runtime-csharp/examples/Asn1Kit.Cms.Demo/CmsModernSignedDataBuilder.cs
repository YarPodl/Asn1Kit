using Asn1Kit.Cms.Demo;
using Asn1Kit.Runtime;
using ModernCms = Asn1Kit.Modern.CryptographicMessageSyntax2009;
using ModernCertificate = Asn1Kit.Modern.PKIX1Explicit2009.Certificate;
using ModernAlg = Asn1Kit.Modern.AlgorithmInformation2009;

namespace Asn1Kit.Cms.Demo.Modern;

public static class CmsModernSignedDataBuilder
{
    public static byte[] BuildAttached(ReadOnlyMemory<byte> content, Asn1Value<ModernCertificate> certificate)
    {
        if (certificate.OriginalEncoding.IsEmpty)
            throw new ArgumentException("Signer certificate must retain its original encoding.", nameof(certificate));

        var digest = EducationalCmsSigning.DigestSha256(content.Span);
        var digestAlgorithm = new ModernAlg.AlgorithmIdentifier { Algorithm = EducationalCmsSigning.Sha256 };
        var signatureAlgorithm = new ModernAlg.AlgorithmIdentifier { Algorithm = EducationalCmsSigning.RsaEncryption };
        var tbs = certificate.Value.ToBeSigned;

        var signer = new ModernCms.SignerInfo
        {
            Version = ModernCms.CMSVersion.V1,
            Sid = ModernCms.SignerIdentifier.FromIssuerAndSerialNumber(new ModernCms.IssuerAndSerialNumber
            {
                Issuer = tbs.Issuer,
                SerialNumber = tbs.SerialNumber
            }),
            DigestAlgorithm = digestAlgorithm,
            SignedAttrs = new[]
            {
                ModernCms.SignedAttributesSetBindings.ContentType.Create(
                    ModernCms.CryptographicMessageSyntax2009Oids.IdData),
                ModernCms.SignedAttributesSetBindings.MessageDigest.Create(digest),
            },
            SignatureAlgorithm = signatureAlgorithm,
            Signature = EducationalCmsSigning.PlaceholderSignature
        };

        var signedData = new ModernCms.SignedData
        {
            Version = ModernCms.CMSVersion.V1,
            DigestAlgorithms = new[] { digestAlgorithm },
            EncapContentInfo = new ModernCms.EncapsulatedContentInfo
            {
                EContentType = ModernCms.CryptographicMessageSyntax2009Oids.IdData,
                EContent = Asn1Contained<Asn1Any>.FromEncoded(content)
            },
            Certificates = new[] { ModernCms.CertificateChoices.FromCertificate(certificate) },
            SignerInfos = new[] { signer }
        };

        var contentInfo = ModernCms.ContentSetContentBindings.CtSignedData.Create(signedData);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        contentInfo.Encode(writer);
        return writer.Encode();
    }
}

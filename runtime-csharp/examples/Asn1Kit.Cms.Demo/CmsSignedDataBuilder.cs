using Asn1Kit.Cms;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using CmsAttribute = Asn1Kit.Cms.Attribute;

namespace Asn1Kit.Cms.Demo;

public static class CmsSignedDataBuilder
{
    public static byte[] BuildAttached(ReadOnlyMemory<byte> content, Asn1Value<Certificate> certificate)
    {
        if (certificate.OriginalEncoding.IsEmpty)
            throw new ArgumentException("Signer certificate must retain its original encoding.", nameof(certificate));

        var digest = EducationalCmsSigning.DigestSha256(content.Span);
        var digestAlgorithm = new AlgorithmIdentifier { Algorithm = EducationalCmsSigning.Sha256 };
        var signatureAlgorithm = new AlgorithmIdentifier { Algorithm = EducationalCmsSigning.RsaEncryption };
        var tbs = certificate.Value.TbsCertificate.Value;
        var signedAttrs = new[]
        {
            new CmsAttribute
            {
                AttrType = CryptographicMessageSyntax2004Oids.IdContentType,
                AttrValues = new[]
                {
                    Asn1Any.FromValue(CryptographicMessageSyntax2004Oids.IdData,
                        static (writer, value) => writer.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, value))
                }
            },
            new CmsAttribute
            {
                AttrType = CryptographicMessageSyntax2004Oids.IdMessageDigest,
                AttrValues = new[]
                {
                    Asn1Any.FromValue(digest,
                        static (writer, value) => writer.WriteOctetString(Asn1Tag.OctetString, value))
                }
            }
        };

        var signer = new SignerInfo
        {
            Version = CMSVersion.V1,
            Sid = SignerIdentifier.FromIssuerAndSerialNumber(new IssuerAndSerialNumber
            {
                Issuer = tbs.Issuer,
                SerialNumber = tbs.SerialNumber
            }),
            DigestAlgorithm = digestAlgorithm,
            SignedAttrs = signedAttrs,
            SignatureAlgorithm = signatureAlgorithm,
            Signature = EducationalCmsSigning.PlaceholderSignature
        };

        var signedData = new SignedData
        {
            Version = CMSVersion.V1,
            DigestAlgorithms = new[] { digestAlgorithm },
            EncapContentInfo = new EncapsulatedContentInfo
            {
                EContentType = CryptographicMessageSyntax2004Oids.IdData,
                EContent = content
            },
            Certificates = new[]
            {
                CertificateChoices.FromCertificate(Asn1Lazy<Certificate>.FromEncoded(
                    certificate.OriginalEncoding,
                    Asn1Encoding.Der,
                    Asn1ReaderOptions.Default,
                    Certificate.Decode))
            },
            SignerInfos = new[] { signer }
        };

        var contentInfo = new ContentInfo();
        contentInfo.SetContent(ContentInfoContentBindings.SignedData, signedData);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        contentInfo.Encode(writer);
        return writer.Encode();
    }
}

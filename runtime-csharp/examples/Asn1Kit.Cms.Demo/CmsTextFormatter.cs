using Asn1Kit.Runtime;
using Asn1Kit.Cms;
using ModernCms = Asn1Kit.Modern.CryptographicMessageSyntax2009;
using CmsAttribute = Asn1Kit.Cms.Attribute;
using ModernAttribute = Asn1Kit.Modern.CryptographicMessageSyntax2009.Attribute;

namespace Asn1Kit.Cms.Demo;

public static class CmsTextFormatter
{
    public static void Write(TextWriter output, ReadOnlyMemory<byte> encoded, bool modern)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (modern)
            WriteModern(output, encoded);
        else
            WritePkix(output, encoded);
    }

    public static void WritePkix(TextWriter output, ReadOnlyMemory<byte> encoded)
    {
        ArgumentNullException.ThrowIfNull(output);
        var contentInfo = Asn1Utils.Decode(encoded, ContentInfo.Decode);
        if (contentInfo.ContentType != CryptographicMessageSyntax2004Oids.IdSignedData ||
            !contentInfo.TryDecodeContent(ContentInfoContentBindings.SignedData, out SignedData signedData))
            throw new InvalidDataException("ContentInfo must contain SignedData.");

        var writer = new DemoTextWriter(output);
        writer.Line("CMS_ContentInfo:");
        writer.Push();
        writer.Line("contentType: " + CmsDemoOidNames.Format(contentInfo.ContentType));
        writer.Line("d.signedData:");
        writer.Push();
        writer.Line("version: " + signedData.Version);
        writer.Line("digestAlgorithms:");
        writer.Push();
        foreach (var algorithm in signedData.DigestAlgorithms)
            writer.Line("algorithm: " + CmsDemoOidNames.Format(algorithm.Algorithm));
        if (signedData.DigestAlgorithms.Length == 0)
            writer.Line("<absent>");
        writer.Pop();
        writer.Line("encapContentInfo:");
        writer.Push();
        writer.Line("eContentType: " + CmsDemoOidNames.Format(signedData.EncapContentInfo.EContentType));
        if (signedData.EncapContentInfo.EContent is { } content)
        {
            writer.Line("eContent:");
            writer.Push();
            writer.BytesSummary("octet string", content.Span);
            writer.Pop();
        }
        else
            writer.Line("eContent: <absent>");
        writer.Pop();
        WritePkixCertificates(writer, signedData.Certificates);
        writer.Line(signedData.Crls is null
            ? "crls: <absent>"
            : "crls: " + signedData.Crls.Length);
        writer.Line("signerInfos:");
        writer.Push();
        if (signedData.SignerInfos.Length == 0)
            writer.Line("<absent>");
        foreach (var signer in signedData.SignerInfos)
            WritePkixSignerInfo(writer, signer);
        writer.Pop();
        writer.Pop();
        writer.Pop();
    }

    public static void WriteModern(TextWriter output, ReadOnlyMemory<byte> encoded)
    {
        ArgumentNullException.ThrowIfNull(output);
        var contentInfo = Asn1Utils.Decode(encoded, ModernCms.ContentInfo.Decode);
        if (contentInfo.ContentType != ModernCms.CryptographicMessageSyntax2009Oids.IdSignedData ||
            !TryDecodeModernSignedData(contentInfo, out var signedData))
            throw new InvalidDataException("ContentInfo must contain SignedData.");

        var writer = new DemoTextWriter(output);
        writer.Line("CMS_ContentInfo:");
        writer.Push();
        writer.Line("contentType: " + CmsDemoOidNames.Format(contentInfo.ContentType));
        writer.Line("d.signedData:");
        writer.Push();
        writer.Line("version: " + signedData.Version);
        writer.Line("digestAlgorithms:");
        writer.Push();
        foreach (var algorithm in signedData.DigestAlgorithms)
            writer.Line("algorithm: " + CmsDemoOidNames.Format(algorithm.Algorithm));
        if (signedData.DigestAlgorithms.Length == 0)
            writer.Line("<absent>");
        writer.Pop();
        writer.Line("encapContentInfo:");
        writer.Push();
        writer.Line("eContentType: " + CmsDemoOidNames.Format(signedData.EncapContentInfo.EContentType));
        if (signedData.EncapContentInfo.EContent is { } content)
        {
            writer.Line("eContent:");
            writer.Push();
            writer.BytesSummary("octet string", content.Contents.Span);
            writer.Pop();
        }
        else
            writer.Line("eContent: <absent>");
        writer.Pop();
        WriteModernCertificates(writer, signedData.Certificates);
        writer.Line(signedData.Crls is null
            ? "crls: <absent>"
            : "crls: " + signedData.Crls.Length);
        writer.Line("signerInfos:");
        writer.Push();
        if (signedData.SignerInfos.Length == 0)
            writer.Line("<absent>");
        foreach (var signer in signedData.SignerInfos)
            WriteModernSignerInfo(writer, signer);
        writer.Pop();
        writer.Pop();
        writer.Pop();
    }

    private static bool TryDecodeModernSignedData(ModernCms.ContentInfo contentInfo, out ModernCms.SignedData signedData)
        => ModernCms.CryptographicMessageSyntax2009OpenTypeExtensions.TryDecodeContent(
            contentInfo, ModernCms.ContentSetContentBindings.CtSignedData, out signedData);

    private static void WritePkixCertificates(DemoTextWriter writer, CertificateChoices[]? certificates)
    {
        if (certificates is null || certificates.Length == 0)
        {
            writer.Line("certificates: <absent>");
            return;
        }

        writer.Line("certificates:");
        writer.Push();
        foreach (var choice in certificates)
        {
            if (choice.Kind != CertificateChoicesKind.Certificate || choice.Certificate is null)
            {
                writer.Line("CertificateChoices: " + choice.Kind);
                continue;
            }

            CertificateTextFormatter.Write(writer, choice.Certificate.Value);
        }
        writer.Pop();
    }

    private static void WriteModernCertificates(DemoTextWriter writer, ModernCms.CertificateChoices[]? certificates)
    {
        if (certificates is null || certificates.Length == 0)
        {
            writer.Line("certificates: <absent>");
            return;
        }

        writer.Line("certificates:");
        writer.Push();
        foreach (var choice in certificates)
        {
            if (choice.Kind != ModernCms.CertificateChoicesKind.Certificate || choice.Certificate is null)
            {
                writer.Line("CertificateChoices: " + choice.Kind);
                continue;
            }

            CertificateTextFormatter.Write(writer, choice.Certificate.Value.Value);
        }
        writer.Pop();
    }

    private static void WritePkixSignerInfo(DemoTextWriter writer, SignerInfo signer)
    {
        writer.Line("SignerInfo:");
        writer.Push();
        writer.Line("version: " + signer.Version);
        writer.Line("sid:");
        writer.Push();
        WritePkixSid(writer, signer.Sid);
        writer.Pop();
        writer.Line("digestAlgorithm: " + CmsDemoOidNames.Format(signer.DigestAlgorithm.Algorithm));
        WritePkixAttributes(writer, "signedAttrs", signer.SignedAttrs?.Value);
        writer.Line("signatureAlgorithm: " + CmsDemoOidNames.Format(signer.SignatureAlgorithm.Algorithm));
        writer.BytesSummary("signature", signer.Signature.Span);
        WritePkixAttributes(writer, "unsignedAttrs", signer.UnsignedAttrs);
        writer.Pop();
    }

    private static void WriteModernSignerInfo(DemoTextWriter writer, ModernCms.SignerInfo signer)
    {
        writer.Line("SignerInfo:");
        writer.Push();
        writer.Line("version: " + signer.Version);
        writer.Line("sid:");
        writer.Push();
        WriteModernSid(writer, signer.Sid);
        writer.Pop();
        writer.Line("digestAlgorithm: " + CmsDemoOidNames.Format(signer.DigestAlgorithm.Algorithm));
        WriteModernAttributes(writer, "signedAttrs", signer.SignedAttrs?.Value);
        writer.Line("signatureAlgorithm: " + CmsDemoOidNames.Format(signer.SignatureAlgorithm.Algorithm));
        writer.BytesSummary("signature", signer.Signature.Span);
        WriteModernAttributes(writer, "unsignedAttrs", signer.UnsignedAttrs);
        writer.Pop();
    }

    private static void WritePkixSid(DemoTextWriter writer, SignerIdentifier sid)
    {
        if (sid.Kind == SignerIdentifierKind.IssuerAndSerialNumber && sid.IssuerAndSerialNumber is { } ias)
        {
            writer.Line("issuerAndSerialNumber:");
            writer.Push();
            writer.Line("issuer: " + RdnSequenceFormatter.Format(ias.Issuer.Value));
            writer.Line("serialNumber: " + DemoTextWriter.FormatSerial(ias.SerialNumber));
            writer.Pop();
        }
        else if (sid.Kind == SignerIdentifierKind.SubjectKeyIdentifier && sid.SubjectKeyIdentifier is { } ski)
        {
            writer.Line("subjectKeyIdentifier:");
            writer.Push();
            writer.Hex(ski.Span);
            writer.Pop();
        }
        else
            writer.Line("<unknown>");
    }

    private static void WriteModernSid(DemoTextWriter writer, ModernCms.SignerIdentifier sid)
    {
        if (sid.Kind == ModernCms.SignerIdentifierKind.IssuerAndSerialNumber && sid.IssuerAndSerialNumber is { } ias)
        {
            writer.Line("issuerAndSerialNumber:");
            writer.Push();
            writer.Line("issuer: " + RdnSequenceFormatter.Format(ias.Issuer.Value));
            writer.Line("serialNumber: " + DemoTextWriter.FormatSerial(ias.SerialNumber));
            writer.Pop();
        }
        else if (sid.Kind == ModernCms.SignerIdentifierKind.SubjectKeyIdentifier && sid.SubjectKeyIdentifier is { } ski)
        {
            writer.Line("subjectKeyIdentifier:");
            writer.Push();
            writer.Hex(ski.Span);
            writer.Pop();
        }
        else
            writer.Line("<unknown>");
    }

    private static void WritePkixAttributes(DemoTextWriter writer, string label, CmsAttribute[]? attributes)
    {
        if (attributes is null || attributes.Length == 0)
        {
            writer.Line(label + ": <absent>");
            return;
        }

        writer.Line(label + ":");
        writer.Push();
        foreach (var attribute in attributes)
        {
            writer.Line("attrType: " + CmsDemoOidNames.Format(attribute.AttrType));
            writer.Line("attrValues: " + attribute.AttrValues.Length);
            writer.Push();
            foreach (var value in attribute.AttrValues)
                writer.Hex(value.EncodedMemory.Span);
            writer.Pop();
        }
        writer.Pop();
    }

    private static void WriteModernAttributes(DemoTextWriter writer, string label, ModernAttribute[]? attributes)
    {
        if (attributes is null || attributes.Length == 0)
        {
            writer.Line(label + ": <absent>");
            return;
        }

        writer.Line(label + ":");
        writer.Push();
        foreach (var attribute in attributes)
        {
            writer.Line("attrType: " + CmsDemoOidNames.Format(attribute.AttrType));
            writer.Line("attrValues: " + attribute.AttrValues.Length);
            writer.Push();
            foreach (var value in attribute.AttrValues)
                writer.Hex(value.EncodedMemory.Span);
            writer.Pop();
        }
        writer.Pop();
    }
}

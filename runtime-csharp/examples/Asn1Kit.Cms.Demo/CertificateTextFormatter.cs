using Asn1Kit.Runtime;
using PkixCertificate = Asn1Kit.Pkix.Certificate;
using ModernCertificate = Asn1Kit.Modern.PKIX1Explicit2009.Certificate;

namespace Asn1Kit.Cms.Demo;

public static class CertificateTextFormatter
{
    public static void Write(TextWriter output, PkixCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(output);
        Write(new DemoTextWriter(output), certificate);
    }

    public static void Write(TextWriter output, ModernCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(output);
        Write(new DemoTextWriter(output), certificate);
    }

    internal static void Write(DemoTextWriter writer, PkixCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var tbs = certificate.TbsCertificate.Value;
        writer.Line("Certificate:");
        writer.Push();
        writer.Line("Data:");
        writer.Push();
        WriteVersion(writer, tbs.Version);
        writer.Line("Serial Number:");
        writer.Push();
        writer.Line(DemoTextWriter.FormatSerial(tbs.SerialNumber));
        writer.Pop();
        writer.Line("Signature Algorithm: " + CmsDemoOidNames.Format(tbs.Signature.Algorithm));
        writer.Line("Issuer: " + RdnSequenceFormatter.Format(tbs.Issuer.Value));
        writer.Line("Validity");
        writer.Push();
        writer.Line("Not Before: " + DemoTextWriter.FormatTime(tbs.Validity.NotBefore.Value));
        writer.Line("Not After : " + DemoTextWriter.FormatTime(tbs.Validity.NotAfter.Value));
        writer.Pop();
        writer.Line("Subject: " + RdnSequenceFormatter.Format(tbs.Subject.Value));
        writer.Line("Subject Public Key Info:");
        writer.Push();
        var spki = tbs.SubjectPublicKeyInfo.Value;
        writer.Line("Public Key Algorithm: " + CmsDemoOidNames.Format(spki.Algorithm.Algorithm));
        writer.Line("Public-Key: (" + DemoTextWriter.FormatBitLength(spki.SubjectPublicKey) + ")");
        writer.Pop();
        var extensions = tbs.Extensions?.Value ?? Array.Empty<Asn1Kit.Pkix.Extension>();
        if (extensions.Length != 0)
        {
            writer.Line("X509v3 extensions:");
            writer.Push();
            foreach (var extension in extensions)
            {
                writer.Line(CmsDemoOidNames.Format(extension.ExtnID) + ": " +
                            (extension.Critical ? "critical" : "non-critical"));
                writer.Push();
                writer.Hex(extension.ExtnValue.Span);
                writer.Pop();
            }
            writer.Pop();
        }
        writer.Pop();
        writer.Line("Signature Algorithm: " + CmsDemoOidNames.Format(certificate.SignatureAlgorithm.Algorithm));
        writer.Hex(certificate.Signature.Span);
        writer.Pop();
    }

    internal static void Write(DemoTextWriter writer, ModernCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var tbs = certificate.ToBeSigned;
        writer.Line("Certificate:");
        writer.Push();
        writer.Line("Data:");
        writer.Push();
        WriteVersion(writer, tbs.Version);
        writer.Line("Serial Number:");
        writer.Push();
        writer.Line(DemoTextWriter.FormatSerial(tbs.SerialNumber));
        writer.Pop();
        writer.Line("Signature Algorithm: " + CmsDemoOidNames.Format(tbs.Signature.Algorithm));
        writer.Line("Issuer: " + RdnSequenceFormatter.Format(tbs.Issuer.Value));
        writer.Line("Validity");
        writer.Push();
        writer.Line("Not Before: " + DemoTextWriter.FormatTime(tbs.Validity.NotBefore.Value));
        writer.Line("Not After : " + DemoTextWriter.FormatTime(tbs.Validity.NotAfter.Value));
        writer.Pop();
        writer.Line("Subject: " + RdnSequenceFormatter.Format(tbs.Subject.Value));
        writer.Line("Subject Public Key Info:");
        writer.Push();
        var spki = tbs.SubjectPublicKeyInfo.Value;
        writer.Line("Public Key Algorithm: " + CmsDemoOidNames.Format(spki.Algorithm.Algorithm));
        writer.Line("Public-Key: (" + DemoTextWriter.FormatBitLength(spki.SubjectPublicKey) + ")");
        writer.Pop();
        var extensions = tbs.Extensions ?? Array.Empty<Asn1Kit.Modern.PKIXCommonTypes2009.Extension>();
        if (extensions.Length != 0)
        {
            writer.Line("X509v3 extensions:");
            writer.Push();
            foreach (var extension in extensions)
            {
                writer.Line(CmsDemoOidNames.Format(extension.ExtnID) + ": " +
                            (extension.Critical ? "critical" : "non-critical"));
                writer.Push();
                writer.Hex(extension.ExtnValue.Contents.Span);
                writer.Pop();
            }
            writer.Pop();
        }
        writer.Pop();
        writer.Line("Signature Algorithm: " + CmsDemoOidNames.Format(certificate.AlgorithmIdentifier.Algorithm));
        writer.Hex(certificate.Signature.Span);
        writer.Pop();
    }

    private static void WriteVersion(DemoTextWriter writer, int version)
    {
        // ASN.1 Version is zero-based (v1=0, v3=2); openssl prints Version: 3 (0x2).
        var display = version + 1;
        writer.Line($"Version: {display} (0x{version:X})");
    }
}

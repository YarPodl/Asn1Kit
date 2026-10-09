using Asn1Kit.Cms.Demo.Modern;
using Asn1Kit.Runtime;
using PkixCertificate = Asn1Kit.Pkix.Certificate;
using ModernCertificate = Asn1Kit.Modern.PKIX1Explicit2009.Certificate;

namespace Asn1Kit.Cms.Demo;

public static class CmsDemoCommand
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
            return Usage(error);

        var command = args[0];
        var rest = args.AsSpan(1).ToArray();
        return command switch
        {
            "verify" => RunVerify(rest, output, error),
            "print-cert" => RunPrintCert(rest, output, error),
            "print-cms" => RunPrintCms(rest, output, error),
            "sign" => RunSign(rest, output, error),
            _ => Usage(error)
        };
    }

    private static int RunVerify(string[] args, TextWriter output, TextWriter error)
    {
        var modern = false;
        var rootPaths = new List<string>();
        var certificatePaths = new List<string>();
        string? cmsPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--modern")
                modern = true;
            else if ((args[index] is "--trusted-root" or "--certificate") && index + 1 < args.Length)
            {
                var paths = args[index] == "--trusted-root" ? rootPaths : certificatePaths;
                paths.Add(args[++index]);
            }
            else if (!args[index].StartsWith("--", StringComparison.Ordinal) && cmsPath is null)
                cmsPath = args[index];
            else
                return Usage(error);
        }

        if (string.IsNullOrWhiteSpace(cmsPath) || (certificatePaths.Count != 0 && rootPaths.Count == 0))
            return Usage(error);

        try
        {
            var encoded = File.ReadAllBytes(cmsPath);
            IReadOnlyList<CmsSignerResult> results;
            if (modern)
            {
                var roots = rootPaths.Count == 0 ? null : rootPaths.Select(LoadModernCertificate).ToArray();
                var available = certificatePaths.Select(LoadModernCertificate).ToArray();
                results = CmsModernSignedDataInspector.Inspect(encoded,
                    roots is null ? new EducationalCryptoVerifier<ModernCertificate>() :
                        new RsaSha256CryptoVerifier<ModernCertificate>(certificate =>
                            certificate.ToBeSigned.SubjectPublicKeyInfo.OriginalEncoding),
                    available, roots);
            }
            else
            {
                var roots = rootPaths.Count == 0 ? null : rootPaths.Select(LoadPkixCertificate).ToArray();
                var available = certificatePaths.Select(LoadPkixCertificate).ToArray();
                results = CmsSignedDataInspector.Inspect(encoded,
                    roots is null ? new EducationalCryptoVerifier<PkixCertificate>() :
                        new RsaSha256CryptoVerifier<PkixCertificate>(certificate =>
                            certificate.TbsCertificate.Value.SubjectPublicKeyInfo.OriginalEncoding),
                    available, roots);
            }

            var accepted = true;
            foreach (var result in results)
            {
                output.WriteLine(
                    $"Signer {result.SignerNumber}: {(result.Accepted ? "accepted" : "rejected")} by {(rootPaths.Count == 0 ? "educational stub" : "RSA/SHA-256 verifier")}: {result.Reason}");
                if (result.Issuer is not null)
                {
                    output.WriteLine($"  Issuer: {result.Issuer}");
                    output.WriteLine($"  Subject: {result.Subject}");
                }

                accepted &= result.Accepted;
            }

            output.WriteLine(rootPaths.Count == 0
                ? "Educational demonstration only: no cryptographic verification or certificate trust validation was performed."
                : "Certificate chain and CMS signature verified with RSA/SHA-256; revocation, validity time and certificate policies were not checked.");
            return accepted ? 0 : 1;
        }
        catch (Exception ex) when (ex is InvalidDataException or Asn1Exception)
        {
            error.WriteLine($"CMS rejected: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"Cannot read CMS or certificate file: {ex.Message}");
            return 2;
        }
    }

    private static int RunPrintCert(string[] args, TextWriter output, TextWriter error)
    {
        var modern = false;
        string? certPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--modern")
                modern = true;
            else if (!args[index].StartsWith("--", StringComparison.Ordinal) && certPath is null)
                certPath = args[index];
            else
                return Usage(error);
        }

        if (string.IsNullOrWhiteSpace(certPath))
            return Usage(error);

        try
        {
            if (modern)
                CertificateTextFormatter.Write(output, LoadModernCertificate(certPath).Value);
            else
                CertificateTextFormatter.Write(output, LoadPkixCertificate(certPath).Value);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or Asn1Exception)
        {
            error.WriteLine($"Certificate rejected: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"Cannot read certificate file: {ex.Message}");
            return 2;
        }
    }

    private static int RunPrintCms(string[] args, TextWriter output, TextWriter error)
    {
        var modern = false;
        string? cmsPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--modern")
                modern = true;
            else if (!args[index].StartsWith("--", StringComparison.Ordinal) && cmsPath is null)
                cmsPath = args[index];
            else
                return Usage(error);
        }

        if (string.IsNullOrWhiteSpace(cmsPath))
            return Usage(error);

        try
        {
            CmsTextFormatter.Write(output, File.ReadAllBytes(cmsPath), modern);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or Asn1Exception)
        {
            error.WriteLine($"CMS rejected: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"Cannot read CMS file: {ex.Message}");
            return 2;
        }
    }

    private static int RunSign(string[] args, TextWriter output, TextWriter error)
    {
        var modern = false;
        string? certificatePath = null;
        string? contentPath = null;
        string? outputPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--modern")
                modern = true;
            else if (args[index] == "--certificate" && index + 1 < args.Length)
                certificatePath = args[++index];
            else if (args[index] == "--content" && index + 1 < args.Length)
                contentPath = args[++index];
            else if (args[index] == "--output" && index + 1 < args.Length)
                outputPath = args[++index];
            else
                return Usage(error);
        }

        if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(contentPath) ||
            string.IsNullOrWhiteSpace(outputPath))
            return Usage(error);

        try
        {
            var content = File.ReadAllBytes(contentPath);
            byte[] encoded;
            string issuer;
            string subject;
            if (modern)
            {
                var certificate = LoadModernCertificate(certificatePath);
                encoded = CmsModernSignedDataBuilder.BuildAttached(content, certificate);
                issuer = RdnSequenceFormatter.Format(certificate.Value.ToBeSigned.Issuer.Value);
                subject = RdnSequenceFormatter.Format(certificate.Value.ToBeSigned.Subject.Value);
            }
            else
            {
                var certificate = LoadPkixCertificate(certificatePath);
                encoded = CmsSignedDataBuilder.BuildAttached(content, certificate);
                issuer = RdnSequenceFormatter.Format(certificate.Value.TbsCertificate.Value.Issuer.Value);
                subject = RdnSequenceFormatter.Format(certificate.Value.TbsCertificate.Value.Subject.Value);
            }

            File.WriteAllBytes(outputPath, encoded);
            output.WriteLine("Educational CMS SignedData written (placeholder signature; not cryptographically valid).");
            output.WriteLine($"  Output: {outputPath}");
            output.WriteLine($"  Issuer: {issuer}");
            output.WriteLine($"  Subject: {subject}");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or Asn1Exception)
        {
            error.WriteLine($"CMS sign rejected: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"Cannot read or write sign inputs: {ex.Message}");
            return 2;
        }
    }

    private static Asn1Value<PkixCertificate> LoadPkixCertificate(string path) =>
        Asn1Utils.DecodeRetained(File.ReadAllBytes(path), PkixCertificate.Decode);

    private static Asn1Value<ModernCertificate> LoadModernCertificate(string path) =>
        Asn1Utils.DecodeRetained(File.ReadAllBytes(path), ModernCertificate.Decode);

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage:");
        error.WriteLine("  Asn1Kit.Cms.Demo verify [--modern] [--trusted-root <root.cer>]... [--certificate <intermediate.cer>]... <attached-signeddata.p7m>");
        error.WriteLine("  Asn1Kit.Cms.Demo print-cert [--modern] <certificate.cer>");
        error.WriteLine("  Asn1Kit.Cms.Demo print-cms [--modern] <attached-signeddata.p7m>");
        error.WriteLine("  Asn1Kit.Cms.Demo sign [--modern] --certificate <signer.cer> --content <data> --output <out.p7m>");
        return 2;
    }
}

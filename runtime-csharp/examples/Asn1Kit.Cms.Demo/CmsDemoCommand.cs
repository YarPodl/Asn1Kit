using Asn1Kit.Cms.Demo.Modern;
using Asn1Kit.Runtime;
using BenchCertificate = Asn1Kit.Pkix.Bench.Certificate;
using ModernCertificate = Asn1Kit.Modern.PKIX1Explicit2009.Certificate;

namespace Asn1Kit.Cms.Demo;

public static class CmsDemoCommand
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
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
                var roots = rootPaths.Count == 0 ? null : rootPaths.Select(LoadBenchCertificate).ToArray();
                var available = certificatePaths.Select(LoadBenchCertificate).ToArray();
                results = CmsSignedDataInspector.Inspect(encoded,
                    roots is null ? new EducationalCryptoVerifier<BenchCertificate>() :
                        new RsaSha256CryptoVerifier<BenchCertificate>(certificate =>
                            certificate.TbsCertificate.Value.SubjectPublicKeyInfo.OriginalEncoding),
                    available, roots);
            }
            var accepted = true;
            foreach (var result in results)
            {
                output.WriteLine($"Signer {result.SignerNumber}: {(result.Accepted ? "accepted" : "rejected")} by {(rootPaths.Count == 0 ? "educational stub" : "RSA/SHA-256 verifier")}: {result.Reason}");
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

    private static Asn1Value<BenchCertificate> LoadBenchCertificate(string path)
    {
        var reader = new Asn1Reader(File.ReadAllBytes(path), Asn1Encoding.Der);
        var certificate = reader.ReadWithOriginalEncoding(BenchCertificate.Decode);
        reader.ThrowIfNotEmpty();
        return certificate;
    }

    private static Asn1Value<ModernCertificate> LoadModernCertificate(string path)
    {
        var reader = new Asn1Reader(File.ReadAllBytes(path), Asn1Encoding.Der);
        var certificate = reader.ReadWithOriginalEncoding(ModernCertificate.Decode);
        reader.ThrowIfNotEmpty();
        return certificate;
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage: Asn1Kit.Cms.Demo [--modern] [--trusted-root <root.cer>]... [--certificate <intermediate.cer>]... <attached-signeddata.p7m>");
        return 2;
    }
}

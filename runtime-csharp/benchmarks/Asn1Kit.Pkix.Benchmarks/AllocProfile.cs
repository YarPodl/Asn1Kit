using System.Globalization;
using Asn1Kit.Cms;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;

namespace Asn1Kit.Benchmarks;

/// <summary>
/// One-shot allocation inventory for Decode KPI fixtures (not a BenchmarkDotNet job).
/// </summary>
internal static class AllocProfile
{
    public static int Run()
    {
        ProfileCertificate();
        Console.WriteLine();
        ProfileCrl();
        Console.WriteLine();
        ProfileCms();
        return 0;
    }

    private static void ProfileCertificate()
    {
        var der = FixtureLoader.ReadPkix("TrustAnchorRootCertificate.crt");
        Warmup(() => Certificate.Decode(new Asn1Reader(der, Asn1Encoding.Der)));

        var allocated = MeasureAllocated(() => Certificate.Decode(new Asn1Reader(der, Asn1Encoding.Der)));
        var cert = Certificate.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        var tbs = cert.TbsCertificate.Value;
        var issuer = CountName(tbs.Issuer.Value);
        var subject = CountName(tbs.Subject.Value);
        var algParams = CountAlgorithmParameters(tbs.Signature)
            + CountAlgorithmParameters(tbs.SubjectPublicKeyInfo.Value.Algorithm)
            + CountAlgorithmParameters(cert.SignatureAlgorithm);

        var avaCount = issuer.AvaCount + subject.AvaCount;
        Console.WriteLine("Certificate Decode inventory (TrustAnchorRootCertificate.crt)");
        Console.WriteLine("  Allocated/op:           " + FormatBytes(allocated));
        Console.WriteLine("  Issuer AVAs:            " + issuer.AvaCount);
        Console.WriteLine("  Subject AVAs:           " + subject.AvaCount);
        Console.WriteLine("  Open-type payloads:     " + avaCount + " x Asn1Any (AVA value kept opaque)");
        Console.WriteLine("  Alg parameter payloads: " + algParams + " x Asn1Any? (optional parameters)");
        Console.WriteLine("  Extensions:             " + (tbs.Extensions?.Value.Length ?? 0));
    }

    private static void ProfileCrl()
    {
        var der = FixtureLoader.ReadPkix("GoodCACRL.crl");
        Warmup(() => CertificateList.Decode(new Asn1Reader(der, Asn1Encoding.Der)));

        var allocated = MeasureAllocated(() => CertificateList.Decode(new Asn1Reader(der, Asn1Encoding.Der)));
        var crl = CertificateList.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        var tbs = crl.TbsCertList;
        var issuer = CountName(tbs.Issuer);
        var algParams = CountAlgorithmParameters(tbs.Signature)
            + CountAlgorithmParameters(crl.SignatureAlgorithm);

        Console.WriteLine("CRL Decode inventory (GoodCACRL.crl)");
        Console.WriteLine("  Allocated/op:           " + FormatBytes(allocated));
        Console.WriteLine("  Issuer AVAs:            " + issuer.AvaCount);
        Console.WriteLine("  Open-type payloads:     " + issuer.AvaCount + " x Asn1Any (AVA value kept opaque)");
        Console.WriteLine("  Alg parameter payloads: " + algParams + " x Asn1Any?");
        Console.WriteLine("  Revoked entries:        " + (tbs.RevokedCertificates?.Length ?? 0));
    }

    private static void ProfileCms()
    {
        var der = FixtureLoader.ReadCms("attached-signeddata.p7m");
        Warmup(() => DecodeSignedDataShell(der));

        var allocated = MeasureAllocated(() => DecodeSignedDataShell(der));
        var sd = DecodeSignedDataShell(der);
        var signerCount = sd.SignerInfos.Length;
        var certChoices = sd.Certificates?.Length ?? 0;
        var avaTotal = 0;
        foreach (var signer in sd.SignerInfos)
        {
            if (signer.Sid.IssuerAndSerialNumber is { } ias)
                avaTotal += CountName(ias.Issuer.Value).AvaCount;
        }

        Console.WriteLine("CMS Lazy Decode inventory (attached-signeddata.p7m)");
        Console.WriteLine("  Allocated/op:           " + FormatBytes(allocated));
        Console.WriteLine("  Path:                   ContentInfo.Decode + TryDecodeContent(SignedData)");
        Console.WriteLine("  SignerInfos:            " + signerCount);
        Console.WriteLine("  CertificateChoices:     " + certChoices + " (lazy cert TLV, not materialized)");
        Console.WriteLine("  Issuer AVAs (SIDs):      " + avaTotal + " (opaque Asn1Any until TryDecode)");
    }

    private static SignedData DecodeSignedDataShell(byte[] der)
    {
        var cms = ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        AssertSignedData(cms, out var signedData);
        return signedData;
    }

    private static void AssertSignedData(ContentInfo cms, out SignedData signedData)
    {
        if (!cms.TryDecodeContent(ContentInfoContentBindings.SignedData, out signedData!))
            throw new InvalidOperationException("Expected SignedData content.");
    }

    private static NameStats CountName(AttributeTypeAndValue[][] rdns)
    {
        var stats = new NameStats();
        foreach (var rdn in rdns)
            stats.AvaCount += rdn.Length;
        return stats;
    }

    private static int CountAlgorithmParameters(AlgorithmIdentifier algorithm) =>
        algorithm.Parameters is null ? 0 : 1;

    private static void Warmup(Action action)
    {
        for (var i = 0; i < 64; i++)
        {
            action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static long MeasureAllocated(Action action)
    {
        // Stabilize and take median of several samples.
        var samples = new long[9];
        for (var i = 0; i < samples.Length; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            samples[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        return (bytes / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " KB ("
            + bytes.ToString(CultureInfo.InvariantCulture) + " B)";
    }

    private struct NameStats
    {
        public int AvaCount;
    }
}

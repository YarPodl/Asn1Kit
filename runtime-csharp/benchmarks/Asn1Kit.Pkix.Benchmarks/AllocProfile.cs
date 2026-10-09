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
        Console.WriteLine("  Issuer AVAs:            " + issuer.AvaCount + " (values: " + FormatKindCounts(issuer.Kinds) + ")");
        Console.WriteLine("  Subject AVAs:           " + subject.AvaCount + " (values: " + FormatKindCounts(subject.Kinds) + ")");
        Console.WriteLine("  Open-type value objs:   " + avaCount + " x AttributeTypeAndValue_Value");
        Console.WriteLine("  Decoded strings:        " + (issuer.StringCount + subject.StringCount)
            + " (" + FormatBytes(issuer.StringBytes + subject.StringBytes) + ")");
        Console.WriteLine("  Alg parameter objs:     " + algParams + " x AlgorithmIdentifier_Parameters");
        Console.WriteLine("  Extensions:             " + (tbs.Extensions?.Value.Length ?? 0));
        Console.WriteLine("  Est. vs Asn1Any era:    +" + FormatBytes(EstimateOpenTypeDelta(avaCount, issuer.StringBytes + subject.StringBytes, algParams))
            + " (wrappers+strings+alg-params; baseline kept opaque ANY)");
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
        Console.WriteLine("  Issuer AVAs:            " + issuer.AvaCount + " (values: " + FormatKindCounts(issuer.Kinds) + ")");
        Console.WriteLine("  Open-type value objs:   " + issuer.AvaCount + " x AttributeTypeAndValue_Value");
        Console.WriteLine("  Decoded strings:        " + issuer.StringCount
            + " (" + FormatBytes(issuer.StringBytes) + ")");
        Console.WriteLine("  Alg parameter objs:     " + algParams + " x AlgorithmIdentifier_Parameters");
        Console.WriteLine("  Revoked entries:        " + (tbs.RevokedCertificates?.Length ?? 0));
        Console.WriteLine("  Est. vs Asn1Any era:    +" + FormatBytes(EstimateOpenTypeDelta(issuer.AvaCount, issuer.StringBytes, algParams))
            + " (wrappers+strings+alg-params; baseline kept opaque ANY)");
    }

    private static void ProfileCms()
    {
        var der = FixtureLoader.ReadCms("attached-signeddata.p7m");
        Warmup(() => ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der)));

        var allocated = MeasureAllocated(() => ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der)));
        var cms = ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        var sd = cms.Content.SignedData!;
        var signerCount = sd.SignerInfos.Length;
        var certChoices = sd.Certificates?.Length ?? 0;
        var avaTotal = 0;
        var stringTotal = 0;
        var stringBytes = 0L;
        var kinds = new Dictionary<AttributeTypeAndValue_ValueKind, int>();
        foreach (var signer in sd.SignerInfos)
        {
            if (signer.Sid.IssuerAndSerialNumber is { } ias)
            {
                var name = CountName(ias.Issuer.Value);
                avaTotal += name.AvaCount;
                stringTotal += name.StringCount;
                stringBytes += name.StringBytes;
                MergeKinds(kinds, name.Kinds);
            }
        }

        Console.WriteLine("CMS Lazy Decode inventory (attached-signeddata.p7m)");
        Console.WriteLine("  Allocated/op:           " + FormatBytes(allocated));
        Console.WriteLine("  SignerInfos:            " + signerCount);
        Console.WriteLine("  CertificateChoices:     " + certChoices + " (lazy cert TLV, not materialized)");
        Console.WriteLine("  Issuer AVAs (SIDs):      " + avaTotal + " (values: " + FormatKindCounts(kinds) + ")");
        Console.WriteLine("  Open-type value objs:   " + avaTotal + " x AttributeTypeAndValue_Value");
        Console.WriteLine("  Decoded strings:        " + stringTotal + " (" + FormatBytes(stringBytes) + ")");
    }

    private static NameStats CountName(AttributeTypeAndValue[][] rdns)
    {
        var stats = new NameStats
        {
            Kinds = new Dictionary<AttributeTypeAndValue_ValueKind, int>(),
        };
        foreach (var rdn in rdns)
        {
            foreach (var ava in rdn)
            {
                stats.AvaCount++;
                var kind = ava.Value.Kind;
                stats.Kinds[kind] = stats.Kinds.TryGetValue(kind, out var n) ? n + 1 : 1;
                if (ava.Value.Value is not null)
                {
                    stats.StringCount++;
                    stats.StringBytes += EstimateStringBytes(ava.Value.Value);
                }
            }
        }

        return stats;
    }

    private static void MergeKinds(
        Dictionary<AttributeTypeAndValue_ValueKind, int> target,
        Dictionary<AttributeTypeAndValue_ValueKind, int> source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = target.TryGetValue(pair.Key, out var n) ? n + pair.Value : pair.Value;
        }
    }

    private static int CountAlgorithmParameters(AlgorithmIdentifier algorithm) =>
        algorithm.Parameters is null ? 0 : 1;

    private static long EstimateOpenTypeDelta(int avaCount, long stringBytes, int algParamCount)
    {
        // x64: AttributeTypeAndValue_Value holds enum + string? + Asn1Any? (nullable struct with two ReadOnlyMemory).
        // Align to 8; measured layout is typically 72-80 B. AlgorithmIdentifier_Parameters ~48-56 B.
        const int avaWrapperBytes = 80;
        const int algParamBytes = 56;
        return ((long)avaCount * avaWrapperBytes) + stringBytes + ((long)algParamCount * algParamBytes);
    }

    private static long EstimateStringBytes(string value)
    {
        // .NET Core string: object header + length + (chars+1)*2, aligned.
        var raw = 16 + 4 + ((value.Length + 1) * 2);
        return (raw + 7) & ~7;
    }

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

    private static string FormatKindCounts(Dictionary<AttributeTypeAndValue_ValueKind, int> kinds)
    {
        if (kinds.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", kinds.OrderByDescending(p => p.Value)
            .Select(p => p.Key + "=" + p.Value.ToString(CultureInfo.InvariantCulture)));
    }

    private struct NameStats
    {
        public int AvaCount;
        public int StringCount;
        public long StringBytes;
        public Dictionary<AttributeTypeAndValue_ValueKind, int> Kinds;
    }
}

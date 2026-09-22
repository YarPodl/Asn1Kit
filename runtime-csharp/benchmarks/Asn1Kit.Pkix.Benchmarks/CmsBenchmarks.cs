using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Org.BouncyCastle.Asn1;
using BenchContentInfo = Asn1Kit.Cms.Bench.ContentInfo;
using EagerContentInfo = Asn1Kit.Cms.ContentInfo;
using BcContentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo;

namespace Asn1Kit.Pkix.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class CmsBenchmarks
{
    private byte[] _der = null!;
    private BenchContentInfo _asn1KitLazy = null!;
    private EagerContentInfo _asn1KitEager = null!;
    private SignedCms _bcl = null!;
    private BcContentInfo _bouncyCastle = null!;
    private Asn1Writer _encodeWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _der = FixtureLoader.ReadCms("attached-signeddata.p7m");
        _asn1KitLazy = BenchContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        _asn1KitEager = EagerContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        _bcl = new SignedCms();
        _bcl.Decode(_der);
        _bouncyCastle = BcContentInfo.GetInstance(Asn1Object.FromByteArray(_der));
        _encodeWriter = new Asn1Writer(Asn1Encoding.Der);

        // Sanity: lazy path keeps cert TLV without materializing.
        var lazyCert = _asn1KitLazy.Content.SignedData!.Certificates!
            .Single(c => c.Certificate is not null)
            .Certificate!;
        if (!lazyCert.HasEncoded || lazyCert.IsMaterialized)
        {
            throw new InvalidOperationException("Bench CMS setup expected lazy certificate TLV before materialize.");
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Decode", "CMS")]
    public BenchContentInfo Asn1Kit_Lazy_Decode() =>
        BenchContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));

    [Benchmark]
    [BenchmarkCategory("Decode", "CMS")]
    public BenchContentInfo Asn1Kit_Lazy_Materialize_Decode()
    {
        var info = BenchContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        MaterializeBench(info);
        return info;
    }

    [Benchmark]
    [BenchmarkCategory("Decode", "CMS")]
    public EagerContentInfo Asn1Kit_Eager_Decode() =>
        EagerContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));

    [Benchmark]
    [BenchmarkCategory("Decode", "CMS")]
    public SignedCms Bcl_Decode()
    {
        var cms = new SignedCms();
        cms.Decode(_der);
        return cms;
    }

    [Benchmark]
    [BenchmarkCategory("Decode", "CMS")]
    public SignedCms Bcl_Materialize_Decode()
    {
        var cms = new SignedCms();
        cms.Decode(_der);
        MaterializeBcl(cms);
        return cms;
    }

    [Benchmark]
    [BenchmarkCategory("Decode", "CMS")]
    public BcContentInfo BouncyCastle_Decode() =>
        BcContentInfo.GetInstance(Asn1Object.FromByteArray(_der));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] Asn1Kit_Lazy_Encode()
    {
        _encodeWriter.Reset();
        _asn1KitLazy.Encode(_encodeWriter);
        return _encodeWriter.Encode();
    }

    [Benchmark]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] Asn1Kit_Eager_Encode()
    {
        _encodeWriter.Reset();
        _asn1KitEager.Encode(_encodeWriter);
        return _encodeWriter.Encode();
    }

    [Benchmark]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] Bcl_Encode() => _bcl.Encode();

    [Benchmark]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] BouncyCastle_Encode() => _bouncyCastle.GetEncoded();

    private static void MaterializeBench(BenchContentInfo info)
    {
        var signed = info.Content.SignedData
            ?? throw new InvalidOperationException("Expected SignedData content.");
        if (signed.Certificates is null)
        {
            return;
        }

        foreach (var choice in signed.Certificates)
        {
            if (choice.Certificate is not null)
            {
                _ = choice.Certificate.Value.TbsCertificate.SerialNumber;
            }
        }

        foreach (var signer in signed.SignerInfos)
        {
            _ = signer.Signature.Length;
        }
    }

    private static void MaterializeBcl(SignedCms cms)
    {
        foreach (X509Certificate2 cert in cms.Certificates)
        {
            _ = cert.Thumbprint;
        }

        _ = cms.SignerInfos.Count;
    }
}

using System.Security.Cryptography.Pkcs;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Org.BouncyCastle.Asn1;
using Asn1KitContentInfo = Asn1Kit.Cms.ContentInfo;
using BcContentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo;

namespace Asn1Kit.Pkix.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class CmsBenchmarks
{
    private byte[] _der = null!;
    private Asn1KitContentInfo _asn1Kit = null!;
    private SignedCms _bcl = null!;
    private BcContentInfo _bouncyCastle = null!;

    [GlobalSetup]
    public void Setup()
    {
        _der = FixtureLoader.ReadCms("attached-signeddata.p7m");
        _asn1Kit = Asn1KitContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        _bcl = new SignedCms();
        _bcl.Decode(_der);
        _bouncyCastle = BcContentInfo.GetInstance(Asn1Object.FromByteArray(_der));
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Decode", "CMS")]
    public Asn1KitContentInfo Asn1Kit_Decode() =>
        Asn1KitContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));

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
    public BcContentInfo BouncyCastle_Decode() =>
        BcContentInfo.GetInstance(Asn1Object.FromByteArray(_der));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] Asn1Kit_Encode()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        _asn1Kit.Encode(writer);
        return writer.Encode();
    }

    [Benchmark]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] Bcl_Encode() => _bcl.Encode();

    [Benchmark]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] BouncyCastle_Encode() => _bouncyCastle.GetEncoded();
}

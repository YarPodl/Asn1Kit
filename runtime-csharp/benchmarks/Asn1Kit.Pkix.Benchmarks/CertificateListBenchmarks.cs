using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Org.BouncyCastle.Asn1;
using BcCertificateList = Org.BouncyCastle.Asn1.X509.CertificateList;

namespace Asn1Kit.Pkix.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class CertificateListBenchmarks
{
    private byte[] _der = null!;
    private CertificateList _asn1Kit = null!;
    private BcCertificateList _bouncyCastle = null!;

    [GlobalSetup]
    public void Setup()
    {
        _der = FixtureLoader.ReadPkix("GoodCACRL.crl");
        _asn1Kit = CertificateList.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        _bouncyCastle = BcCertificateList.GetInstance(Asn1Object.FromByteArray(_der));
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Decode", "CRL")]
    public CertificateList Asn1Kit_Decode() =>
        CertificateList.Decode(new Asn1Reader(_der, Asn1Encoding.Der));

    [Benchmark]
    [BenchmarkCategory("Decode", "CRL")]
    public BcCertificateList BouncyCastle_Decode() =>
        BcCertificateList.GetInstance(Asn1Object.FromByteArray(_der));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Encode", "CRL")]
    public byte[] Asn1Kit_Encode()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        _asn1Kit.Encode(writer);
        return writer.Encode();
    }

    [Benchmark]
    [BenchmarkCategory("Encode", "CRL")]
    public byte[] BouncyCastle_Encode() => _bouncyCastle.GetEncoded();
}

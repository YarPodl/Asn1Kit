using Asn1Kit.EncodeBench;
using Asn1Kit.Pkix.Bench;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Org.BouncyCastle.Asn1;
using BcCertificateList = Org.BouncyCastle.Asn1.X509.CertificateList;

namespace Asn1Kit.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class CertificateListBenchmarks
{
    private byte[] _der = null!;
    private CertificateList _asn1KitEncode = null!;
    private BcCertificateList _bouncyCastleEncode = null!;
    private Asn1Writer _encodeWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _der = FixtureLoader.ReadPkix("GoodCACRL.crl");
        _asn1KitEncode = EncodeSamples.CertificateListFromFixture(_der);
        _bouncyCastleEncode = EncodeSamples.BouncyCastleCertificateListFromFixture(_der);
        _encodeWriter = new Asn1Writer(Asn1Encoding.Der);
        _encodeWriter.EnsureCapacity(_der.Length);
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
        _encodeWriter.Reset();
        _asn1KitEncode.Encode(_encodeWriter);
        return _encodeWriter.Encode(static encoded => encoded.ToArray());
    }

    [Benchmark]
    [BenchmarkCategory("Encode", "CRL")]
    public byte[] BouncyCastle_Encode() => _bouncyCastleEncode.GetEncoded();
}

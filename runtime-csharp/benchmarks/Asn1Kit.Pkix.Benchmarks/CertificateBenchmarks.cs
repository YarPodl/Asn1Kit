using Asn1Kit.EncodeBench;
using System.Security.Cryptography.X509Certificates;
using Asn1Kit.Pkix.Bench;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;

namespace Asn1Kit.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class CertificateBenchmarks
{
    private byte[] _der = null!;
    private Certificate _asn1KitEncode = null!;
    private X509CertificateStructure _bouncyCastleEncode = null!;
    private Asn1Writer _encodeWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _der = FixtureLoader.ReadPkix("TrustAnchorRootCertificate.crt");
        _asn1KitEncode = EncodeSamples.CertificateFromFixture(_der);
        _bouncyCastleEncode = EncodeSamples.BouncyCastleCertificateFromFixture(_der);
        _encodeWriter = new Asn1Writer(Asn1Encoding.Der);
        _encodeWriter.EnsureCapacity(_der.Length);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Decode", "Certificate")]
    public Certificate Asn1Kit_Decode() =>
        Certificate.Decode(new Asn1Reader(_der, Asn1Encoding.Der));

    [Benchmark]
    [BenchmarkCategory("Decode", "Certificate")]
    public X509Certificate2 Bcl_Decode() => new X509Certificate2(_der);

    [Benchmark]
    [BenchmarkCategory("Decode", "Certificate")]
    public X509CertificateStructure BouncyCastle_Decode() =>
        X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(_der));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Encode", "Certificate")]
    public byte[] Asn1Kit_Encode()
    {
        _encodeWriter.Reset();
        _asn1KitEncode.Encode(_encodeWriter);
        return _encodeWriter.Encode(static encoded => encoded.ToArray());
    }

    [Benchmark]
    [BenchmarkCategory("Encode", "Certificate")]
    public byte[] BouncyCastle_Encode() => _bouncyCastleEncode.GetEncoded();
}

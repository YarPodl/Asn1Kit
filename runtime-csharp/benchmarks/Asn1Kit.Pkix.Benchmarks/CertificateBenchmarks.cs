using System.Security.Cryptography.X509Certificates;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;

namespace Asn1Kit.Pkix.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class CertificateBenchmarks
{
    private byte[] _der = null!;
    private Certificate _asn1Kit = null!;
    private X509CertificateStructure _bouncyCastle = null!;
    private byte[] _bclRaw = null!;

    [GlobalSetup]
    public void Setup()
    {
        _der = FixtureLoader.ReadPkix("TrustAnchorRootCertificate.crt");
        _asn1Kit = Certificate.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        _bouncyCastle = X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(_der));
        using var bcl = new X509Certificate2(_der);
        _bclRaw = bcl.RawData;
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
        var writer = new Asn1Writer(Asn1Encoding.Der);
        _asn1Kit.Encode(writer);
        return writer.Encode();
    }

    /// <summary>BCL has no structural re-encode of Certificate; RawData is an identity copy of the input DER.</summary>
    [Benchmark]
    [BenchmarkCategory("Encode", "Certificate")]
    public byte[] Bcl_Encode_RawDataCopy() => (byte[])_bclRaw.Clone();

    [Benchmark]
    [BenchmarkCategory("Encode", "Certificate")]
    public byte[] BouncyCastle_Encode() => _bouncyCastle.GetEncoded();
}

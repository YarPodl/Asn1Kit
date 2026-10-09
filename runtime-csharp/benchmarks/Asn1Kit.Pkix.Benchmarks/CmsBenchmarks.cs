using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Asn1Kit.Cms;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Org.BouncyCastle.Asn1;
using CmsContentInfo = Asn1Kit.Cms.ContentInfo;
using BcContentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo;

namespace Asn1Kit.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class CmsBenchmarks
{
    private byte[] _der = null!;
    private CmsContentInfo _asn1KitContentInfo = null!;
    private SignedData _asn1KitSignedData = null!;
    private SignedCms _bcl = null!;
    private BcContentInfo _bouncyCastle = null!;
    private Asn1Writer _encodeWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _der = FixtureLoader.ReadCms("attached-signeddata.p7m");
        _asn1KitContentInfo = CmsContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        if (!_asn1KitContentInfo.TryDecodeContent(ContentInfoContentBindings.SignedData, out _asn1KitSignedData!))
        {
            throw new InvalidOperationException("CMS setup expected SignedData content.");
        }

        _bcl = new SignedCms();
        _bcl.Decode(_der);
        _bouncyCastle = BcContentInfo.GetInstance(Asn1Object.FromByteArray(_der));
        _encodeWriter = new Asn1Writer(Asn1Encoding.Der);
        _encodeWriter.EnsureCapacity(_der.Length);

        // Sanity: lazy path keeps cert TLV without materializing.
        var lazyCert = _asn1KitSignedData.Certificates!
            .Single(c => c.Certificate is not null)
            .Certificate!;
        if (!lazyCert.HasEncoded || lazyCert.IsMaterialized)
        {
            throw new InvalidOperationException("CMS setup expected lazy certificate TLV before materialize.");
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Decode", "CMS")]
    public SignedData Asn1Kit_Lazy_Decode()
    {
        // Fair peer to BCL SignedCms / BC ContentInfo: envelope + SignedData shell.
        // CertificateChoices.certificate stays lazy (no .Value).
        var info = CmsContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        if (!info.TryDecodeContent(ContentInfoContentBindings.SignedData, out var signedData))
        {
            throw new InvalidOperationException("Expected SignedData content.");
        }

        return signedData;
    }

    [Benchmark]
    [BenchmarkCategory("Decode", "CMS")]
    public CmsContentInfo Asn1Kit_Lazy_Materialize_Decode()
    {
        var info = CmsContentInfo.Decode(new Asn1Reader(_der, Asn1Encoding.Der));
        MaterializeAsn1Kit(info);
        return info;
    }

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
        // Re-bind SignedData each op so Encode pays structural cost (not opaque WriteAny of retained content).
        _encodeWriter.Reset();
        _asn1KitContentInfo.SetContent(ContentInfoContentBindings.SignedData, _asn1KitSignedData);
        _asn1KitContentInfo.Encode(_encodeWriter);
        return _encodeWriter.Encode(static encoded => encoded.ToArray());
    }

    [Benchmark]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] Bcl_Encode() => _bcl.Encode();

    [Benchmark]
    [BenchmarkCategory("Encode", "CMS")]
    public byte[] BouncyCastle_Encode() => _bouncyCastle.GetEncoded();

    private static void MaterializeAsn1Kit(CmsContentInfo info)
    {
        if (!info.TryDecodeContent(ContentInfoContentBindings.SignedData, out var signed))
        {
            throw new InvalidOperationException("Expected SignedData content.");
        }

        if (signed.Certificates is null)
        {
            return;
        }

        foreach (var choice in signed.Certificates)
        {
            if (choice.Certificate is not null)
            {
                _ = choice.Certificate.Value.TbsCertificate.Value.SerialNumber;
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

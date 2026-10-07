using Asn1Kit.EncodeBench;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using Org.BouncyCastle.Asn1;
using CmsContentInfo = Asn1Kit.Cms.ContentInfo;
using BcContentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo;
using BcCertificateList = Org.BouncyCastle.Asn1.X509.CertificateList;
using BcCertificateStructure = Org.BouncyCastle.Asn1.X509.X509CertificateStructure;

namespace Asn1Kit.Benchmarks;

internal static class Smoke
{
    public static void VerifyFixtures()
    {
        var certDer = FixtureLoader.ReadPkix("TrustAnchorRootCertificate.crt");
        var crlDer = FixtureLoader.ReadPkix("GoodCACRL.crl");
        var cmsDer = FixtureLoader.ReadCms("attached-signeddata.p7m");

        _ = Certificate.Decode(new Asn1Reader(certDer, Asn1Encoding.Der));
        var certEncode = EncodeSamples.CreateCertificate();
        var certWriter = new Asn1Writer(Asn1Encoding.Der);
        certEncode.Encode(certWriter);
        if (certWriter.EncodedLength == 0)
        {
            throw new InvalidOperationException("Hand-built Certificate encode produced empty output.");
        }

        using (var bcl = new X509Certificate2(certDer))
        {
            _ = bcl.Thumbprint;
        }

        _ = BcCertificateStructure.GetInstance(Asn1Object.FromByteArray(certDer));
        _ = EncodeSamples.CreateBouncyCastleCertificate().GetEncoded();

        _ = CertificateList.Decode(new Asn1Reader(crlDer, Asn1Encoding.Der));
        var crlEncode = EncodeSamples.CreateCertificateList();
        var crlWriter = new Asn1Writer(Asn1Encoding.Der);
        crlEncode.Encode(crlWriter);
        if (crlWriter.EncodedLength == 0)
        {
            throw new InvalidOperationException("Hand-built CertificateList encode produced empty output.");
        }

        _ = BcCertificateList.GetInstance(Asn1Object.FromByteArray(crlDer));
        _ = EncodeSamples.CreateBouncyCastleCertificateList().GetEncoded();

        var cms = CmsContentInfo.Decode(new Asn1Reader(cmsDer, Asn1Encoding.Der));
        var lazyCert = cms.Content.SignedData!.Certificates!
            .Single(c => c.Certificate is not null)
            .Certificate!;
        if (!lazyCert.HasEncoded)
        {
            throw new InvalidOperationException("CMS decode did not retain certificate TLV.");
        }

        if (lazyCert.IsMaterialized)
        {
            throw new InvalidOperationException("CMS decode materialized certificate unexpectedly.");
        }

        var lazyWriter = new Asn1Writer(Asn1Encoding.Der);
        cms.Encode(lazyWriter);
        var lazyEncoded = lazyWriter.Encode();
        if (lazyEncoded.Length == 0)
        {
            throw new InvalidOperationException("CMS lazy encode produced empty output.");
        }

        // Touch .Value once — encode must still succeed (HasEncoded path).
        _ = lazyCert.Value.TbsCertificate.Value.SerialNumber;
        if (!lazyCert.IsMaterialized)
        {
            throw new InvalidOperationException("Expected certificate materialization after .Value.");
        }

        var signedCms = new SignedCms();
        signedCms.Decode(cmsDer);
        _ = signedCms.Encode();
        _ = signedCms.Certificates.Count;
        _ = BcContentInfo.GetInstance(Asn1Object.FromByteArray(cmsDer));

        Console.WriteLine(
            "Smoke OK: Certificate, CRL, CMS (lazy cert + materialize) under Asn1Kit / BCL / BouncyCastle.");
    }
}

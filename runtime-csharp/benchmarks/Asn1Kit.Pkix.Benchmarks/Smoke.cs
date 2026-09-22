using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Asn1KitContentInfo = Asn1Kit.Cms.ContentInfo;
using BcContentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo;
using BcCertificateList = Org.BouncyCastle.Asn1.X509.CertificateList;

namespace Asn1Kit.Pkix.Benchmarks;

internal static class Smoke
{
    public static void VerifyFixtures()
    {
        var certDer = FixtureLoader.ReadPkix("TrustAnchorRootCertificate.crt");
        var crlDer = FixtureLoader.ReadPkix("GoodCACRL.crl");
        var cmsDer = FixtureLoader.ReadCms("attached-signeddata.p7m");

        _ = Certificate.Decode(new Asn1Reader(certDer, Asn1Encoding.Der));
        using (var bcl = new X509Certificate2(certDer))
        {
            _ = bcl.Thumbprint;
        }

        _ = X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(certDer));

        _ = CertificateList.Decode(new Asn1Reader(crlDer, Asn1Encoding.Der));
        _ = BcCertificateList.GetInstance(Asn1Object.FromByteArray(crlDer));

        _ = Asn1KitContentInfo.Decode(new Asn1Reader(cmsDer, Asn1Encoding.Der));
        var signedCms = new SignedCms();
        signedCms.Decode(cmsDer);
        _ = signedCms.Encode();
        _ = BcContentInfo.GetInstance(Asn1Object.FromByteArray(cmsDer));

        Console.WriteLine("Smoke OK: Certificate, CRL, CMS fixtures decode under Asn1Kit / BCL / BouncyCastle.");
    }
}

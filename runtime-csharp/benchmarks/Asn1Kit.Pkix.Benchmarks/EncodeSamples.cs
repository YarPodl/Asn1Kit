using System.Numerics;
using System.Text;
using Asn1Kit.Runtime;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using BcAlgorithmIdentifier = Org.BouncyCastle.Asn1.X509.AlgorithmIdentifier;
using BcAuthorityKeyIdentifier = Org.BouncyCastle.Asn1.X509.AuthorityKeyIdentifier;
using BcBasicConstraints = Org.BouncyCastle.Asn1.X509.BasicConstraints;
using BcCertificateList = Org.BouncyCastle.Asn1.X509.CertificateList;
using BcKeyUsage = Org.BouncyCastle.Asn1.X509.KeyUsage;
using BcSubjectKeyIdentifier = Org.BouncyCastle.Asn1.X509.SubjectKeyIdentifier;
using BcSubjectPublicKeyInfo = Org.BouncyCastle.Asn1.X509.SubjectPublicKeyInfo;
using BcTime = Org.BouncyCastle.Asn1.X509.Time;
using Certificate = global::Asn1Kit.Pkix.Bench.Certificate;
using CertificateList = global::Asn1Kit.Pkix.Bench.CertificateList;
using TBSCertificate = global::Asn1Kit.Pkix.Bench.TBSCertificate;
using TBSCertList = global::Asn1Kit.Pkix.Bench.TBSCertList;
using TBSCertList_RevokedCertificates_Item = global::Asn1Kit.Pkix.Bench.TBSCertList_RevokedCertificates_Item;
using AlgorithmIdentifier = global::Asn1Kit.Pkix.Bench.AlgorithmIdentifier;
using AlgorithmIdentifier_Parameters = global::Asn1Kit.Pkix.Bench.AlgorithmIdentifier_Parameters;
using SubjectPublicKeyInfo = global::Asn1Kit.Pkix.Bench.SubjectPublicKeyInfo;
using AttributeTypeAndValue = global::Asn1Kit.Pkix.Bench.AttributeTypeAndValue;
using Extension = global::Asn1Kit.Pkix.Bench.Extension;
using Validity = global::Asn1Kit.Pkix.Bench.Validity;
using Time = global::Asn1Kit.Pkix.Bench.Time;
using PkixVersion = global::Asn1Kit.Pkix.Bench.Version;
using KitAuthorityKeyIdentifier = global::Asn1Kit.Pkix.Bench.AuthorityKeyIdentifier;
using KitBasicConstraints = global::Asn1Kit.Pkix.Bench.BasicConstraints;
using KitKeyUsage = global::Asn1Kit.Pkix.Bench.KeyUsage;
using KeyUsageFlags = global::Asn1Kit.Pkix.Bench.KeyUsageFlags;
using CRLReason = global::Asn1Kit.Pkix.Bench.CRLReason;

namespace Asn1Kit.EncodeBench;

/// <summary>
/// Hand-built encode-bench object graphs (PKITS Trust Anchor / GoodCACRL scale).
/// No Decode: values are C# object initializers so retainEncoded cannot WriteRaw fixture TLV.
/// </summary>
internal static class EncodeSamples
{
    private static readonly Asn1Oid OidCountryName = Asn1Oid.Parse("2.5.4.6");
    private static readonly Asn1Oid OidOrganizationName = Asn1Oid.Parse("2.5.4.10");
    private static readonly Asn1Oid OidCommonName = Asn1Oid.Parse("2.5.4.3");
    private static readonly Asn1Oid OidSha256WithRsa = Asn1Oid.Parse("1.2.840.113549.1.1.11");
    private static readonly Asn1Oid OidRsaEncryption = Asn1Oid.Parse("1.2.840.113549.1.1.1");
    private static readonly Asn1Oid OidSubjectKeyIdentifier = Asn1Oid.Parse("2.5.29.14");
    private static readonly Asn1Oid OidKeyUsage = Asn1Oid.Parse("2.5.29.15");
    private static readonly Asn1Oid OidBasicConstraints = Asn1Oid.Parse("2.5.29.19");
    private static readonly Asn1Oid OidAuthorityKeyIdentifier = Asn1Oid.Parse("2.5.29.35");
    private static readonly Asn1Oid OidCrlNumber = Asn1Oid.Parse("2.5.29.20");
    private static readonly Asn1Oid OidCrlReason = Asn1Oid.Parse("2.5.29.21");

    private static readonly DateTimeOffset NotBefore = new(2010, 1, 1, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NotAfter = new(2030, 12, 31, 8, 30, 0, TimeSpan.Zero);

    private static readonly byte[] SubjectKeyId = Filled(20, 0xE4);
    private static readonly byte[] AuthorityKeyId = Filled(20, 0x58);
    private static readonly byte[] SubjectPublicKeyBytes = Filled(270, 0x30);
    private static readonly byte[] SignatureBytes = Filled(256, 0xC5);

    public static Certificate CreateCertificate()
    {
        var name = TrustAnchorName();
        var signatureAlgorithm = Sha256WithRsa();
        return new Certificate
        {
            TbsCertificate = new TBSCertificate
            {
                Version = PkixVersion.V3,
                SerialNumber = Asn1Integer.FromInt32(1),
                Signature = signatureAlgorithm,
                Issuer = Asn1Retained<AttributeTypeAndValue[][]>.FromValue(name),
                Validity = new Validity
                {
                    NotBefore = Time.FromUtcTime(NotBefore),
                    NotAfter = Time.FromUtcTime(NotAfter),
                },
                Subject = Asn1Retained<AttributeTypeAndValue[][]>.FromValue(CloneName(name)),
                SubjectPublicKeyInfo = Asn1Retained<SubjectPublicKeyInfo>.FromValue(CreateSpki()),
                Extensions = Asn1Retained<Extension[]>.FromValue(new Extension[]
                {
                    Ext(OidSubjectKeyIdentifier, critical: false, EncodeSki(SubjectKeyId)),
                    Ext(OidKeyUsage, critical: true, EncodeKeyUsage(KeyUsageFlags.KeyCertSign | KeyUsageFlags.CRLSign)),
                    Ext(OidBasicConstraints, critical: true, EncodeBasicConstraintsCa()),
                }),
            },
            SignatureAlgorithm = Sha256WithRsa(),
            Signature = Asn1BitString.CopyFrom(SignatureBytes, unusedBits: 0),
        };
    }

    public static CertificateList CreateCertificateList()
    {
        var signatureAlgorithm = Sha256WithRsa();
        return new CertificateList
        {
            TbsCertList = new TBSCertList
            {
                Version = PkixVersion.V2,
                Signature = signatureAlgorithm,
                Issuer = GoodCaName(),
                ThisUpdate = Time.FromUtcTime(NotBefore),
                NextUpdate = Time.FromUtcTime(NotAfter),
                RevokedCertificates = new[]
                {
                    Revoked(0x0E, NotBefore),
                    Revoked(0x0F, NotBefore),
                },
                CrlExtensions = new[]
                {
                    Ext(OidAuthorityKeyIdentifier, critical: false, EncodeAki(AuthorityKeyId)),
                    Ext(OidCrlNumber, critical: false, EncodeCrlNumber(1)),
                },
            },
            SignatureAlgorithm = Sha256WithRsa(),
            Signature = Asn1BitString.CopyFrom(SignatureBytes, unusedBits: 0),
        };
    }

    public static X509CertificateStructure CreateBouncyCastleCertificate()
    {
        var name = BcName("Trust Anchor");
        var signatureAlgorithm = BcSha256WithRsa();
        var extensions = new X509ExtensionsGenerator();
        extensions.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new BcSubjectKeyIdentifier(SubjectKeyId));
        extensions.AddExtension(X509Extensions.KeyUsage, true, new BcKeyUsage(BcKeyUsage.KeyCertSign | BcKeyUsage.CrlSign));
        extensions.AddExtension(X509Extensions.BasicConstraints, true, new BcBasicConstraints(cA: true));

        var tbsGen = new V3TbsCertificateGenerator();
        tbsGen.SetSerialNumber(new DerInteger(1));
        tbsGen.SetSignature(signatureAlgorithm);
        tbsGen.SetIssuer(name);
        tbsGen.SetStartDate(new BcTime(NotBefore.UtcDateTime));
        tbsGen.SetEndDate(new BcTime(NotAfter.UtcDateTime));
        tbsGen.SetSubject(name);
        tbsGen.SetSubjectPublicKeyInfo(new BcSubjectPublicKeyInfo(BcRsaEncryption(), SubjectPublicKeyBytes));
        tbsGen.SetExtensions(extensions.Generate());

        return new X509CertificateStructure(
            tbsGen.GenerateTbsCertificate(),
            signatureAlgorithm,
            new DerBitString(SignatureBytes));
    }

    public static BcCertificateList CreateBouncyCastleCertificateList()
    {
        var signatureAlgorithm = BcSha256WithRsa();
        var reasonExtensions = new X509ExtensionsGenerator();
        reasonExtensions.AddExtension(X509Extensions.ReasonCode, false, new CrlReason(CrlReason.KeyCompromise));
        var entryExtensions = reasonExtensions.Generate();

        var tbsGen = new V2TbsCertListGenerator();
        tbsGen.SetSignature(signatureAlgorithm);
        tbsGen.SetIssuer(BcName("Good CA"));
        tbsGen.SetThisUpdate(new BcTime(NotBefore.UtcDateTime));
        tbsGen.SetNextUpdate(new BcTime(NotAfter.UtcDateTime));
        tbsGen.AddCrlEntry(new DerInteger(0x0E), new BcTime(NotBefore.UtcDateTime), entryExtensions);
        tbsGen.AddCrlEntry(new DerInteger(0x0F), new BcTime(NotBefore.UtcDateTime), entryExtensions);

        var crlExtensions = new X509ExtensionsGenerator();
        crlExtensions.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new BcAuthorityKeyIdentifier(AuthorityKeyId));
        crlExtensions.AddExtension(X509Extensions.CrlNumber, false, new CrlNumber(Org.BouncyCastle.Math.BigInteger.One));
        tbsGen.SetExtensions(crlExtensions.Generate());

        return BcCertificateList.GetInstance(new DerSequence(
            tbsGen.GenerateTbsCertList(),
            signatureAlgorithm,
            new DerBitString(SignatureBytes)));
    }

    private static AttributeTypeAndValue[][] TrustAnchorName() => Name(
        ("US", OidCountryName),
        ("Test Certificates 2011", OidOrganizationName),
        ("Trust Anchor", OidCommonName));

    private static AttributeTypeAndValue[][] GoodCaName() => Name(
        ("US", OidCountryName),
        ("Test Certificates 2011", OidOrganizationName),
        ("Good CA", OidCommonName));

    private static AttributeTypeAndValue[][] Name(params (string Value, Asn1Oid Type)[] rdns)
    {
        var result = new AttributeTypeAndValue[rdns.Length][];
        for (var i = 0; i < rdns.Length; i++)
        {
            var (value, type) = rdns[i];
            result[i] = new[]
            {
                new AttributeTypeAndValue
                {
                    Type = type.Clone(),
                    Value = PrintableStringAny(value),
                },
            };
        }

        return result;
    }

    private static AttributeTypeAndValue[][] CloneName(AttributeTypeAndValue[][] src)
    {
        var result = new AttributeTypeAndValue[src.Length][];
        for (var i = 0; i < src.Length; i++)
        {
            var rdn = src[i];
            var copy = new AttributeTypeAndValue[rdn.Length];
            for (var j = 0; j < rdn.Length; j++)
            {
                var atv = rdn[j];
                copy[j] = new AttributeTypeAndValue
                {
                    Type = atv.Type.Clone(),
                    Value = Asn1Any.CopyFrom(atv.Value.EncodedMemory.Span),
                };
            }

            result[i] = copy;
        }

        return result;
    }

    private static Asn1Any PrintableStringAny(string value) =>
        Asn1Any.FromTagAndContents(Asn1Tag.PrintableString, Encoding.ASCII.GetBytes(value));

    private static AlgorithmIdentifier Sha256WithRsa() => new()
    {
        Algorithm = OidSha256WithRsa.Clone(),
        Parameters = AlgorithmIdentifier_Parameters.FromNull(),
    };

    private static SubjectPublicKeyInfo CreateSpki() => new()
    {
        Algorithm = new AlgorithmIdentifier
        {
            Algorithm = OidRsaEncryption.Clone(),
            Parameters = AlgorithmIdentifier_Parameters.FromNull(),
        },
        SubjectPublicKey = Asn1BitString.CopyFrom(SubjectPublicKeyBytes, unusedBits: 0),
    };

    private static TBSCertList_RevokedCertificates_Item Revoked(int serial, DateTimeOffset when) => new()
    {
        UserCertificate = Asn1Integer.FromInt32(serial),
        RevocationDate = Time.FromUtcTime(when),
        CrlEntryExtensions = new[]
        {
            Ext(OidCrlReason, critical: false, EncodeCrlReason(CRLReason.KeyCompromise)),
        },
    };

    private static Extension Ext(Asn1Oid oid, bool critical, byte[] extnValue) => new()
    {
        ExtnID = oid.Clone(),
        Critical = critical,
        ExtnValue = extnValue,
    };

    private static byte[] EncodeSki(byte[] keyId) =>
        EncodeToBytes(w => w.WriteOctetString(Asn1Tag.OctetString, keyId));

    private static byte[] EncodeKeyUsage(KeyUsageFlags flags)
    {
        var usage = new KitKeyUsage { Flags = flags };
        return EncodeToBytes(usage.Encode);
    }

    private static byte[] EncodeBasicConstraintsCa()
    {
        var bc = new KitBasicConstraints { CA = true };
        return EncodeToBytes(bc.Encode);
    }

    private static byte[] EncodeAki(byte[] keyId)
    {
        var aki = new KitAuthorityKeyIdentifier { KeyIdentifier = keyId };
        return EncodeToBytes(aki.Encode);
    }

    private static byte[] EncodeCrlNumber(int number) =>
        EncodeToBytes(w => w.WriteInteger(Asn1Tag.Integer, number));

    private static byte[] EncodeCrlReason(CRLReason reason) =>
        EncodeToBytes(w => w.WriteEnumerated(Asn1Tag.Enumerated, new BigInteger((int)reason)));

    private static byte[] EncodeToBytes(Action<Asn1Writer> encode)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        encode(writer);
        return writer.Encode();
    }

    private static X509Name BcName(string commonName) =>
        new(
            new List<DerObjectIdentifier> { X509Name.C, X509Name.O, X509Name.CN },
            new List<string> { "US", "Test Certificates 2011", commonName });

    private static BcAlgorithmIdentifier BcSha256WithRsa() =>
        new(PkcsObjectIdentifiers.Sha256WithRsaEncryption, DerNull.Instance);

    private static BcAlgorithmIdentifier BcRsaEncryption() =>
        new(PkcsObjectIdentifiers.RsaEncryption, DerNull.Instance);

    private static byte[] Filled(int length, byte seed)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(seed + i);
        }

        return bytes;
    }
}

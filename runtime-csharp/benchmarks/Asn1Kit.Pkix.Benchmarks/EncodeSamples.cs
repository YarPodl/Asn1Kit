using Asn1Kit.Runtime;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using BcCertificateList = Org.BouncyCastle.Asn1.X509.CertificateList;
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
using TimeKind = global::Asn1Kit.Pkix.Bench.TimeKind;

namespace Asn1Kit.EncodeBench;

/// <summary>
/// Builds encode-bench samples as fresh object graphs (values taken from a decoded fixture).
/// The encode path must not reuse the Decode result so retainEncoded / view aliases cannot
/// turn structural encode into WriteRaw of the input DER.
/// </summary>
internal static class EncodeSamples
{
    public static Certificate CertificateFromFixture(byte[] der)
    {
        var decoded = Certificate.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        return FreshCertificate(decoded);
    }

    public static CertificateList CertificateListFromFixture(byte[] der)
    {
        var decoded = CertificateList.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        return FreshCertificateList(decoded);
    }

    public static X509CertificateStructure BouncyCastleCertificateFromFixture(byte[] der)
    {
        var decoded = X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(der));
        return new X509CertificateStructure(
            TbsCertificateStructure.GetInstance(Asn1Object.FromByteArray(decoded.TbsCertificate.GetDerEncoded())),
            Org.BouncyCastle.Asn1.X509.AlgorithmIdentifier.GetInstance(
                Asn1Object.FromByteArray(decoded.SignatureAlgorithm.GetDerEncoded())),
            new DerBitString(decoded.Signature.GetBytes(), decoded.Signature.PadBits));
    }

    public static BcCertificateList BouncyCastleCertificateListFromFixture(byte[] der)
    {
        var decoded = BcCertificateList.GetInstance(Asn1Object.FromByteArray(der));
        return BcCertificateList.GetInstance(Asn1Object.FromByteArray(decoded.GetDerEncoded()));
    }

    private static Certificate FreshCertificate(Certificate src) => new()
    {
        TbsCertificate = FreshTbs(src.TbsCertificate),
        SignatureAlgorithm = FreshAlgorithm(src.SignatureAlgorithm),
        Signature = CloneBitString(src.Signature),
    };

    private static CertificateList FreshCertificateList(CertificateList src) => new()
    {
        TbsCertList = FreshTbsCertList(src.TbsCertList),
        SignatureAlgorithm = FreshAlgorithm(src.SignatureAlgorithm),
        Signature = CloneBitString(src.Signature),
    };

    private static TBSCertificate FreshTbs(TBSCertificate src)
    {
        Asn1Retained<List<List<AttributeTypeAndValue>>> retainedIssuer = src.Issuer;
        Asn1Retained<List<List<AttributeTypeAndValue>>> retainedSubject = src.Subject;
        Asn1Retained<SubjectPublicKeyInfo> retainedSpki = src.SubjectPublicKeyInfo;
        Asn1Retained<List<Extension>>? retainedExtensions = src.Extensions;

        return new TBSCertificate
        {
            Version = src.Version,
            SerialNumber = CloneInteger(src.SerialNumber),
            Signature = FreshAlgorithm(src.Signature),
            Issuer = Asn1Retained<List<List<AttributeTypeAndValue>>>.FromValue(FreshName(retainedIssuer.Value)),
            Validity = FreshValidity(src.Validity),
            Subject = Asn1Retained<List<List<AttributeTypeAndValue>>>.FromValue(FreshName(retainedSubject.Value)),
            SubjectPublicKeyInfo = Asn1Retained<SubjectPublicKeyInfo>.FromValue(FreshSpki(retainedSpki.Value)),
            IssuerUniqueID = src.IssuerUniqueID is { } iuid ? CloneBitString(iuid) : null,
            SubjectUniqueID = src.SubjectUniqueID is { } suid ? CloneBitString(suid) : null,
            Extensions = retainedExtensions is null
                ? null
                : Asn1Retained<List<Extension>>.FromValue(FreshExtensions(retainedExtensions.Value)),
        };
    }

    private static TBSCertList FreshTbsCertList(TBSCertList src) => new()
    {
        Version = src.Version,
        Signature = FreshAlgorithm(src.Signature),
        Issuer = FreshName(src.Issuer),
        ThisUpdate = FreshTime(src.ThisUpdate),
        NextUpdate = src.NextUpdate is null ? null : FreshTime(src.NextUpdate),
        RevokedCertificates = src.RevokedCertificates is null
            ? null
            : src.RevokedCertificates.Select(FreshRevoked).ToList(),
        CrlExtensions = src.CrlExtensions is null ? null : FreshExtensions(src.CrlExtensions),
    };

    private static TBSCertList_RevokedCertificates_Item FreshRevoked(TBSCertList_RevokedCertificates_Item src) => new()
    {
        UserCertificate = CloneInteger(src.UserCertificate),
        RevocationDate = FreshTime(src.RevocationDate),
        CrlEntryExtensions = src.CrlEntryExtensions is null ? null : FreshExtensions(src.CrlEntryExtensions),
    };

    private static AlgorithmIdentifier FreshAlgorithm(AlgorithmIdentifier src) => new()
    {
        Algorithm = src.Algorithm.Clone(),
        Parameters = src.Parameters is null ? null : FreshParameters(src.Parameters),
    };

    private static AlgorithmIdentifier_Parameters FreshParameters(AlgorithmIdentifier_Parameters src)
    {
        if (src.Null != null)
        {
            return AlgorithmIdentifier_Parameters.FromNull();
        }

        if (src.Unknown != null)
        {
            return AlgorithmIdentifier_Parameters.FromUnknown(src.Unknown.Value.Clone());
        }

        throw new InvalidOperationException("AlgorithmIdentifier parameters has no alternative.");
    }

    private static SubjectPublicKeyInfo FreshSpki(SubjectPublicKeyInfo src) => new()
    {
        Algorithm = FreshAlgorithm(src.Algorithm),
        SubjectPublicKey = CloneBitString(src.SubjectPublicKey),
    };

    private static Validity FreshValidity(Validity src) => new()
    {
        NotBefore = FreshTime(src.NotBefore),
        NotAfter = FreshTime(src.NotAfter),
    };

    private static Time FreshTime(Time src) => src.Kind switch
    {
        TimeKind.UtcTime => Time.FromUtcTime(src.Value),
        TimeKind.GeneralTime => Time.FromGeneralTime(src.Value),
        _ => throw new InvalidOperationException($"Unknown TimeKind '{src.Kind}'."),
    };

    private static List<List<AttributeTypeAndValue>> FreshName(List<List<AttributeTypeAndValue>> src) =>
        src.Select(rdn => rdn.Select(FreshAtv).ToList()).ToList();

    private static AttributeTypeAndValue FreshAtv(AttributeTypeAndValue src) => new()
    {
        Type = src.Type.Clone(),
        Value = src.Value.Clone(),
    };

    private static List<Extension> FreshExtensions(List<Extension> src) =>
        src.Select(FreshExtension).ToList();

    private static Extension FreshExtension(Extension src) => new()
    {
        ExtnID = src.ExtnID.Clone(),
        Critical = src.Critical,
        ExtnValue = src.ExtnValue.ToArray(),
    };

    private static Asn1Integer CloneInteger(Asn1Integer value) =>
        Asn1Integer.CopyFrom(value.Span);

    private static Asn1BitString CloneBitString(Asn1BitString value) =>
        Asn1BitString.CopyFrom(value.Span, value.UnusedBits);
}

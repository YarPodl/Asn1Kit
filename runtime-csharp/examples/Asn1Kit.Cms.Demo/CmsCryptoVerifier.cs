using Asn1Kit.Runtime;
using System.Security.Cryptography;

namespace Asn1Kit.Cms.Demo;

public interface ICmsCryptoVerifier<TCertificate>
{
    bool VerifyDigest(Asn1Oid algorithm, ReadOnlyMemory<byte> content, ReadOnlyMemory<byte> claimedDigest, TCertificate certificate);
    bool VerifySignature(Asn1Oid algorithm, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature, TCertificate certificate);
}

public sealed class EducationalCryptoVerifier<TCertificate> : ICmsCryptoVerifier<TCertificate>
{
    public bool VerifyDigest(Asn1Oid algorithm, ReadOnlyMemory<byte> content, ReadOnlyMemory<byte> claimedDigest, TCertificate certificate)
        => !claimedDigest.IsEmpty;

    public bool VerifySignature(Asn1Oid algorithm, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature, TCertificate certificate)
        => !signature.IsEmpty;
}

public sealed class RsaSha256CryptoVerifier<TCertificate> : ICmsCryptoVerifier<TCertificate>
{
    private static readonly Asn1Oid Sha256 = Asn1Oid.Parse("2.16.840.1.101.3.4.2.1");
    private static readonly Asn1Oid RsaEncryption = Asn1Oid.Parse("1.2.840.113549.1.1.1");
    private static readonly Asn1Oid Sha256WithRsa = Asn1Oid.Parse("1.2.840.113549.1.1.11");
    private readonly Func<TCertificate, ReadOnlyMemory<byte>> _getPublicKeyInfo;

    public RsaSha256CryptoVerifier(Func<TCertificate, ReadOnlyMemory<byte>> getPublicKeyInfo)
    {
        _getPublicKeyInfo = getPublicKeyInfo ?? throw new ArgumentNullException(nameof(getPublicKeyInfo));
    }

    public bool VerifyDigest(Asn1Oid algorithm, ReadOnlyMemory<byte> content, ReadOnlyMemory<byte> claimedDigest, TCertificate certificate)
        => algorithm == Sha256 && CryptographicOperations.FixedTimeEquals(SHA256.HashData(content.Span), claimedDigest.Span);

    public bool VerifySignature(Asn1Oid algorithm, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature, TCertificate certificate)
    {
        if (algorithm != RsaEncryption && algorithm != Sha256WithRsa)
            return false;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(_getPublicKeyInfo(certificate).Span, out _);
            return rsa.VerifyData(signedBytes.Span, signature.Span, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

public sealed record CmsSignerResult(int SignerNumber, bool Accepted, string Reason);

internal readonly record struct EncodedCertificate<T>(T Value, ReadOnlyMemory<byte> OriginalEncoding) where T : notnull;

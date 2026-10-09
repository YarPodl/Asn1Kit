using Asn1Kit.Cms;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using CmsAttribute = Asn1Kit.Cms.Attribute;

namespace Asn1Kit.Cms.Demo;

public static class CmsSignedDataInspector
{
    public static IReadOnlyList<CmsSignerResult> Inspect(ReadOnlyMemory<byte> encoded, ICmsCryptoVerifier<Certificate> verifier,
        IReadOnlyCollection<Asn1Value<Certificate>>? availableCertificates = null,
        IReadOnlyCollection<Asn1Value<Certificate>>? trustedRoots = null)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        var reader = new Asn1Reader(encoded, Asn1Encoding.Ber);
        var contentInfo = ContentInfo.Decode(reader);
        reader.ThrowIfNotEmpty();

        if (contentInfo.ContentType != CryptographicMessageSyntax2004Oids.IdSignedData ||
            !contentInfo.TryDecodeContent(ContentInfoContentBindings.SignedData, out var signedData))
            throw new InvalidDataException("ContentInfo must contain SignedData.");
        var content = signedData.EncapContentInfo.EContent;
        if (content is null)
            throw new InvalidDataException("Attached SignedData requires eContent.");
        if (signedData.SignerInfos.Length == 0)
            throw new InvalidDataException("SignedData has no signers.");

        var results = new List<CmsSignerResult>(signedData.SignerInfos.Length);
        for (var index = 0; index < signedData.SignerInfos.Length; index++)
        {
            try
            {
                var certificate = VerifySigner(signedData, signedData.SignerInfos[index], content.Value, verifier, availableCertificates, trustedRoots);
                results.Add(new CmsSignerResult(index + 1, true, trustedRoots is null
                    ? "structure accepted; stub accepted digest and signature"
                    : "certificate chain and CMS signature accepted",
                    RdnSequenceFormatter.Format(certificate.TbsCertificate.Value.Issuer.Value),
                    RdnSequenceFormatter.Format(certificate.TbsCertificate.Value.Subject.Value)));
            }
            catch (Exception ex) when (ex is InvalidDataException or Asn1Exception)
            {
                results.Add(new CmsSignerResult(index + 1, false, ex.Message));
            }
        }

        return results;
    }

    private static Certificate VerifySigner(SignedData signedData, SignerInfo signer, ReadOnlyMemory<byte> content,
        ICmsCryptoVerifier<Certificate> verifier, IReadOnlyCollection<Asn1Value<Certificate>>? availableCertificates,
        IReadOnlyCollection<Asn1Value<Certificate>>? trustedRoots)
    {
        if (!signedData.DigestAlgorithms.Any(item => item.Algorithm == signer.DigestAlgorithm.Algorithm))
            throw new InvalidDataException("Signer digest algorithm is absent from SignedData.digestAlgorithms.");

        var selected = FindCertificate(signedData.Certificates, signer.Sid);
        if (selected is null)
            throw new InvalidDataException("No unique embedded certificate matches the signer identifier.");
        var certificate = selected.Certificate!.Value;

        if (trustedRoots is not null)
            VerifyChain(new EncodedCertificate<Certificate>(certificate, selected.Certificate.EncodedMemory),
                signedData.Certificates, availableCertificates ?? Array.Empty<Asn1Value<Certificate>>(), trustedRoots, verifier);

        ReadOnlyMemory<byte> signedBytes = content;
        if (signer.SignedAttrs is not null)
        {
            var signedAttrs = signer.SignedAttrs.Value;
            var claimedDigest = ReadSignedAttributes(signedAttrs.Value, signedData.EncapContentInfo.EContentType);
            if (!verifier.VerifyDigest(signer.DigestAlgorithm.Algorithm, content, claimedDigest, certificate))
                throw new InvalidDataException("Digest was rejected by the educational stub.");

            var originalAttributes = new Asn1Any(signedAttrs.OriginalEncoding);
            signedBytes = Asn1Any.FromTagAndContents(Asn1Tag.Set, originalAttributes.ContentsMemory.Span).EncodedMemory;
        }

        if (!verifier.VerifySignature(signer.SignatureAlgorithm.Algorithm, signedBytes, signer.Signature, certificate))
            throw new InvalidDataException("Signature was rejected by the educational stub.");
        return certificate;
    }

    private static ReadOnlyMemory<byte> ReadSignedAttributes(CmsAttribute[] attributes, Asn1Oid contentType)
    {
        Asn1Oid? declaredContentType = null;
        ReadOnlyMemory<byte>? digest = null;
        foreach (var attribute in attributes)
        {
            if (attribute.AttrType == CryptographicMessageSyntax2004Oids.IdContentType)
            {
                if (declaredContentType is not null || attribute.AttrValues.Length != 1)
                    throw new InvalidDataException("SignedAttrs must contain exactly one contentType value.");
                declaredContentType = attribute.AttrValues[0].DecodeValue(static reader => reader.ReadOid(Asn1Tag.ObjectIdentifier));
            }
            else if (attribute.AttrType == CryptographicMessageSyntax2004Oids.IdMessageDigest)
            {
                if (digest is not null || attribute.AttrValues.Length != 1)
                    throw new InvalidDataException("SignedAttrs must contain exactly one messageDigest value.");
                digest = attribute.AttrValues[0].DecodeValue(static reader => reader.ReadOctetString(Asn1Tag.OctetString));
            }
        }

        if (declaredContentType is null || digest is null)
            throw new InvalidDataException("SignedAttrs requires contentType and messageDigest.");
        if (declaredContentType.Value != contentType)
            throw new InvalidDataException("SignedAttrs contentType differs from eContentType.");
        return digest.Value;
    }

    private static CertificateChoices? FindCertificate(CertificateChoices[]? choices, SignerIdentifier sid)
    {
        CertificateChoices? match = null;
        foreach (var choice in choices ?? Array.Empty<CertificateChoices>())
        {
            if (choice.Kind != CertificateChoicesKind.Certificate || choice.Certificate is null)
                continue;
            var certificate = choice.Certificate.Value;
            var matches = sid.Kind switch
            {
                SignerIdentifierKind.IssuerAndSerialNumber => MatchesIssuerAndSerial(certificate, sid.IssuerAndSerialNumber!),
                SignerIdentifierKind.SubjectKeyIdentifier => MatchesSubjectKeyIdentifier(certificate, sid.SubjectKeyIdentifier!.Value),
                _ => false
            };
            if (!matches) continue;
            if (match is not null) return null;
            match = choice;
        }
        return match;
    }

    private static bool MatchesIssuerAndSerial(Certificate certificate, IssuerAndSerialNumber sid)
    {
        if (certificate.TbsCertificate.Value.SerialNumber != sid.SerialNumber) return false;
        return certificate.TbsCertificate.Value.Issuer.OriginalEncoding.Span
            .SequenceEqual(sid.Issuer.OriginalEncoding.Span);
    }

    private static bool MatchesSubjectKeyIdentifier(Certificate certificate, ReadOnlyMemory<byte> keyIdentifier)
    {
        var candidate = ReadSubjectKeyIdentifier(certificate);
        return candidate is not null && candidate.Value.Span.SequenceEqual(keyIdentifier.Span);
    }

    private static ReadOnlyMemory<byte>? ReadSubjectKeyIdentifier(Certificate certificate)
    {
        var extension = FindExtension(certificate, PKIX1Implicit88Oids.IdCeSubjectKeyIdentifier);
        if (extension is null) return null;
        var reader = new Asn1Reader(extension.ExtnValue, Asn1Encoding.Ber);
        var keyIdentifier = reader.ReadOctetString(Asn1Tag.OctetString);
        reader.ThrowIfNotEmpty();
        return keyIdentifier;
    }

    private static AuthorityKeyIdentifier? ReadAuthorityKeyIdentifier(Certificate certificate)
    {
        var extension = FindExtension(certificate, PKIX1Implicit88Oids.IdCeAuthorityKeyIdentifier);
        if (extension is null) return null;
        var reader = new Asn1Reader(extension.ExtnValue, Asn1Encoding.Ber);
        var identifier = AuthorityKeyIdentifier.Decode(reader);
        reader.ThrowIfNotEmpty();
        if ((identifier.AuthorityCertIssuer is null) != (identifier.AuthorityCertSerialNumber is null) ||
            (identifier.KeyIdentifier is null && identifier.AuthorityCertIssuer is null))
            throw new InvalidDataException("Certificate AuthorityKeyIdentifier has incomplete issuer identification.");
        return identifier;
    }

    private static bool MatchesAuthority(Certificate candidate, AuthorityKeyIdentifier? identifier)
    {
        if (identifier is null) return true;
        if (identifier.KeyIdentifier is { } keyIdentifier && !MatchesSubjectKeyIdentifier(candidate, keyIdentifier))
            return false;
        if (identifier.AuthorityCertIssuer is null) return true;
        if (candidate.TbsCertificate.Value.SerialNumber != identifier.AuthorityCertSerialNumber)
            return false;
        return identifier.AuthorityCertIssuer.Any(name => name.Kind == GeneralNameKind.DirectoryName &&
            NamesEqual(name.DirectoryName!, candidate.TbsCertificate.Value.Issuer.Value));
    }

    private static bool NamesEqual(AttributeTypeAndValue[][] left, AttributeTypeAndValue[][] right)
    {
        if (left.Length != right.Length) return false;
        for (var rdn = 0; rdn < left.Length; rdn++)
        {
            if (left[rdn].Length != right[rdn].Length) return false;
            for (var attribute = 0; attribute < left[rdn].Length; attribute++)
            {
                var a = left[rdn][attribute];
                var b = right[rdn][attribute];
                if (a.Type != b.Type ||
                    !a.Value.EncodedMemory.Span.SequenceEqual(b.Value.EncodedMemory.Span))
                    return false;
            }
        }
        return true;
    }

    private static Extension? FindExtension(Certificate certificate, Asn1Oid oid)
    {
        Extension? match = null;
        foreach (var extension in certificate.TbsCertificate.Value.Extensions?.Value ?? Array.Empty<Extension>())
        {
            if (extension.ExtnID != oid) continue;
            if (match is not null)
                throw new InvalidDataException("Certificate has duplicate key identifier extensions.");
            match = extension;
        }
        return match;
    }

    private static void VerifyChain(EncodedCertificate<Certificate> signer, CertificateChoices[]? embedded,
        IReadOnlyCollection<Asn1Value<Certificate>> available, IReadOnlyCollection<Asn1Value<Certificate>> trustedRoots,
        ICmsCryptoVerifier<Certificate> verifier)
    {
        if (trustedRoots.Count == 0)
            throw new InvalidDataException("At least one trusted root certificate is required.");

        if (signer.OriginalEncoding.IsEmpty || available.Concat(trustedRoots).Any(item => item.OriginalEncoding.IsEmpty))
            throw new InvalidDataException("Certificate chain requires original certificate encodings.");
        var candidates = (embedded ?? Array.Empty<CertificateChoices>())
            .Where(choice => choice.Certificate is not null)
            .Select(choice => new EncodedCertificate<Certificate>(choice.Certificate!.Value, choice.Certificate.EncodedMemory))
            .Concat(available.Concat(trustedRoots).Select(item => new EncodedCertificate<Certificate>(item.Value, item.OriginalEncoding)))
            .GroupBy(candidate => Convert.ToHexString(candidate.OriginalEncoding.Span), StringComparer.Ordinal)
            .Select(group => group.First()).ToArray();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = signer;
        while (true)
        {
            var identity = Convert.ToHexString(current.OriginalEncoding.Span);
            if (!visited.Add(identity))
                throw new InvalidDataException("Certificate chain contains a cycle.");

            var trusted = trustedRoots.Any(root => root.OriginalEncoding.Span.SequenceEqual(current.OriginalEncoding.Span));
            var issuerName = current.Value.TbsCertificate.Value.Issuer.OriginalEncoding;
            var authorityKeyIdentifier = ReadAuthorityKeyIdentifier(current.Value);
            var issuers = candidates.Where(candidate =>
                issuerName.Span.SequenceEqual(candidate.Value.TbsCertificate.Value.Subject.OriginalEncoding.Span) &&
                MatchesAuthority(candidate.Value, authorityKeyIdentifier)).ToArray();
            if (issuers.Length == 0)
                throw new InvalidDataException("Certificate chain issuer is unavailable.");
            if (issuers.Length != 1)
                throw new InvalidDataException("Certificate chain issuer is ambiguous.");

            var issuer = issuers[0];
            if (current.Value.Signature.UnusedBits != 0 ||
                current.Value.SignatureAlgorithm.Algorithm != current.Value.TbsCertificate.Value.Signature.Algorithm ||
                !verifier.VerifySignature(current.Value.SignatureAlgorithm.Algorithm,
                    current.Value.TbsCertificate.OriginalEncoding, current.Value.Signature.Memory, issuer.Value))
                throw new InvalidDataException("Certificate chain signature was rejected.");
            if (trusted)
                return;
            current = issuer;
        }
    }

}

using Asn1Kit.Runtime;
using Asn1Kit.Cms.Demo;
using ModernCms = Asn1Kit.Modern.CryptographicMessageSyntax2009;
using Common = Asn1Kit.Modern.PKIXCommonTypes2009;
using ModernPkix = Asn1Kit.Modern.PKIX1Explicit2009;
using ModernImplicit = Asn1Kit.Modern.PKIX1Implicit2009;

namespace Asn1Kit.Cms.Demo.Modern;

public static class CmsModernSignedDataInspector
{
    public static IReadOnlyList<CmsSignerResult> Inspect(ReadOnlyMemory<byte> encoded, ICmsCryptoVerifier<ModernPkix.Certificate> verifier,
        IReadOnlyCollection<Asn1Value<ModernPkix.Certificate>>? availableCertificates = null,
        IReadOnlyCollection<Asn1Value<ModernPkix.Certificate>>? trustedRoots = null)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        var reader = new Asn1Reader(encoded, Asn1Encoding.Ber);
        var contentInfo = ModernCms.ContentInfo.Decode(reader);
        reader.ThrowIfNotEmpty();

        if (contentInfo.ContentType != ModernCms.CryptographicMessageSyntax2009Oids.IdSignedData || contentInfo.Content.CtSignedData is null)
            throw new InvalidDataException("ContentInfo must contain SignedData.");

        var signedData = contentInfo.Content.CtSignedData;
        var content = signedData.EncapContentInfo.EContent?.Contents;
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
                    RdnSequenceFormatter.Format(certificate.ToBeSigned.Issuer.Value),
                    RdnSequenceFormatter.Format(certificate.ToBeSigned.Subject.Value)));
            }
            catch (Exception ex) when (ex is InvalidDataException or Asn1Exception)
            {
                results.Add(new CmsSignerResult(index + 1, false, ex.Message));
            }
        }

        return results;
    }

    private static ModernPkix.Certificate VerifySigner(ModernCms.SignedData signedData, ModernCms.SignerInfo signer, ReadOnlyMemory<byte> content,
        ICmsCryptoVerifier<ModernPkix.Certificate> verifier,
        IReadOnlyCollection<Asn1Value<ModernPkix.Certificate>>? availableCertificates,
        IReadOnlyCollection<Asn1Value<ModernPkix.Certificate>>? trustedRoots)
    {
        if (!signedData.DigestAlgorithms.Any(item => item.Algorithm == signer.DigestAlgorithm.Algorithm))
            throw new InvalidDataException("Signer digest algorithm is absent from SignedData.digestAlgorithms.");

        var selected = FindCertificate(signedData.Certificates, signer.Sid);
        if (selected is null)
            throw new InvalidDataException("No unique embedded certificate matches the signer identifier.");
        var certificate = selected.Value.Value;

        if (trustedRoots is not null)
            VerifyChain(new EncodedCertificate<ModernPkix.Certificate>(certificate, selected.Value.OriginalEncoding),
                signedData.Certificates, availableCertificates ?? Array.Empty<Asn1Value<ModernPkix.Certificate>>(), trustedRoots, verifier);

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

    private static ReadOnlyMemory<byte> ReadSignedAttributes(ModernCms.Attribute[] attributes, Asn1Oid contentType)
    {
        Asn1Oid? declaredContentType = null;
        ReadOnlyMemory<byte>? digest = null;
        foreach (var attribute in attributes)
        {
            if (attribute.AttrType == ModernCms.CryptographicMessageSyntax2009Oids.IdContentType)
            {
                if (declaredContentType is not null || attribute.AttrValues.Length != 1)
                    throw new InvalidDataException("SignedAttrs must contain exactly one contentType value.");
                declaredContentType = attribute.AttrValues[0].DecodeValue(static reader => reader.ReadOid(Asn1Tag.ObjectIdentifier));
            }
            else if (attribute.AttrType == ModernCms.CryptographicMessageSyntax2009Oids.IdMessageDigest)
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

    private static Asn1Value<ModernPkix.Certificate>? FindCertificate(ModernCms.CertificateChoices[]? choices, ModernCms.SignerIdentifier sid)
    {
        Asn1Value<ModernPkix.Certificate>? match = null;
        foreach (var choice in choices ?? Array.Empty<ModernCms.CertificateChoices>())
        {
            if (choice.Kind != ModernCms.CertificateChoicesKind.Certificate || choice.Certificate is null)
                continue;
            var certificate = choice.Certificate.Value.Value;
            var matches = sid.Kind switch
            {
                ModernCms.SignerIdentifierKind.IssuerAndSerialNumber => MatchesIssuerAndSerial(certificate, sid.IssuerAndSerialNumber!),
                ModernCms.SignerIdentifierKind.SubjectKeyIdentifier => MatchesSubjectKeyIdentifier(certificate, sid.SubjectKeyIdentifier!.Value),
                _ => false
            };
            if (!matches) continue;
            if (match is not null) return null;
            match = choice.Certificate.Value;
        }
        return match;
    }

    private static bool MatchesIssuerAndSerial(ModernPkix.Certificate certificate, ModernCms.IssuerAndSerialNumber sid)
    {
        if (certificate.ToBeSigned.SerialNumber != sid.SerialNumber) return false;
        return certificate.ToBeSigned.Issuer.OriginalEncoding.Span.SequenceEqual(sid.Issuer.OriginalEncoding.Span);
    }

    private static bool MatchesSubjectKeyIdentifier(ModernPkix.Certificate certificate, ReadOnlyMemory<byte> keyIdentifier)
    {
        var candidate = ReadSubjectKeyIdentifier(certificate);
        return candidate is not null && candidate.Value.Span.SequenceEqual(keyIdentifier.Span);
    }

    private static ReadOnlyMemory<byte>? ReadSubjectKeyIdentifier(ModernPkix.Certificate certificate)
    {
        var extension = FindExtension(certificate, ModernImplicit.PKIX1Implicit2009Oids.IdCeSubjectKeyIdentifier);
        if (extension is null) return null;
        var reader = new Asn1Reader(extension.ExtnValue.Contents, Asn1Encoding.Ber);
        var keyIdentifier = reader.ReadOctetString(Asn1Tag.OctetString);
        reader.ThrowIfNotEmpty();
        return keyIdentifier;
    }

    private static ModernImplicit.AuthorityKeyIdentifier? ReadAuthorityKeyIdentifier(ModernPkix.Certificate certificate)
    {
        var extension = FindExtension(certificate, ModernImplicit.PKIX1Implicit2009Oids.IdCeAuthorityKeyIdentifier);
        if (extension is null) return null;
        var reader = new Asn1Reader(extension.ExtnValue.Contents, Asn1Encoding.Ber);
        var identifier = ModernImplicit.AuthorityKeyIdentifier.Decode(reader);
        reader.ThrowIfNotEmpty();
        if ((identifier.AuthorityCertIssuer is null) != (identifier.AuthorityCertSerialNumber is null) ||
            (identifier.KeyIdentifier is null && identifier.AuthorityCertIssuer is null))
            throw new InvalidDataException("Certificate AuthorityKeyIdentifier has incomplete issuer identification.");
        return identifier;
    }

    private static bool MatchesAuthority(ModernPkix.Certificate candidate, ModernImplicit.AuthorityKeyIdentifier? identifier)
    {
        if (identifier is null) return true;
        if (identifier.KeyIdentifier is { } keyIdentifier && !MatchesSubjectKeyIdentifier(candidate, keyIdentifier))
            return false;
        if (identifier.AuthorityCertIssuer is null) return true;
        if (candidate.ToBeSigned.SerialNumber != identifier.AuthorityCertSerialNumber)
            return false;
        return identifier.AuthorityCertIssuer.Any(name => name.Kind == ModernImplicit.GeneralNameKind.DirectoryName &&
            NamesEqual(name.DirectoryName!, candidate.ToBeSigned.Issuer.Value));
    }

    private static bool NamesEqual(Common.SingleAttribute[][] left, Common.SingleAttribute[][] right)
    {
        if (left.Length != right.Length) return false;
        for (var rdn = 0; rdn < left.Length; rdn++)
        {
            if (left[rdn].Length != right[rdn].Length) return false;
            for (var attribute = 0; attribute < left[rdn].Length; attribute++)
            {
                var a = left[rdn][attribute];
                var b = right[rdn][attribute];
                if (a.Type != b.Type || !a.Value.EncodedMemory.Span.SequenceEqual(b.Value.EncodedMemory.Span))
                    return false;
            }
        }
        return true;
    }

    private static Common.Extension? FindExtension(ModernPkix.Certificate certificate, Asn1Oid oid)
    {
        Common.Extension? match = null;
        foreach (var extension in certificate.ToBeSigned.Extensions ?? Array.Empty<Common.Extension>())
        {
            if (extension.ExtnID != oid) continue;
            if (match is not null)
                throw new InvalidDataException("Certificate has duplicate key identifier extensions.");
            match = extension;
        }
        return match;
    }

    private static void VerifyChain(EncodedCertificate<ModernPkix.Certificate> signer, ModernCms.CertificateChoices[]? embedded,
        IReadOnlyCollection<Asn1Value<ModernPkix.Certificate>> available,
        IReadOnlyCollection<Asn1Value<ModernPkix.Certificate>> trustedRoots,
        ICmsCryptoVerifier<ModernPkix.Certificate> verifier)
    {
        if (trustedRoots.Count == 0)
            throw new InvalidDataException("At least one trusted root certificate is required.");

        if (signer.OriginalEncoding.IsEmpty || available.Concat(trustedRoots).Any(item => item.OriginalEncoding.IsEmpty))
            throw new InvalidDataException("Certificate chain requires original certificate encodings.");
        var candidates = (embedded ?? Array.Empty<ModernCms.CertificateChoices>())
            .Where(choice => choice.Certificate is not null)
            .Select(choice => new EncodedCertificate<ModernPkix.Certificate>(
                choice.Certificate!.Value.Value, choice.Certificate.Value.OriginalEncoding))
            .Concat(available.Concat(trustedRoots).Select(item =>
                new EncodedCertificate<ModernPkix.Certificate>(item.Value, item.OriginalEncoding)))
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
            var issuerName = current.Value.ToBeSigned.Issuer.OriginalEncoding;
            var authorityKeyIdentifier = ReadAuthorityKeyIdentifier(current.Value);
            var issuers = candidates.Where(candidate =>
                issuerName.Span.SequenceEqual(candidate.Value.ToBeSigned.Subject.OriginalEncoding.Span) &&
                MatchesAuthority(candidate.Value, authorityKeyIdentifier)).ToArray();
            if (issuers.Length == 0)
                throw new InvalidDataException("Certificate chain issuer is unavailable.");
            if (issuers.Length != 1)
                throw new InvalidDataException("Certificate chain issuer is ambiguous.");

            var issuer = issuers[0];
            if (current.Value.Signature.UnusedBits != 0 ||
                current.Value.AlgorithmIdentifier.Algorithm != current.Value.ToBeSigned.Signature.Algorithm ||
                !verifier.VerifySignature(current.Value.AlgorithmIdentifier.Algorithm,
                    current.Value.ToBeSignedOriginalEncoding, current.Value.Signature.Memory, issuer.Value))
                throw new InvalidDataException("Certificate chain signature was rejected.");
            if (trusted)
                return;
            current = issuer;
        }
    }

}

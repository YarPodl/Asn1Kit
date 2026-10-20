using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// One-shot oracle dump for RFC 4134 CMS fixtures.
// Uses SignedCms + System.Formats.Asn1 only — never Asn1Kit.

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Asn1Kit.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }

    throw new InvalidOperationException("Asn1Kit.sln not found from " + AppContext.BaseDirectory);
}

var root = RepoRoot();
var fixtureDir = Path.Combine(root, "runtime-csharp", "fixtures", "cms", "rfc4134");
var detached = File.ReadAllBytes(Path.Combine(fixtureDir, "detached-content.bin"));

var document = new ExpectedDocument
{
    Source = "RFC 4134 Appendix A binaries; expected from System.Security.Cryptography.Pkcs.SignedCms + System.Formats.Asn1 (see README.md)",
    DetachedContentSha1Hex = Convert.ToHexString(SHA1.HashData(detached)).ToLowerInvariant(),
    DetachedContentUtf8 = Encoding.ASCII.GetString(detached),
    Messages = new Dictionary<string, ExpectedMessage>(StringComparer.Ordinal),
};

DumpDataContentInfo(document, fixtureDir, "3.2.bin");
DumpSigned(document, fixtureDir, "4.2.bin", detachedContent: null);
DumpSigned(document, fixtureDir, "4.3.bin", detachedContent: detached);
DumpSigned(document, fixtureDir, "4.6.bin", detachedContent: null);
DumpSigned(document, fixtureDir, "4.7.bin", detachedContent: null);
DumpSigned(document, fixtureDir, "4.10.bin", detachedContent: null);

var jsonPath = Path.Combine(fixtureDir, "expected.json");
var json = JsonSerializer.Serialize(document, new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
});
File.WriteAllText(jsonPath, json + Environment.NewLine);
Console.WriteLine("Wrote " + jsonPath);

static void DumpDataContentInfo(ExpectedDocument document, string fixtureDir, string fileName)
{
    var der = File.ReadAllBytes(Path.Combine(fixtureDir, fileName));
    var reader = new AsnReader(der, AsnEncodingRules.DER);
    var seq = reader.ReadSequence();
    var contentType = seq.ReadObjectIdentifier();
    var contentReader = seq.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
    var octet = contentReader.ReadOctetString();
    contentReader.ThrowIfNotEmpty();
    seq.ThrowIfNotEmpty();
    reader.ThrowIfNotEmpty();

    var message = new ExpectedMessage
    {
        Kind = "data",
        ContentType = contentType,
        DataOctetLength = octet.Length,
        DataOctetUtf8 = Encoding.ASCII.GetString(octet),
        DataOctetSha1Hex = Convert.ToHexString(SHA1.HashData(octet)).ToLowerInvariant(),
    };
    document.Messages[fileName] = message;
    WriteDump(fixtureDir, fileName, DumpDataText(message, der));
}

static void DumpSigned(ExpectedDocument document, string fixtureDir, string fileName, byte[]? detachedContent)
{
    var der = File.ReadAllBytes(Path.Combine(fixtureDir, fileName));
    SignedCms signed;
    if (detachedContent is null)
    {
        signed = new SignedCms();
        signed.Decode(der);
    }
    else
    {
        signed = new SignedCms(new ContentInfo(detachedContent), detached: true);
        signed.Decode(der);
    }

    var structural = ReadSignedDataStructure(der);
    var message = new ExpectedMessage
    {
        Kind = "signedData",
        // Outer ContentInfo.contentType (id-signedData), not eContentType.
        ContentType = structural.OuterContentType,
        SignedData = new ExpectedSignedData
        {
            Version = structural.Version,
            DigestAlgorithms = structural.DigestAlgorithms,
            EContentType = structural.EContentType,
            EContentPresent = structural.EContent is not null,
            EContentLength = structural.EContent?.Length,
            EContentSha1Hex = structural.EContent is null
                ? null
                : Convert.ToHexString(SHA1.HashData(structural.EContent)).ToLowerInvariant(),
            CertificateCount = signed.Certificates.Count,
            Certificates = signed.Certificates.Cast<X509Certificate2>().Select(cert => new ExpectedCertificate
            {
                SerialNumberHex = cert.SerialNumber.ToLowerInvariant(),
                Subject = cert.Subject,
                SubjectKeyIdentifierHex = GetSkiHex(cert),
            }).ToList(),
            SignerCount = signed.SignerInfos.Count,
            SignerInfos = new List<ExpectedSignerInfo>(),
        },
    };

    for (var i = 0; i < signed.SignerInfos.Count; i++)
    {
        var signer = signed.SignerInfos[i];
        var structuralSigner = structural.SignerInfos[i];
        var expectedSigner = new ExpectedSignerInfo
        {
            SidKind = structuralSigner.SidKind,
            SerialNumberHex = structuralSigner.SerialNumberHex,
            SubjectKeyIdentifierHex = structuralSigner.SubjectKeyIdentifierHex,
            DigestAlgorithm = signer.DigestAlgorithm.Value!,
            SignatureAlgorithm = structuralSigner.SignatureAlgorithm,
            SignatureByteLength = signer.GetSignature().Length,
            SignedAttrsPresent = structuralSigner.SignedAttrsTlvHex is not null,
            SignedAttrsTlvHex = structuralSigner.SignedAttrsTlvHex,
            SignedAttrs = structuralSigner.SignedAttrs,
        };
        message.SignedData!.SignerInfos.Add(expectedSigner);
    }

    document.Messages[fileName] = message;
    WriteDump(fixtureDir, fileName, DumpSignedText(message, signed, der));
}

static SignedDataStructure ReadSignedDataStructure(byte[] der)
{
    var outer = new AsnReader(der, AsnEncodingRules.DER);
    var contentInfo = outer.ReadSequence();
    var outerContentType = contentInfo.ReadObjectIdentifier();
    var content = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
    var signedData = content.ReadSequence();
    content.ThrowIfNotEmpty();
    contentInfo.ThrowIfNotEmpty();
    outer.ThrowIfNotEmpty();

    var version = (int)signedData.ReadInteger();
    var digestAlgorithms = new List<string>();
    var digestSet = signedData.ReadSetOf();
    while (digestSet.HasData)
    {
        var alg = digestSet.ReadSequence();
        digestAlgorithms.Add(alg.ReadObjectIdentifier());
        if (alg.HasData)
            alg.ReadEncodedValue(); // parameters
        alg.ThrowIfNotEmpty();
    }

    var encap = signedData.ReadSequence();
    var eContentType = encap.ReadObjectIdentifier();
    byte[]? eContent = null;
    if (encap.HasData)
    {
        var eContentOuter = encap.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
        eContent = eContentOuter.ReadOctetString();
        eContentOuter.ThrowIfNotEmpty();
    }
    encap.ThrowIfNotEmpty();

    if (signedData.HasData &&
        signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
    {
        var certs = signedData.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
        while (certs.HasData)
            certs.ReadEncodedValue();
    }

    if (signedData.HasData &&
        signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
    {
        var crls = signedData.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 1));
        while (crls.HasData)
            crls.ReadEncodedValue();
    }

    var signerInfos = new List<SignerInfoStructure>();
    var signerSet = signedData.ReadSetOf();
    while (signerSet.HasData)
        signerInfos.Add(ReadSignerInfo(signerSet.ReadSequence()));
    signedData.ThrowIfNotEmpty();

    return new SignedDataStructure(
        outerContentType, version, digestAlgorithms, eContentType, eContent, signerInfos);
}

static SignerInfoStructure ReadSignerInfo(AsnReader signer)
{
    _ = (int)signer.ReadInteger(); // version
    string sidKind;
    string? serialHex = null;
    string? skiHex = null;
    var sidTag = signer.PeekTag();
    if (sidTag.TagClass == TagClass.Universal && sidTag.TagValue == (int)UniversalTagNumber.Sequence)
    {
        sidKind = "issuerAndSerial";
        var ias = signer.ReadSequence();
        ias.ReadEncodedValue(); // issuer Name
        serialHex = Convert.ToHexString(ias.ReadIntegerBytes().ToArray()).ToLowerInvariant().TrimStart('0');
        if (serialHex.Length == 0)
            serialHex = "0";
        if ((serialHex.Length & 1) == 1)
            serialHex = "0" + serialHex;
        ias.ThrowIfNotEmpty();
    }
    else if (sidTag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
    {
        sidKind = "subjectKeyIdentifier";
        skiHex = Convert.ToHexString(signer.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0)))
            .ToLowerInvariant();
    }
    else
    {
        throw new InvalidOperationException("Unsupported SignerIdentifier tag: " + sidTag);
    }

    var digestAlg = signer.ReadSequence();
    var digestOid = digestAlg.ReadObjectIdentifier();
    if (digestAlg.HasData)
        digestAlg.ReadEncodedValue();
    digestAlg.ThrowIfNotEmpty();

    ExpectedSignedAttrs? signedAttrs = null;
    string? signedAttrsTlvHex = null;
    if (signer.HasData &&
        signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
    {
        // Capture full IMPLICIT [0] SET encoding, then reparse as universal SET for field dumps.
        var encoded = signer.ReadEncodedValue();
        signedAttrsTlvHex = Convert.ToHexString(encoded.ToArray()).ToLowerInvariant();
        // Rewrite context tag 0xA0 to universal SET 0x31 for attribute walk / content compare.
        var rewritten = encoded.ToArray();
        rewritten[0] = 0x31;
        var attrReader = new AsnReader(rewritten, AsnEncodingRules.DER);
        var set = attrReader.ReadSetOf();
        signedAttrs = ReadSignedAttrs(set);
        attrReader.ThrowIfNotEmpty();
    }

    var sigAlg = signer.ReadSequence();
    var signatureAlgorithm = sigAlg.ReadObjectIdentifier();
    if (sigAlg.HasData)
        sigAlg.ReadEncodedValue();
    sigAlg.ThrowIfNotEmpty();

    var signature = signer.ReadOctetString();
    if (signer.HasData &&
        signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
    {
        var unsigned = signer.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 1));
        while (unsigned.HasData)
            unsigned.ReadEncodedValue();
    }
    signer.ThrowIfNotEmpty();

    return new SignerInfoStructure(
        sidKind,
        serialHex,
        skiHex,
        digestOid,
        signatureAlgorithm,
        signature.Length,
        signedAttrsTlvHex,
        signedAttrs);
}

static ExpectedSignedAttrs ReadSignedAttrs(AsnReader set)
{
    string? contentType = null;
    string? messageDigestHex = null;
    string? signingTimeUtc = null;
    var oids = new List<string>();
    while (set.HasData)
    {
        var attr = set.ReadSequence();
        var oid = attr.ReadObjectIdentifier();
        oids.Add(oid);
        var values = attr.ReadSetOf();
        var valueEncoding = values.ReadEncodedValue();
        values.ThrowIfNotEmpty();
        attr.ThrowIfNotEmpty();

        switch (oid)
        {
            case "1.2.840.113549.1.9.3":
                contentType = new AsnReader(valueEncoding, AsnEncodingRules.DER).ReadObjectIdentifier();
                break;
            case "1.2.840.113549.1.9.4":
                messageDigestHex = Convert.ToHexString(
                    new AsnReader(valueEncoding, AsnEncodingRules.DER).ReadOctetString()).ToLowerInvariant();
                break;
            case "1.2.840.113549.1.9.5":
            {
                var timeReader = new AsnReader(valueEncoding, AsnEncodingRules.DER);
                if (timeReader.PeekTag().TagValue == (int)UniversalTagNumber.UtcTime)
                    signingTimeUtc = timeReader.ReadUtcTime().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                else
                    signingTimeUtc = timeReader.ReadGeneralizedTime().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                break;
            }
        }
    }

    return new ExpectedSignedAttrs
    {
        AttributeOids = oids,
        ContentType = contentType,
        MessageDigestHex = messageDigestHex,
        SigningTimeUtc = signingTimeUtc,
    };
}

static string? GetSkiHex(X509Certificate2 cert)
{
    foreach (var extension in cert.Extensions)
    {
        if (extension is X509SubjectKeyIdentifierExtension ski)
            return ski.SubjectKeyIdentifier?.ToLowerInvariant();
    }

    return null;
}

static void WriteDump(string fixtureDir, string fileName, string text)
{
    var path = Path.Combine(fixtureDir, Path.GetFileNameWithoutExtension(fileName) + ".signedcms.txt");
    File.WriteAllText(path, text);
    Console.WriteLine("Wrote " + path);
}

static string DumpDataText(ExpectedMessage message, byte[] der) =>
    "file: " + message.Kind + Environment.NewLine +
    "derLength: " + der.Length + Environment.NewLine +
    "contentType: " + message.ContentType + Environment.NewLine +
    "dataOctetLength: " + message.DataOctetLength + Environment.NewLine +
    "dataOctetUtf8: " + message.DataOctetUtf8 + Environment.NewLine +
    "dataOctetSha1Hex: " + message.DataOctetSha1Hex + Environment.NewLine;

static string DumpSignedText(ExpectedMessage message, SignedCms signed, byte[] der)
{
    var sb = new StringBuilder();
    sb.AppendLine("fileKind: " + message.Kind);
    sb.AppendLine("derLength: " + der.Length);
    sb.AppendLine("contentType: " + message.ContentType);
    var sd = message.SignedData!;
    sb.AppendLine("version: " + sd.Version);
    sb.AppendLine("digestAlgorithms: " + string.Join(", ", sd.DigestAlgorithms));
    sb.AppendLine("eContentType: " + sd.EContentType);
    sb.AppendLine("eContentPresent: " + sd.EContentPresent);
    sb.AppendLine("eContentLength: " + sd.EContentLength);
    sb.AppendLine("eContentSha1Hex: " + sd.EContentSha1Hex);
    sb.AppendLine("certificateCount: " + sd.CertificateCount);
    foreach (var cert in sd.Certificates)
    {
        sb.AppendLine($"certificate: serial={cert.SerialNumberHex}; subject={cert.Subject}; ski={cert.SubjectKeyIdentifierHex}");
    }

    sb.AppendLine("signerCount: " + sd.SignerCount);
    for (var i = 0; i < sd.SignerInfos.Count; i++)
    {
        var s = sd.SignerInfos[i];
        sb.AppendLine($"signer[{i}].sidKind: {s.SidKind}");
        sb.AppendLine($"signer[{i}].serialNumberHex: {s.SerialNumberHex}");
        sb.AppendLine($"signer[{i}].subjectKeyIdentifierHex: {s.SubjectKeyIdentifierHex}");
        sb.AppendLine($"signer[{i}].digestAlgorithm: {s.DigestAlgorithm}");
        sb.AppendLine($"signer[{i}].signatureAlgorithm: {s.SignatureAlgorithm}");
        sb.AppendLine($"signer[{i}].signatureByteLength: {s.SignatureByteLength}");
        sb.AppendLine($"signer[{i}].signedAttrsPresent: {s.SignedAttrsPresent}");
        sb.AppendLine($"signer[{i}].signedAttrsTlvHex: {s.SignedAttrsTlvHex}");
        if (s.SignedAttrs is not null)
        {
            sb.AppendLine($"signer[{i}].signedAttrs.attributeOids: {string.Join(", ", s.SignedAttrs.AttributeOids)}");
            sb.AppendLine($"signer[{i}].signedAttrs.contentType: {s.SignedAttrs.ContentType}");
            sb.AppendLine($"signer[{i}].signedAttrs.messageDigestHex: {s.SignedAttrs.MessageDigestHex}");
            sb.AppendLine($"signer[{i}].signedAttrs.signingTimeUtc: {s.SignedAttrs.SigningTimeUtc}");
        }

        sb.AppendLine($"signedCms.signer[{i}].certificate.serial: {signed.SignerInfos[i].Certificate?.SerialNumber.ToLowerInvariant()}");
    }

    return sb.ToString();
}

sealed class ExpectedDocument
{
    public string Source { get; set; } = "";
    public string? DetachedContentSha1Hex { get; set; }
    public string? DetachedContentUtf8 { get; set; }
    public Dictionary<string, ExpectedMessage> Messages { get; set; } = new();
}

sealed class ExpectedMessage
{
    public string Kind { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int? DataOctetLength { get; set; }
    public string? DataOctetUtf8 { get; set; }
    public string? DataOctetSha1Hex { get; set; }
    public ExpectedSignedData? SignedData { get; set; }
}

sealed class ExpectedSignedData
{
    public int Version { get; set; }
    public List<string> DigestAlgorithms { get; set; } = new();
    public string EContentType { get; set; } = "";
    public bool EContentPresent { get; set; }
    public int? EContentLength { get; set; }
    public string? EContentSha1Hex { get; set; }
    public int CertificateCount { get; set; }
    public List<ExpectedCertificate> Certificates { get; set; } = new();
    public int SignerCount { get; set; }
    public List<ExpectedSignerInfo> SignerInfos { get; set; } = new();
}

sealed class ExpectedCertificate
{
    public string SerialNumberHex { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? SubjectKeyIdentifierHex { get; set; }
}

sealed class ExpectedSignerInfo
{
    public string SidKind { get; set; } = "";
    public string? SerialNumberHex { get; set; }
    public string? SubjectKeyIdentifierHex { get; set; }
    public string DigestAlgorithm { get; set; } = "";
    public string SignatureAlgorithm { get; set; } = "";
    public int SignatureByteLength { get; set; }
    public bool SignedAttrsPresent { get; set; }
    public string? SignedAttrsTlvHex { get; set; }
    public ExpectedSignedAttrs? SignedAttrs { get; set; }
}

sealed class ExpectedSignedAttrs
{
    public List<string> AttributeOids { get; set; } = new();
    public string? ContentType { get; set; }
    public string? MessageDigestHex { get; set; }
    public string? SigningTimeUtc { get; set; }
}

readonly record struct SignedDataStructure(
    string OuterContentType,
    int Version,
    List<string> DigestAlgorithms,
    string EContentType,
    byte[]? EContent,
    List<SignerInfoStructure> SignerInfos);

readonly record struct SignerInfoStructure(
    string SidKind,
    string? SerialNumberHex,
    string? SubjectKeyIdentifierHex,
    string DigestAlgorithm,
    string SignatureAlgorithm,
    int SignatureByteLength,
    string? SignedAttrsTlvHex,
    ExpectedSignedAttrs? SignedAttrs);

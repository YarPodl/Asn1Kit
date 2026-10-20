using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

internal static class CmsRfc4134Fixtures
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string FixturePath(string fileName) =>
        TestData.RepoPath(Path.Combine("runtime-csharp", "fixtures", "cms", "rfc4134", fileName));

    public static byte[] ReadDer(string fileName) => File.ReadAllBytes(FixturePath(fileName));

    public static ExpectedCmsDocument LoadExpected()
    {
        var json = File.ReadAllText(FixturePath("expected.json"));
        return JsonSerializer.Deserialize<ExpectedCmsDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("expected.json deserialized to null.");
    }

    public static ExpectedCmsMessage RequireMessage(string fileName)
    {
        var document = LoadExpected();
        if (!document.Messages.TryGetValue(fileName, out var message))
            throw new InvalidOperationException("expected.json missing message " + fileName);
        return message;
    }

    public static string ToHex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    public static string NormalizeIntegerHex(string hex)
    {
        var trimmed = hex.Trim().ToLowerInvariant().TrimStart('0');
        if (trimmed.Length == 0)
            return "00";
        if ((trimmed.Length & 1) == 1)
            trimmed = "0" + trimmed;
        return trimmed;
    }

    public static string IntegerHex(Asn1Integer value) => NormalizeIntegerHex(ToHex(value.Span));

    public static byte[] ParseHex(string hex)
    {
        if ((hex.Length & 1) != 0)
            throw new ArgumentException("Hex string must have even length.", nameof(hex));

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    public static string Sha1Hex(ReadOnlySpan<byte> bytes) =>
        ToHex(SHA1.HashData(bytes));

    public static SignedCms DecodeSignedCms(byte[] der, byte[]? detachedContent)
    {
        if (detachedContent is null)
        {
            var signed = new SignedCms();
            signed.Decode(der);
            return signed;
        }

        var detached = new SignedCms(new ContentInfo(detachedContent), detached: true);
        detached.Decode(der);
        return detached;
    }
}

internal sealed class ExpectedCmsDocument
{
    public string Source { get; set; } = "";
    public string? DetachedContentSha1Hex { get; set; }
    public string? DetachedContentUtf8 { get; set; }
    public Dictionary<string, ExpectedCmsMessage> Messages { get; set; } =
        new(StringComparer.Ordinal);
}

internal sealed class ExpectedCmsMessage
{
    public string Kind { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int? DataOctetLength { get; set; }
    public string? DataOctetUtf8 { get; set; }
    public string? DataOctetSha1Hex { get; set; }
    public ExpectedCmsSignedData? SignedData { get; set; }
}

internal sealed class ExpectedCmsSignedData
{
    public int Version { get; set; }
    public List<string> DigestAlgorithms { get; set; } = new();
    public string EContentType { get; set; } = "";
    public bool EContentPresent { get; set; }
    public int? EContentLength { get; set; }
    public string? EContentSha1Hex { get; set; }
    public int CertificateCount { get; set; }
    public List<ExpectedCmsCertificate> Certificates { get; set; } = new();
    public int SignerCount { get; set; }
    public List<ExpectedCmsSignerInfo> SignerInfos { get; set; } = new();
}

internal sealed class ExpectedCmsCertificate
{
    public string SerialNumberHex { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? SubjectKeyIdentifierHex { get; set; }
}

internal sealed class ExpectedCmsSignerInfo
{
    public string SidKind { get; set; } = "";
    public string? SerialNumberHex { get; set; }
    public string? SubjectKeyIdentifierHex { get; set; }
    public string DigestAlgorithm { get; set; } = "";
    public string SignatureAlgorithm { get; set; } = "";
    public int SignatureByteLength { get; set; }
    public bool SignedAttrsPresent { get; set; }
    public string? SignedAttrsTlvHex { get; set; }
    public ExpectedCmsSignedAttrs? SignedAttrs { get; set; }
}

internal sealed class ExpectedCmsSignedAttrs
{
    public List<string> AttributeOids { get; set; } = new();
    public string? ContentType { get; set; }
    public string? MessageDigestHex { get; set; }
    public string? SigningTimeUtc { get; set; }
}

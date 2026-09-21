using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

internal static class PkixFixtures
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string FixturePath(string fileName) =>
        TestData.RepoPath(Path.Combine("runtime-csharp", "fixtures", "pkix", fileName));

    public static byte[] ReadDer(string fileName) => File.ReadAllBytes(FixturePath(fileName));

    public static ExpectedDocument LoadExpected()
    {
        var json = File.ReadAllText(FixturePath("expected.json"));
        return JsonSerializer.Deserialize<ExpectedDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("expected.json deserialized to null.");
    }

    public static byte[] ParseHex(string hex)
    {
        if ((hex.Length & 1) != 0)
        {
            throw new ArgumentException("Hex string must have even length.", nameof(hex));
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return bytes;
    }

    public static string ToHex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ReadDirectoryString(Asn1Any value)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteAny(value);
        return DirectoryString.Decode(new Asn1Reader(writer.Encode(), Asn1Encoding.Der)).Value;
    }

    public static List<(string Oid, string Value)> FlattenName(List<List<AttributeTypeAndValue>> name)
    {
        var result = new List<(string, string)>();
        foreach (var rdn in name)
        {
            foreach (var atv in rdn)
            {
                result.Add((atv.Type, ReadDirectoryString(atv.Value)));
            }
        }

        return result;
    }

    public static Extension RequireExtension(IEnumerable<Extension> extensions, string oid)
    {
        return extensions.Single(e => e.ExtnID == oid);
    }

    public static Asn1Reader ExtnValueReader(Extension extension) =>
        new(extension.ExtnValue, Asn1Encoding.Der);

    public static void AssertExtnValueRoundTrip(Extension extension, Action<Asn1Writer> encode)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        encode(writer);
        Assert.Equal(ToHex(extension.ExtnValue.Span), ToHex(writer.Encode()));
    }

    public static KeyUsageFlags ParseKeyUsage(IEnumerable<string> names)
    {
        var flags = KeyUsageFlags.None;
        foreach (var name in names)
        {
            flags |= name.ToLowerInvariant() switch
            {
                "digitalsignature" => KeyUsageFlags.DigitalSignature,
                "nonrepudiation" => KeyUsageFlags.NonRepudiation,
                "keyencipherment" => KeyUsageFlags.KeyEncipherment,
                "dataencipherment" => KeyUsageFlags.DataEncipherment,
                "keyagreement" => KeyUsageFlags.KeyAgreement,
                "keycertsign" => KeyUsageFlags.KeyCertSign,
                "crlsign" => KeyUsageFlags.CRLSign,
                "encipheronly" => KeyUsageFlags.EncipherOnly,
                "decipheronly" => KeyUsageFlags.DecipherOnly,
                _ => throw new ArgumentException($"Unknown KeyUsage name '{name}'."),
            };
        }

        return flags;
    }

    public static CRLReason ParseCrlReason(string name) =>
        name.ToLowerInvariant() switch
        {
            "unspecified" => CRLReason.Unspecified,
            "keycompromise" => CRLReason.KeyCompromise,
            "cacompromise" => CRLReason.CACompromise,
            "affiliationchanged" => CRLReason.AffiliationChanged,
            "superseded" => CRLReason.Superseded,
            "cessationofoperation" => CRLReason.CessationOfOperation,
            "certificatehold" => CRLReason.CertificateHold,
            "removefromcrl" => CRLReason.RemoveFromCRL,
            "privilegewithdrawn" => CRLReason.PrivilegeWithdrawn,
            "aacompromise" => CRLReason.AACompromise,
            _ => throw new ArgumentException($"Unknown CRLReason '{name}'."),
        };
}

internal sealed class ExpectedDocument
{
    public string Source { get; set; } = "";
    public Dictionary<string, ExpectedCertificate> Certificates { get; set; } = new();
    public Dictionary<string, ExpectedCrl> Crls { get; set; } = new();
    public ExpectedGeneralNamesDocument? GeneralNames { get; set; }
}

internal sealed class ExpectedCertificate
{
    public int Asn1Version { get; set; }
    public int SerialNumber { get; set; }
    public string SignatureAlgorithm { get; set; } = "";
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset NotAfterUtc { get; set; }
    public List<ExpectedDnAttribute> Subject { get; set; } = new();
    public List<ExpectedDnAttribute> Issuer { get; set; } = new();
    public string SubjectPublicKeyAlgorithm { get; set; } = "";
    public bool SubjectPublicKeyParametersNull { get; set; }
    public int SubjectPublicKeyUnusedBits { get; set; }
    public int SubjectPublicKeyByteLength { get; set; }
    public int SignatureUnusedBits { get; set; }
    public int SignatureByteLength { get; set; }
    public List<string> ExtensionOids { get; set; } = new();
    public List<ExpectedExtension> Extensions { get; set; } = new();
}

internal sealed class ExpectedCrl
{
    public int Asn1Version { get; set; }
    public string SignatureAlgorithm { get; set; } = "";
    public DateTimeOffset ThisUpdateUtc { get; set; }
    public DateTimeOffset NextUpdateUtc { get; set; }
    public List<ExpectedDnAttribute> Issuer { get; set; } = new();
    public int SignatureUnusedBits { get; set; }
    public int SignatureByteLength { get; set; }
    public List<ExpectedRevokedCertificate> RevokedCertificates { get; set; } = new();
    public List<string> CrlExtensionOids { get; set; } = new();
    public List<ExpectedExtension> CrlExtensions { get; set; } = new();

    // Back-compat for older asserts that only listed serials.
    public List<int> RevokedSerialNumbers =>
        RevokedCertificates.Select(e => e.SerialNumber).ToList();
}

internal sealed class ExpectedRevokedCertificate
{
    public int SerialNumber { get; set; }
    public DateTimeOffset RevocationDateUtc { get; set; }
    public string? CrlReason { get; set; }
}

internal sealed class ExpectedExtension
{
    public string Oid { get; set; } = "";
    public bool Critical { get; set; }
    public string? SubjectKeyIdentifierHex { get; set; }
    public string? AuthorityKeyIdentifierHex { get; set; }
    public List<string>? KeyUsage { get; set; }
    public ExpectedBasicConstraints? BasicConstraints { get; set; }
    public List<string>? CertificatePolicies { get; set; }
    public ExpectedNameConstraints? NameConstraints { get; set; }
    public List<ExpectedGeneralName>? SubjectAltName { get; set; }
    public int? CrlNumber { get; set; }
}

internal sealed class ExpectedBasicConstraints
{
    public bool Ca { get; set; }
    public bool PathLenPresent { get; set; }
    public int? PathLen { get; set; }
}

internal sealed class ExpectedNameConstraints
{
    public List<string> PermittedDns { get; set; } = new();
    public bool ExcludedPresent { get; set; }
}

internal sealed class ExpectedGeneralName
{
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
}

internal sealed class ExpectedDnAttribute
{
    [JsonPropertyName("oid")]
    public string Oid { get; set; } = "";
    public string Value { get; set; } = "";
}

internal sealed class ExpectedGeneralNamesDocument
{
    public string Source { get; set; } = "";
    public List<ExpectedGeneralNameCase> Cases { get; set; } = new();
}

internal sealed class ExpectedGeneralNameCase
{
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? IpAddressHex { get; set; }
    public string? Oid { get; set; }
}

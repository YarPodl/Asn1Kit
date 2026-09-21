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
}

internal sealed class ExpectedDocument
{
    public string Source { get; set; } = "";
    public Dictionary<string, ExpectedCertificate> Certificates { get; set; } = new();
    public Dictionary<string, ExpectedCrl> Crls { get; set; } = new();
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
    public List<string> ExtensionOids { get; set; } = new();
}

internal sealed class ExpectedCrl
{
    public int Asn1Version { get; set; }
    public string SignatureAlgorithm { get; set; } = "";
    public DateTimeOffset ThisUpdateUtc { get; set; }
    public DateTimeOffset NextUpdateUtc { get; set; }
    public List<ExpectedDnAttribute> Issuer { get; set; } = new();
    public List<int> RevokedSerialNumbers { get; set; } = new();
    public List<string> CrlExtensionOids { get; set; } = new();
}

internal sealed class ExpectedDnAttribute
{
    [JsonPropertyName("oid")]
    public string Oid { get; set; } = "";
    public string Value { get; set; } = "";
}

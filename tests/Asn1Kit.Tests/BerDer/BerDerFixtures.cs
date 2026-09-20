using System.Globalization;
using System.Text.Json;

namespace Asn1Kit.Tests;

internal static class Hex
{
    public static byte[] Parse(string hex)
    {
        if (hex is null)
        {
            throw new ArgumentNullException(nameof(hex));
        }

        var cleaned = hex.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if ((cleaned.Length & 1) != 0)
        {
            throw new FormatException($"Hex string '{hex}' has odd length.");
        }

        return Convert.FromHexString(cleaned);
    }

    public static string Format(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);
}

public sealed class BerDerCase
{
    public string Name { get; set; } = "";
    public string Rules { get; set; } = "der";
    public string Op { get; set; } = "";
    public string Bytes { get; set; } = "";
    public JsonElement Value { get; set; }
    public bool Encode { get; set; } = true;
    public bool Reject { get; set; }
    public string? Form { get; set; }
    public int? UnusedBits { get; set; }
    public int? FractionDigits { get; set; }
    public string? Source { get; set; }

    public Asn1Kit.Runtime.Asn1Encoding Encoding =>
        string.Equals(Rules, "ber", StringComparison.OrdinalIgnoreCase)
            ? Asn1Kit.Runtime.Asn1Encoding.Ber
            : Asn1Kit.Runtime.Asn1Encoding.Der;

    public byte[] GetBytes() => Hex.Parse(Bytes);

    public bool ShouldEncode => Encode && !Reject && string.Equals(Rules, "der", StringComparison.OrdinalIgnoreCase);

    public bool HasValue => Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
}

internal static class BerDerFixtures
{
    public static IReadOnlyList<BerDerCase> Load(string relativePath)
    {
        var path = TestData.RepoPath(relativePath);
        var json = File.ReadAllText(path);
        var cases = JsonSerializer.Deserialize<List<BerDerCase>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (cases is null || cases.Count == 0)
        {
            throw new InvalidOperationException($"No cases in '{relativePath}'.");
        }

        foreach (var c in cases)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                throw new InvalidOperationException($"Case in '{relativePath}' is missing name.");
            }
        }

        return cases;
    }

    public static IEnumerable<object[]> AsTheoryData(string relativePath) =>
        Load(relativePath).Select(c => new object[] { c });

    public static string? GetString(BerDerCase c)
    {
        if (!c.HasValue)
        {
            return null;
        }

        return c.Value.ValueKind == JsonValueKind.String
            ? c.Value.GetString()
            : c.Value.GetRawText();
    }

    public static bool GetBoolean(BerDerCase c) =>
        c.Value.ValueKind == JsonValueKind.True
        || (c.Value.ValueKind == JsonValueKind.String
            && bool.Parse(c.Value.GetString()!));

    public static System.Numerics.BigInteger GetInteger(BerDerCase c)
    {
        var text = GetString(c) ?? throw new InvalidOperationException($"Case '{c.Name}' missing integer value.");
        return System.Numerics.BigInteger.Parse(text, CultureInfo.InvariantCulture);
    }

    public static byte[] GetOctetValue(BerDerCase c)
    {
        var text = GetString(c) ?? "";
        return text.Length == 0 ? Array.Empty<byte>() : Hex.Parse(text);
    }

    public static DateTimeOffset GetTime(BerDerCase c)
    {
        var text = GetString(c) ?? throw new InvalidOperationException($"Case '{c.Name}' missing time value.");
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}

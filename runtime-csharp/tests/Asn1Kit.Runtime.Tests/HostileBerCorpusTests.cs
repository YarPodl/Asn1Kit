using System.Text.Json;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

/// <summary>
/// Gate replay of the hostile-BER corpus (fuzz findings and hand-crafted limit cases).
/// Does not run the fuzzer itself.
/// </summary>
public sealed class HostileBerCorpusTests
{
    public static IEnumerable<object[]> CorpusCases() =>
        LoadCorpus().Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void BoundedDecoderFuzz_CorpusRegressions(HostileBerCase c)
    {
        var options = BuildOptions(c);
        var bytes = Hex.Parse(c.Bytes);
        var reader = new Asn1Reader(bytes, c.Encoding, options);

        if (c.Reject)
        {
            Assert.Throws<Asn1Exception>(() => Consume(reader, c));
            return;
        }

        Consume(reader, c);
        reader.ThrowIfNotEmpty();
    }

    private static void Consume(Asn1Reader reader, HostileBerCase c)
    {
        switch (c.Op)
        {
            case "nestedSequence":
                using (reader.EnterSequence(Asn1Tag.Sequence))
                using (reader.EnterSequence(Asn1Tag.Sequence))
                {
                    var value = reader.ReadInteger(Asn1Tag.Integer);
                    if (c.HasValue)
                    {
                        Assert.Equal(c.Value.GetInt32(), value);
                    }
                }
                break;
            case "octetString":
            {
                var value = reader.ReadOctetString(Asn1Tag.OctetString);
                if (c.HasValue)
                {
                    Assert.Equal(Hex.Parse(c.Value.GetString()!), value.ToArray());
                }
                break;
            }
            case "any":
                _ = reader.ReadAny();
                break;
            default:
                throw new InvalidOperationException($"Unsupported hostile-BER op '{c.Op}'.");
        }
    }

    private static Asn1ReaderOptions BuildOptions(HostileBerCase c) => new()
    {
        MaxRecursionDepth = c.MaxRecursionDepth ?? Asn1ReaderOptions.DefaultMaxRecursionDepth,
        MaxConstructedSegments = c.MaxConstructedSegments ?? Asn1ReaderOptions.DefaultMaxConstructedSegments,
        MaxMaterializedConstructedValueBytes = c.MaxMaterializedConstructedValueBytes
            ?? Asn1ReaderOptions.DefaultMaxMaterializedConstructedValueBytes
    };

    private static IReadOnlyList<HostileBerCase> LoadCorpus()
    {
        var path = TestData.RepoPath("runtime-csharp/fixtures/ber-der/hostile-ber.json");
        var json = File.ReadAllText(path);
        var cases = JsonSerializer.Deserialize<List<HostileBerCase>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        if (cases is null || cases.Count == 0)
        {
            throw new InvalidOperationException("hostile-ber.json has no cases.");
        }

        return cases;
    }
}

public sealed class HostileBerCase
{
    public string Name { get; set; } = "";
    public string Rules { get; set; } = "der";
    public string Op { get; set; } = "";
    public string Bytes { get; set; } = "";
    public JsonElement Value { get; set; }
    public bool Reject { get; set; }
    public int? MaxRecursionDepth { get; set; }
    public int? MaxConstructedSegments { get; set; }
    public int? MaxMaterializedConstructedValueBytes { get; set; }
    public string? Source { get; set; }

    public Asn1Encoding Encoding =>
        string.Equals(Rules, "ber", StringComparison.OrdinalIgnoreCase)
            ? Asn1Encoding.Ber
            : Asn1Encoding.Der;

    public bool HasValue => Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;

    public override string ToString() => Name;
}

using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class PkixGeneratedCodeTests
{
    private static readonly string GoldenDir = "runtime-csharp/generated/Asn1Kit.Pkix";

    [Fact]
    public void GenerateMatchesCommittedGoldenSources()
    {
        var document = IrSerializer.Load(TestData.RepoPath("compiler/fixtures/ir/cms-2004.json"));
        var generated = new CSharpBackend().Generate(document);
        Assert.Equal(3, generated.Count);

        foreach (var file in generated)
        {
            var goldenPath = TestData.RepoPath(Path.Combine(GoldenDir, file.RelativePath));
            Assert.True(File.Exists(goldenPath), $"Missing golden file: {goldenPath}");
            var expected = NormalizeNewlines(File.ReadAllText(goldenPath));
            var actual = NormalizeNewlines(file.Contents);
            Assert.Equal(expected, actual);
        }

        var goldenFiles = Directory.GetFiles(TestData.RepoPath(GoldenDir), "*.g.cs")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        var generatedNames = generated.Select(f => f.RelativePath).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(goldenFiles, generatedNames);
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}

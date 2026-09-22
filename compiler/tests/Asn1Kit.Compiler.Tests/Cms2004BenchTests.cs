using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class Cms2004BenchTests
{
    [Fact]
    public void CommittedBenchIr_MatchesGoldenPlusPatch()
    {
        var document = IrSerializer.Load(TestData.RepoPath("compiler/fixtures/ir/cms-2004.json"));
        IrOptionsPatch.ApplyFile(document, TestData.RepoPath("compiler/fixtures/ir/cms-2004-bench.patch.json"));

        var expected = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(expected);

        var committedPath = TestData.RepoPath("compiler/fixtures/ir/cms-2004-bench.json");
        Assert.True(File.Exists(committedPath), $"Missing bench IR: {committedPath}");
        var committed = File.ReadAllText(committedPath);
        IrSerializer.ValidateSchema(committed);
        var actual = IrSerializer.ToJson(IrSerializer.FromJson(committed));
        Assert.Equal(expected, actual);
    }
}

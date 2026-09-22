using System.Text.Json.Nodes;
using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class IrOptionsPatchTests
{
    [Fact]
    public void Apply_CmsBenchPatch_SetsNamespacesAndLazyCertificate()
    {
        var document = IrSerializer.Load(TestData.RepoPath("compiler/fixtures/ir/cms-2004.json"));
        IrOptionsPatch.ApplyFile(document, TestData.RepoPath("compiler/fixtures/ir/cms-2004-bench.patch.json"));

        Assert.Equal(
            "Asn1Kit.Pkix.Bench",
            IrOptions.CSharpNamespace(document.Modules.Single(m => m.Name == "PKIX1Explicit88").Options));
        Assert.Equal(
            "Asn1Kit.Pkix.Bench",
            IrOptions.CSharpNamespace(document.Modules.Single(m => m.Name == "PKIX1Implicit88").Options));
        Assert.Equal(
            "Asn1Kit.Cms.Bench",
            IrOptions.CSharpNamespace(
                document.Modules.Single(m => m.Name == "CryptographicMessageSyntax2004").Options));

        var cms = document.Modules.Single(m => m.Name == "CryptographicMessageSyntax2004");
        var choices = Assert.IsType<ChoiceType>(cms.Types.Single(t => t.Name == "CertificateChoices").Type);
        var certificate = choices.Components.Single(c => c.Name == "certificate");
        Assert.True(IrOptions.IsLazy(certificate.Options));
    }

    [Fact]
    public void Apply_UnknownField_Throws()
    {
        var document = IrSerializer.Load(TestData.RepoPath("compiler/fixtures/ir/cms-2004.json"));
        var patch = new JsonObject
        {
            ["fields"] = new JsonObject
            {
                ["CryptographicMessageSyntax2004.CertificateChoices.noSuchField"] = new JsonObject
                {
                    ["lazy"] = true
                }
            }
        };

        var ex = Assert.Throws<IrException>(() => IrOptionsPatch.Apply(document, patch));
        Assert.Contains("noSuchField", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UnknownModule_Throws()
    {
        var document = new IrDocument { Modules = { new IrModule { Name = "Only" } } };
        var patch = new JsonObject
        {
            ["modules"] = new JsonObject
            {
                ["Missing"] = new JsonObject { ["lazy"] = true }
            }
        };

        var ex = Assert.Throws<IrException>(() => IrOptionsPatch.Apply(document, patch));
        Assert.Contains("Missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_DeepMergesObjects_AndReplacesLeaves()
    {
        var target = IrOptions.SetCSharp(null, "namespace", "Old.Ns");
        target = IrOptions.SetCSharp(target, "typeName", "Keep");
        var patch = new JsonObject
        {
            ["csharp"] = new JsonObject { ["namespace"] = "New.Ns" },
            ["lazy"] = true
        };

        var merged = IrOptionsPatch.Merge(target, patch);
        Assert.Equal("New.Ns", IrOptions.CSharpNamespace(merged));
        Assert.Equal("Keep", IrOptions.CSharpTypeName(merged));
        Assert.True(IrOptions.IsLazy(merged));
    }

    [Fact]
    public void Generate_AfterBenchPatch_EmitsLazyCertificateAndBenchNamespaces()
    {
        var document = IrSerializer.Load(TestData.RepoPath("compiler/fixtures/ir/cms-2004.json"));
        IrOptionsPatch.ApplyFile(document, TestData.RepoPath("compiler/fixtures/ir/cms-2004-bench.patch.json"));

        var files = new CSharpBackend().Generate(document);
        var joined = string.Join('\n', files.Select(f => f.Contents));

        Assert.Contains("namespace Asn1Kit.Pkix.Bench", joined, StringComparison.Ordinal);
        Assert.Contains("namespace Asn1Kit.Cms.Bench", joined, StringComparison.Ordinal);

        var cmsFile = files.Single(f => f.RelativePath.Contains("CryptographicMessageSyntax", StringComparison.Ordinal));
        Assert.Contains(
            "Asn1Lazy<Asn1Kit.Pkix.Bench.Certificate>",
            cmsFile.Contents,
            StringComparison.Ordinal);
    }
}

using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class Cms2004Tests
{
    private static readonly string[] AsnPaths =
    {
        "compiler/fixtures/asn1/pkix1-explicit88.asn",
        "compiler/fixtures/asn1/pkix1-implicit88.asn",
        "compiler/fixtures/asn1/cms-2004.asn"
    };

    [Fact]
    public void CompilesAndMatchesGoldenIr()
    {
        var paths = AsnPaths.Select(TestData.RepoPath).ToArray();
        var goldenPath = TestData.RepoPath("compiler/fixtures/ir/cms-2004.json");
        var document = new Asn1Compiler().CompileFiles(paths);
        OpenTypeBindings.ApplyFile(document, TestData.RepoPath("compiler/fixtures/opentype/pkix-bindings.json"));
        OpenTypeBindings.ApplyFile(document, TestData.RepoPath("compiler/fixtures/opentype/cms-bindings.json"));
        ApplyCmsGoldenNamespaces(document);

        var actual = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(actual);

        var golden = File.ReadAllText(goldenPath);
        IrSerializer.ValidateSchema(golden);
        var expected = IrSerializer.ToJson(IrSerializer.FromJson(golden));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SpotChecksCmsShapesAndImports()
    {
        var document = new Asn1Compiler().CompileFiles(AsnPaths.Select(TestData.RepoPath));
        Assert.Equal(3, document.Modules.Count);

        var module = document.Modules.Single(m => m.Name == "CryptographicMessageSyntax2004");
        Assert.Equal("1.2.840.113549.1.9.16.0.24", module.Oid);
        Assert.Equal(TagDefaults.Implicit, module.TagDefault);

        var import = Assert.Single(module.Imports);
        Assert.Equal("PKIX1Explicit88", import.Module);
        Assert.Contains("Certificate", import.Types);
        Assert.Contains("CertificateList", import.Types);
        Assert.Contains("AlgorithmIdentifier", import.Types);
        Assert.Contains("CertificateSerialNumber", import.Types);
        Assert.Contains("Name", import.Types);
        Assert.Null(import.Values);

        var types = module.Types.ToDictionary(t => t.Name);
        var values = module.Values.ToDictionary(v => v.Name);

        var contentInfo = Assert.IsType<SequenceType>(types["ContentInfo"].Type);
        var content = contentInfo.Components.Single(c => c.Name == "content");
        Assert.Equal(0, content.Type.Tag!.Number);
        Assert.Equal(TagModes.Explicit, content.Type.Tag.Mode);
        var any = Assert.IsType<AnyType>(content.Type);
        Assert.Equal("contentType", any.DefinedBy);

        var signedData = Assert.IsType<SequenceType>(types["SignedData"].Type);
        Assert.Contains(signedData.Components, c => c.Name == "signerInfos");
        Assert.Contains(signedData.Components, c => c.Name == "encapContentInfo");

        var choices = Assert.IsType<ChoiceType>(types["CertificateChoices"].Type);
        Assert.Equal(
            new[] { "certificate", "extendedCertificate", "other" },
            choices.Components.Select(c => c.Name).ToArray());
        Assert.DoesNotContain(choices.Components, c => c.Name is "v1AttrCert" or "v2AttrCert");
        var other = choices.Components.Single(c => c.Name == "other");
        Assert.Equal(3, other.Type.Tag!.Number);
        Assert.Equal(TagModes.Implicit, other.Type.Tag.Mode);

        Assert.Equal(
            "1.2.840.113549.1.7.2",
            Assert.IsType<IrOidValue>(values["id-signedData"].Value).Value);

        // Same simple name in PKIX and CMS is allowed (module-scoped).
        Assert.Contains(document.Modules, m => m.Types.Any(t => t.Name == "Attribute"));
        Assert.Equal(2, document.Modules.Count(m => m.Types.Any(t => t.Name == "Attribute")));
    }

    private static void ApplyCmsGoldenNamespaces(IrDocument document)
    {
        foreach (var module in document.Modules)
        {
            var ns = module.Name == "CryptographicMessageSyntax2004"
                ? "Asn1Kit.Cms"
                : "Asn1Kit.Pkix";
            module.Options = IrOptions.SetCSharp(module.Options, "namespace", ns);
        }
    }
}

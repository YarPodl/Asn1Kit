using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class PkixImplicit88Tests
{
    private static readonly string[] AsnPaths =
    {
        "compiler/fixtures/asn1/pkix1-explicit88.asn",
        "compiler/fixtures/asn1/pkix1-implicit88.asn"
    };

    [Fact]
    public void CompilesAndMatchesGoldenIr()
    {
        var paths = AsnPaths.Select(TestData.RepoPath).ToArray();
        var goldenPath = TestData.RepoPath("compiler/fixtures/ir/pkix1-implicit88.json");
        var document = new Asn1Compiler().CompileFiles(paths);
        OpenTypeBindings.ApplyFile(document, TestData.RepoPath("compiler/fixtures/opentype/pkix-bindings.json"));
        var actual = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(actual);

        var golden = File.ReadAllText(goldenPath);
        IrSerializer.ValidateSchema(golden);
        var expected = IrSerializer.ToJson(IrSerializer.FromJson(golden));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SpotChecksPkixShapesAndImports()
    {
        var document = new Asn1Compiler().CompileFiles(AsnPaths.Select(TestData.RepoPath));
        Assert.Equal(2, document.Modules.Count);

        var module = document.Modules.Single(m => m.Name == "PKIX1Implicit88");
        Assert.Equal("1.3.6.1.5.5.7.0.19", module.Oid);
        Assert.Equal(TagDefaults.Implicit, module.TagDefault);

        var import = Assert.Single(module.Imports);
        Assert.Equal("PKIX1Explicit88", import.Module);
        Assert.Contains("Name", import.Types);
        Assert.Contains("DirectoryString", import.Types);
        Assert.Contains("id-pe", import.Values!);

        var types = module.Types.ToDictionary(t => t.Name);
        var values = module.Values.ToDictionary(v => v.Name);

        var aki = Assert.IsType<SequenceType>(types["AuthorityKeyIdentifier"].Type);
        var keyIdentifier = aki.Components.Single(c => c.Name == "keyIdentifier");
        Assert.Equal(0, keyIdentifier.Type.Tag!.Number);
        Assert.Equal(TagClasses.Context, keyIdentifier.Type.Tag.Class);
        Assert.Equal(TagModes.Implicit, keyIdentifier.Type.Tag.Mode);

        var policyQualifierId = types["PolicyQualifierId"].Type;
        Assert.IsType<OidType>(policyQualifierId);
        Assert.Contains("|", policyQualifierId.Constraint!.Unsupported);

        Assert.Equal(
            "1.3.6.1.5.5.7.3.1",
            Assert.IsType<IrOidValue>(values["id-kp-serverAuth"].Value).Value);

        var crlReason = Assert.IsType<EnumeratedType>(types["CRLReason"].Type);
        Assert.Equal(
            new (string Name, long Value)[]
            {
                ("unspecified", 0),
                ("keyCompromise", 1),
                ("cACompromise", 2),
                ("affiliationChanged", 3),
                ("superseded", 4),
                ("cessationOfOperation", 5),
                ("certificateHold", 6),
                ("removeFromCRL", 8),
                ("privilegeWithdrawn", 9),
                ("aACompromise", 10)
            },
            crlReason.Values.Select(v => (v.Name, v.Value)).ToArray());
    }
}

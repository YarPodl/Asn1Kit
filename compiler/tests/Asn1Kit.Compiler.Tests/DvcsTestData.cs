using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

internal static class DvcsTestData
{
    private static readonly Lazy<string> ValidatedDocumentJson = new(
        CompileAndSerialize,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static readonly string[] AsnPaths =
    {
        "compiler/fixtures/asn1/pkix1-explicit88.asn",
        "compiler/fixtures/asn1/pkix1-implicit88.asn",
        "compiler/fixtures/asn1/cms-2004.asn",
        "compiler/fixtures/asn1/pkcs10.asn",
        "compiler/fixtures/asn1/pkixcrmf.asn",
        "compiler/fixtures/asn1/pkixcmp.asn",
        "compiler/fixtures/asn1/ocsp.asn",
        "compiler/fixtures/asn1/ess.asn",
        "compiler/fixtures/asn1/smime-v3.asn",
        "compiler/fixtures/asn1/dvcs.asn"
    };

    public static IrDocument Compile() =>
        IrSerializer.FromJson(ValidatedDocumentJson.Value, validateSchema: false);

    private static string CompileAndSerialize()
    {
        var document = new Asn1Compiler().CompileFiles(AsnPaths.Select(TestData.RepoPath));
        OpenTypeBindings.ApplyFile(document, TestData.RepoPath("compiler/fixtures/opentype/pkix-bindings.json"));
        OpenTypeBindings.ApplyFile(document, TestData.RepoPath("compiler/fixtures/opentype/cms-bindings.json"));
        IrOptionsPatch.ApplyFile(document, TestData.RepoPath("compiler/fixtures/ir/dvcs.patch.json"));
        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        return json;
    }
}

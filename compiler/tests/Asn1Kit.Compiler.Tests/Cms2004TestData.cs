using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

internal static class Cms2004TestData
{
    public static readonly string[] AsnPaths =
    {
        "compiler/fixtures/asn1/pkix1-explicit88.asn",
        "compiler/fixtures/asn1/pkix1-implicit88.asn",
        "compiler/fixtures/asn1/cms-2004.asn"
    };

    private static readonly Lazy<string> RawJson = new(
        SerializeRaw,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> GoldenJson = new(
        SerializeGolden,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IrDocument CompileRaw() =>
        IrSerializer.FromJson(RawJson.Value, validateSchema: false);

    public static IrDocument CompileGolden() =>
        IrSerializer.FromJson(GoldenJson.Value, validateSchema: false);

    private static string SerializeRaw()
    {
        var document = new Asn1Compiler().CompileFiles(AsnPaths.Select(TestData.RepoPath));
        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        return json;
    }

    private static string SerializeGolden()
    {
        var document = IrSerializer.FromJson(RawJson.Value, validateSchema: false);
        OpenTypeBindings.ApplyFile(document, TestData.RepoPath("compiler/fixtures/opentype/pkix-bindings.json"));
        OpenTypeBindings.ApplyFile(document, TestData.RepoPath("compiler/fixtures/opentype/cms-bindings.json"));
        foreach (var module in document.Modules)
        {
            var ns = module.Name == "CryptographicMessageSyntax2004"
                ? "Asn1Kit.Cms"
                : "Asn1Kit.Pkix";
            module.Options = IrOptions.SetCSharp(module.Options, "namespace", ns);
        }

        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        return json;
    }
}

using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

internal static class PkixExplicit88TestData
{
    private static readonly Lazy<string> RawJson = new(
        SerializeRaw,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> WithBindingsJson = new(
        SerializeWithBindings,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IrDocument CompileRaw() =>
        IrSerializer.FromJson(RawJson.Value, validateSchema: false);

    public static IrDocument CompileWithBindings() =>
        IrSerializer.FromJson(WithBindingsJson.Value, validateSchema: false);

    private static string SerializeRaw()
    {
        var document = new Asn1Compiler().CompileFiles(new[]
        {
            TestData.RepoPath("compiler/fixtures/asn1/pkix1-explicit88.asn")
        });
        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        return json;
    }

    private static string SerializeWithBindings()
    {
        var document = IrSerializer.FromJson(RawJson.Value, validateSchema: false);
        OpenTypeBindings.ApplyFile(document, TestData.RepoPath("compiler/fixtures/opentype/pkix-bindings.json"));
        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        return json;
    }
}

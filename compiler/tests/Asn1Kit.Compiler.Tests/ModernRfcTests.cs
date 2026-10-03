using System.Reflection;
using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class ModernRfcTests
{
    private static readonly Lazy<IrDocument> Corpus = new(() =>
    {
        var document = new Asn1Compiler().CompileFiles(Directory.GetFiles(TestData.RepoPath("compiler/fixtures/asn1/modern"), "*.asn").OrderBy(p => p, StringComparer.Ordinal));
        foreach (var module in document.Modules)
        {
            var identifier = string.Concat(module.Name.Split('-', '_').Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
            module.Options = IrOptions.SetCSharp(module.Options, "namespace", "Asn1Kit.Modern." + identifier);
        }
        return document;
    });
    private static readonly Lazy<Assembly> Assembly = new(() => ModernAsn1Tests.CompileGenerated(new CSharpBackend().Generate(Corpus.Value).Select(f => f.Contents).ToArray()));

    [Fact]
    public void WholeRfcCorpusMatchesIrAndGeneratedGoldenSources()
    {
        Assert.Equal(35, Corpus.Value.Modules.Count);
        Assert.Equal(File.ReadAllText(TestData.RepoPath("compiler/fixtures/ir/modern-pkix-cms.json")).TrimEnd(), IrSerializer.ToJson(Corpus.Value));
        var files = new CSharpBackend().Generate(Corpus.Value);
        foreach (var file in files)
            Assert.Equal(File.ReadAllText(TestData.RepoPath("runtime-csharp/generated/Asn1Kit.Modern/" + file.RelativePath)).Replace("\r\n", "\n"), file.Contents.Replace("\r\n", "\n"));
        Assert.NotNull(Assembly.Value);
    }

    [Theory]
    [InlineData("TrustAnchorRootCertificate.crt")]
    [InlineData("ValidCertificatePathTest1EE.crt")]
    public void ModernCertificateReadsExistingExternalVectors(string file)
    {
        var bytes = File.ReadAllBytes(TestData.RepoPath("runtime-csharp/fixtures/pkix/" + file));
        var type = ResolveType("PKIX1Explicit-2009", "Certificate");
        Assert.Equal(bytes, RoundTrip(type, bytes));
    }

    [Theory]
    [InlineData("ContentInfo")]
    [InlineData("EncapsulatedContentInfo")]
    public void CmsDataRemainsOpaque(string name)
    {
        var bytes = Convert.FromHexString("301206092A864886F70D010701A0050403616263");
        var type = ResolveType("CryptographicMessageSyntax-2010", name);
        Assert.Equal(bytes, RoundTrip(type, bytes));
    }

    [Fact]
    public void RsaPssDefaultsProduceEmptyDerSequenceAndAreIndependent()
    {
        var type = ResolveType("PKIX1-PSS-OAEP-Algorithms-2009", "RSASSA-PSS-params");
        Assert.Equal(Convert.FromHexString("3000"), RoundTrip(type, Convert.FromHexString("3000")));
        var first = Activator.CreateInstance(type)!;
        var second = Activator.CreateInstance(type)!;
        Assert.NotSame(type.GetProperty("HashAlgorithm")!.GetValue(first), type.GetProperty("HashAlgorithm")!.GetValue(second));
    }

    private static Type ResolveType(string moduleName, string name)
    {
        var module = Corpus.Value.Modules.Single(m => m.Name == moduleName);
        var definition = module.Types.Single(t => t.Name == name);
        while (definition.Type is RefType reference)
        {
            module = Corpus.Value.Modules.Single(m => m.Name == (reference.Module ?? module.Name));
            definition = module.Types.Single(t => t.Name == reference.Name);
        }
        var typeName = IrOptions.CSharpTypeName(definition.Options) ?? string.Concat(definition.Name.Split('-', '_').Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
        return Assembly.Value.GetType(IrOptions.CSharpNamespace(module.Options) + "." + typeName, throwOnError: true)!;
    }

    private static byte[] RoundTrip(Type type, byte[] bytes)
    {
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var value = type.GetMethod("Decode", new[] { typeof(Asn1Reader) })!.Invoke(null, new object[] { reader });
        reader.ThrowIfNotEmpty();
        var writer = new Asn1Writer();
        type.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(value, new object[] { writer });
        return writer.Encode();
    }
}

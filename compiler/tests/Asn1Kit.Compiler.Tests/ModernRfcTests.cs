using System.Reflection;
using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;
using Asn1Kit.Runtime;
using Asn1Kit.Modern.GostR34102012PKISyntax;
using ModernCertificate = Asn1Kit.Modern.PKIX1Explicit2009.Certificate;
using ModernCertificateList = Asn1Kit.Modern.PKIX1Explicit2009.CertificateList;
using ModernAttributeCertificate = Asn1Kit.Modern.PKIXAttributeCertificate2009.AttributeCertificate;
using ModernAttributeCertificateV1 = Asn1Kit.Modern.AttributeCertificateVersion12009.AttributeCertificateV1;

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
        IrOptionsPatch.ApplyFile(document, TestData.RepoPath("compiler/fixtures/ir/modern-pkix-cms.patch.json"));
        return document;
    });

    private static readonly Assembly ModernAssembly = typeof(ModernCertificate).Assembly;

    [Fact]
    public void ModernCmsPatchRetainsIssuerAndSignedAttributes()
    {
        var module = Corpus.Value.Modules.Single(m => m.Name == "CryptographicMessageSyntax-2009");
        var issuer = Assert.IsType<SequenceType>(module.Types.Single(t => t.Name == "IssuerAndSerialNumber").Type)
            .Components.Single(c => c.Name == "issuer");
        var signedAttrs = Assert.IsType<SequenceType>(module.Types.Single(t => t.Name == "SignerInfo").Type)
            .Components.Single(c => c.Name == "signedAttrs");
        Assert.True(IrOptions.IsRetainEncoded(issuer.Options));
        Assert.True(IrOptions.IsRetainEncoded(signedAttrs.Options));
    }

    [Fact]
    public void WholeRfcCorpusMatchesIrAndGeneratedGoldenSources()
    {
        Assert.Equal(28, Corpus.Value.Modules.Count);
        Assert.DoesNotContain(Corpus.Value.Modules.SelectMany(m => m.Types),
            t => System.Text.RegularExpressions.Regex.IsMatch(t.Name, "-[0-9A-F]{16}$"));
        var algorithmModule = Corpus.Value.Modules.Single(m => m.Name == "AlgorithmInformation-2009");
        Assert.Single(algorithmModule.Types.Where(t => t.Name == "AlgorithmIdentifier"));
        Assert.Equal(4, Corpus.Value.Modules.SelectMany(m => m.Types)
            .Count(t => t.Specialization is { Name: "SIGNED" }));
        Assert.Equal(File.ReadAllText(TestData.RepoPath("compiler/fixtures/ir/modern-pkix-cms.json")).TrimEnd(), IrSerializer.ToJson(Corpus.Value));
        var files = new CSharpBackend().Generate(Corpus.Value);
        Assert.DoesNotContain(files.SelectMany(f => System.Text.RegularExpressions.Regex.Matches(f.Contents,
            @"public (?:sealed|abstract) class [A-Za-z_][A-Za-z0-9_]*[0-9A-F]{16}\b").Cast<System.Text.RegularExpressions.Match>()),
            match => match.Success);
        var goldenDir = TestData.RepoPath("runtime-csharp/generated/Asn1Kit.Modern");
        foreach (var file in files)
            Assert.Equal(File.ReadAllText(Path.Combine(goldenDir, file.RelativePath)).Replace("\r\n", "\n"), file.Contents.Replace("\r\n", "\n"));
        var goldenFiles = Directory.GetFiles(goldenDir, "*.g.cs")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        var generatedNames = files.Select(f => f.RelativePath).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(goldenFiles, generatedNames);

        var signedBase = typeof(Asn1Kit.Modern.PKIX1Explicit2009.Signed<>);
        foreach (var type in new[]
        {
            typeof(ModernCertificate),
            typeof(ModernCertificateList),
            typeof(ModernAttributeCertificate),
            typeof(ModernAttributeCertificateV1)
        })
            Assert.Equal(signedBase, type.BaseType!.GetGenericTypeDefinition());
        Assert.Null(typeof(Asn1Kit.Modern.PKIX1Explicit2009.Certificate).Assembly
            .GetType("Asn1Kit.Modern.PKIX1Explicit2009.SignedAlgorithmIdentifier"));
        Assert.Equal(
            typeof(Asn1Kit.Modern.AlgorithmInformation2009.AlgorithmIdentifier),
            signedBase.GetProperty("AlgorithmIdentifier")!.PropertyType);
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

    [Fact]
    public void GostR34102012OidCatalogMatchesRfc9215()
    {
        Assert.Equal("1.2.643.7.1", GostR34102012PKISyntaxOids.IdTc26String);
        Assert.Equal("1.2.643.7.1.1.1.1", GostR34102012PKISyntaxOids.IdTc26Gost341012256String);
        Assert.Equal("1.2.643.7.1.1.2.2", GostR34102012PKISyntaxOids.IdTc26Gost341112256String);
        Assert.Equal("1.2.643.7.1.1.3.2", GostR34102012PKISyntaxOids.IdTc26SignwithdigestGost341012256String);
        Assert.Equal("1.2.643.7.1.2.1.1.1", GostR34102012PKISyntaxOids.IdTc26Gost34102012256ParamSetAString);
        Assert.Equal("1.2.643.100.111", Asn1Kit.Modern.RuStrongCertsSyntax.RuStrongCertsSyntaxOids.IdSubjectSignToolString);
    }

    [Theory]
    [InlineData("300B06092A8503070102010101", "1.2.643.7.1.2.1.1.1", null)]
    [InlineData("301306072A85030202230006082A85030701010202", "1.2.643.2.2.35.0", "1.2.643.7.1.1.2.2")]
    public void GostR34102012PublicKeyParametersRoundTrip(string hex, string publicKeyParamSet, string? digestParamSet)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.Equal(bytes, RoundTrip(typeof(GostR34102012PublicKeyParameters), bytes));
        var value = GostR34102012PublicKeyParameters.Decode(new Asn1Reader(bytes, Asn1Encoding.Der));
        Assert.Equal(publicKeyParamSet, value.PublicKeyParamSet.ToString());
        Assert.Equal(digestParamSet, value.DigestParamSet?.ToString());
    }

    [Fact]
    public void Rfc9215GostCertificateReadsSignatureAndSpkiParameters()
    {
        var bytes = File.ReadAllBytes(TestData.RepoPath("runtime-csharp/fixtures/pkix/rfc9215-d2-tc26-256-a.crt"));
        Assert.Equal(bytes, RoundTrip(typeof(ModernCertificate), bytes));
        var certificate = ModernCertificate.Decode(new Asn1Reader(bytes, Asn1Encoding.Der));
        Assert.Equal(
            GostR34102012PKISyntaxOids.IdTc26SignwithdigestGost341012256String,
            certificate.AlgorithmIdentifier.Algorithm.ToString());
        Assert.Null(certificate.AlgorithmIdentifier.Parameters);
        var spki = certificate.ToBeSigned.SubjectPublicKeyInfo.Value;
        Assert.Equal(
            GostR34102012PKISyntaxOids.IdTc26Gost341012256String,
            spki.Algorithm.Algorithm.ToString());
        Assert.NotNull(spki.Algorithm.Parameters);
        var parameters = spki.Algorithm.Parameters!.Value.DecodeValue(
            GostR34102012PublicKeyParameters.Decode, Asn1Encoding.Der);
        Assert.Equal(
            GostR34102012PKISyntaxOids.IdTc26Gost34102012256ParamSetAString,
            parameters.PublicKeyParamSet.ToString());
        Assert.Null(parameters.DigestParamSet);
        // BIT STRING encapsulates OCTET STRING SIZE(64): tag+length+64 octets.
        Assert.Equal(0, spki.SubjectPublicKey.UnusedBits);
        Assert.Equal(66, spki.SubjectPublicKey.Memory.Length);
        Assert.Equal(0x04, spki.SubjectPublicKey.Span[0]);
        Assert.Equal(0x40, spki.SubjectPublicKey.Span[1]);
    }

    [Theory]
    [InlineData("ContentInfo")]
    [InlineData("EncapsulatedContentInfo")]
    public void CmsDataRemainsOpaque(string name)
    {
        var bytes = Convert.FromHexString("301206092A864886F70D010701A0050403616263");
        var type = ResolveType("CryptographicMessageSyntax-2009", name);
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
        return ModernAssembly.GetType(IrOptions.CSharpNamespace(module.Options) + "." + typeName, throwOnError: true)!;
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

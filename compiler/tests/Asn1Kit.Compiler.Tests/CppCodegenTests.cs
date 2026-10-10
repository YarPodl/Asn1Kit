using Asn1Kit.Codegen;
using Asn1Kit.Codegen.Cpp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;
using Xunit;

namespace Asn1Kit.Tests;

public sealed class CppCodegenTests
{
    [Fact]
    public void GenerateMatchesCommittedExampleGolden()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/example.json");
        var files = new CppBackend().Generate(document);
        var file = Assert.Single(files);
        Assert.Equal("ExampleModule.hpp", file.RelativePath);

        var expectedPath = TestData.RepoPath("compiler/fixtures/codegen-cpp/example/ExampleModule.hpp");
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var actual = file.Contents.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BackendRejectsAnyKind()
    {
        const string asn = @"
AnyModule DEFINITIONS ::= BEGIN
Payload ::= ANY
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var ex = Assert.Throws<NotSupportedException>(() => new CppBackend().Generate(document));
        Assert.Equal("any", ex.Message);
    }

    [Fact]
    public void BackendRejectsLazyOption()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/example.json");
        document.Modules[0].Types[0].Options = IrOptions.SetLazy(document.Modules[0].Types[0].Options, true);
        var ex = Assert.Throws<NotSupportedException>(() => new CppBackend().Generate(document));
        Assert.Equal("lazy", ex.Message);
    }

    [Fact]
    public void GeneratesChoiceSetOfAndOidCatalog()
    {
        const string asn = @"
CppDemo DEFINITIONS AUTOMATIC TAGS ::= BEGIN
id-demo OBJECT IDENTIFIER ::= { 1 2 3 4 }
Color ::= ENUMERATED { red(0), green(1), blue(2) }
Flags ::= BIT STRING { digitalSignature(0), nonRepudiation(1) }
Pair ::= SEQUENCE { a INTEGER, b OCTET STRING }
Bag ::= CHOICE { left INTEGER, right OCTET STRING }
BagSeq ::= SEQUENCE {
  items SEQUENCE OF Pair,
  bags SET OF Bag,
  bag Bag OPTIONAL,
  color Color DEFAULT red
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        document.Modules[0].Options = IrOptions.SetCpp(document.Modules[0].Options, "namespace", "cpp::demo");
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("struct CppDemoOids", source);
        Assert.Contains("IdDemo", source);
        Assert.Contains("enum class Color", source);
        Assert.Contains("struct Flags", source);
        Assert.Contains("FlagsFlags", source);
        Assert.Contains("enum class Bag_kind", source);
        Assert.Contains("write_set_of", source);
        Assert.Contains("read_sequence_of", source);
        Assert.Contains("std::optional", source);
        Assert.Contains("encode_Color", source);
        Assert.Contains("Color::red", source);
    }

    [Fact]
    public void CliGenerateLangCppWorks()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/example.json");
        var files = new CodeGenerator(new ILanguageBackend[] { new CppBackend() })
            .Generate(document, "cpp");
        Assert.Contains("struct Person", files.Single().Contents);
    }
}

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
    public void GeneratesBareAnyFields()
    {
        const string asn = @"
AnyModule DEFINITIONS ::= BEGIN
Payload ::= ANY
Box ::= SEQUENCE {
  required ANY,
  optional ANY OPTIONAL,
  tagged [0] ANY
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("#include \"asn1kit/any.hpp\"", source);
        Assert.Contains("asn1kit::any required{}", source);
        Assert.Contains("std::optional<asn1kit::any> optional{}", source);
        Assert.Contains("asn1kit::any tagged{}", source);
        Assert.Contains("value.required = inner.read_any();", source);
        Assert.Contains("if (!inner.eof())", source);
        Assert.Contains("inner.with_explicit(asn1kit::tag::context(0, true)", source);
        Assert.Contains("scoped.read_any();", source);
        Assert.Contains("w.write_any(this->required);", source);
        Assert.Contains("w.enter_explicit(asn1kit::tag::context(0, true))", source);
        Assert.Contains("w.write_any(this->tagged);", source);
        Assert.DoesNotContain("struct Payload", source);
    }

    [Fact]
    public void GeneratesTaggedAnyWithAutomaticTags()
    {
        const string asn = @"
AutoAny DEFINITIONS AUTOMATIC TAGS ::= BEGIN
Box ::= SEQUENCE {
  a INTEGER,
  payload ANY
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("asn1kit::any payload{}", source);
        Assert.Contains("inner.read_any(asn1kit::tag::context(1))", source);
        Assert.Contains("w.write_any(asn1kit::tag::context(1), this->payload);", source);
    }

    [Fact]
    public void GeneratesContainingOctetAndBitString()
    {
        const string asn = @"
ContainingModule DEFINITIONS AUTOMATIC TAGS ::= BEGIN
Inner ::= SEQUENCE { n INTEGER }
Box ::= SEQUENCE {
  typed OCTET STRING (CONTAINING Inner),
  inline OCTET STRING (CONTAINING SEQUENCE { n INTEGER }),
  opaque OCTET STRING (CONTAINING ANY),
  bits BIT STRING (CONTAINING Inner)
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("#include \"asn1kit/contained.hpp\"", source);
        Assert.Contains("asn1kit::contained<Inner> typed{}", source);
        Assert.Contains("struct Box_Inline_Content", source);
        Assert.Contains("asn1kit::contained<Box_Inline_Content> inline_{}", source);
        Assert.Contains("asn1kit::contained<asn1kit::any> opaque{}", source);
        Assert.Contains("asn1kit::contained<Inner> bits{}", source);
        Assert.Contains(
            "inner.read_contained<Inner>(asn1kit::tag::context(0), false, true, 0",
            source);
        Assert.Contains(
            "inner.read_contained<Box_Inline_Content>(asn1kit::tag::context(1), false, true, 0",
            source);
        Assert.Contains(
            "inner.read_contained<asn1kit::any>(asn1kit::tag::context(2), false, false, 0",
            source);
        Assert.Contains(
            "inner.read_contained<Inner>(asn1kit::tag::context(3), true, true, 0",
            source);
        Assert.Contains(
            "w.write_contained(asn1kit::tag::context(0), false, this->typed",
            source);
        Assert.Contains(
            "w.write_contained(asn1kit::tag::context(3), true, this->bits",
            source);
    }

    [Fact]
    public void GeneratesDirectOpenTypeBindings()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
  C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
  NoData ::= NULL
  emptyEntry C ::= { NoData IDENTIFIED BY {1 2 3} }
  dataEntry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 4} }
  Mixed C ::= {emptyEntry | dataEntry} Empty C ::= {emptyEntry}
  AlgorithmIdentifier{C:S} ::= SEQUENCE {
    algorithm C.&id({S}), parameters C.&T({S}{@algorithm}) OPTIONAL }
  Holder ::= SEQUENCE { mixed AlgorithmIdentifier{Mixed}, empty AlgorithmIdentifier{Empty} }
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("#include \"asn1kit/codec.hpp\"", source);
        Assert.Contains("struct AlgorithmIdentifier", source);
        Assert.Contains("struct parameters_binding", source);
        Assert.Contains("asn1kit::oid key{}", source);
        Assert.DoesNotContain("open_codec", source);
        Assert.DoesNotContain("asn1kit::codecs::", source);
        Assert.Contains("bool try_decode(const AlgorithmIdentifier& src, T& out) const", source);
        Assert.Contains("out = asn1kit::decode_open<T>(", source);
        Assert.Contains("src.parameters = asn1kit::encode_open(value);", source);
        Assert.Contains("AlgorithmIdentifier create(const T& value) const", source);
        Assert.Contains("namespace mixed_parameters_bindings", source);
        Assert.Contains("empty_entry{", source);
        Assert.Contains("data_entry{", source);
        Assert.DoesNotContain("std::function", source);
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
    public void GeneratesExtensibleSequenceSetAndChoice()
    {
        const string asn = @"
ExtModule DEFINITIONS AUTOMATIC TAGS ::= BEGIN
Box ::= SEQUENCE {
  root INTEGER,
  ...,
  [[2: mid BOOLEAN, flag NULL OPTIONAL]],
  tail OCTET STRING OPTIONAL
}
Bag ::= SET {
  a INTEGER,
  b BOOLEAN OPTIONAL,
  ...
}
Pick ::= CHOICE { left INTEGER, ... }
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("struct Box", source);
        Assert.Contains("auto next_extension =", source);
        Assert.Contains("(void)inner.read_any();", source);
        Assert.Contains("Incomplete extension group 2", source);
        Assert.Contains("Duplicate or unordered extension component", source);
        Assert.Contains("std::optional<bool> mid{}", source);
        Assert.Contains("std::optional<asn1kit::null_value> flag{}", source);

        Assert.Contains("struct Bag", source);
        Assert.Contains("enter_set_of", source);
        Assert.Contains("(void)inner.read_any();", source);

        Assert.Contains("enum class Pick_kind", source);
        Assert.Contains("unknown", source);
        Assert.Contains("from_unknown", source);
        Assert.Contains("std::optional<asn1kit::any> unknown{}", source);
        Assert.Contains("return from_unknown(r.read_any());", source);
    }

    [Fact]
    public void GenerateMatchesCommittedExtensibleGolden()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/cpp-extensible.json");
        var files = new CppBackend().Generate(document);
        var file = Assert.Single(files);
        Assert.Equal("ExtModule.hpp", file.RelativePath);

        var expectedPath = TestData.RepoPath("compiler/fixtures/codegen-cpp/extensible/ExtModule.hpp");
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var actual = file.Contents.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
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
        Assert.Contains("asn1kit::tag::context(", source);
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

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

        var expectedPath = TestData.RepoPath("runtime-cpp/generated/example/ExampleModule.hpp");
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
    public void GeneratesContainedAndSetOfOpenTypeBindings()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
  C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
  NoData ::= NULL
  emptyEntry C ::= { NoData IDENTIFIED BY {1 2 3} }
  dataEntry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 4} }
  Mixed C ::= {emptyEntry | dataEntry}
  Extension{C:S} ::= SEQUENCE {
    extnID C.&id({S}),
    extnValue OCTET STRING (CONTAINING C.&T({S}{@extnID})) }
  Attribute{C:S} ::= SEQUENCE {
    type C.&id({S}),
    values SET OF C.&T({S}{@type}) }
  Holder ::= SEQUENCE { ext Extension{Mixed}, attr Attribute{Mixed} }
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("asn1kit::contained<asn1kit::any> extnValue{}", source);
        Assert.Contains("std::vector<asn1kit::any> values{}", source);
        Assert.Contains("struct extnValue_binding", source);
        Assert.Contains("struct values_binding", source);
        Assert.Contains("out = asn1kit::decode_contained<T>(", source);
        Assert.Contains("src.extnValue = asn1kit::encode_contained(value);", source);
        Assert.Contains(
            "bool try_decode(const Attribute& src, std::vector<T>& out) const",
            source);
        Assert.Contains("out = asn1kit::decode_each<T>(", source);
        Assert.Contains("src.values = asn1kit::encode_each(value);", source);
        Assert.Contains("namespace mixed_extnValue_bindings", source);
        Assert.Contains("namespace mixed_values_bindings", source);
    }

    [Fact]
    public void GeneratesAncestorOpenTypeBindings()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
  C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
  item C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 3} }
  Items C ::= {item}
  Outer ::= SEQUENCE { id OBJECT IDENTIFIER, nested SEQUENCE OF SEQUENCE {
    value C.&T({Items}{@id}) } }
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("struct Outer_Nested_Item", source);
        Assert.Contains("struct value_binding", source);
        Assert.Contains(
            "bool try_decode(const Outer_Nested_Item& src, const Outer& parent0, T& out) const",
            source);
        Assert.Contains("if (!(parent0.id == key)) return false;", source);
        Assert.Contains(
            "void set(Outer_Nested_Item& src, const Outer& parent0, const T& value) const",
            source);
        Assert.Contains(
            "Enclosing selector does not match the open-type binding.",
            source);
        Assert.DoesNotContain("Outer_Nested_Item create(", source);
        Assert.Contains("namespace items_value_bindings", source);
    }

    [Fact]
    public void GeneratesNestedSelectorPathAndIntegerKeyBindings()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
  C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &Params, &Value } WITH SYNTAX {
    PARAMS &Params VALUE &Value IDENTIFIED BY &id }
  N ::= CLASS { &id INTEGER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
  oidItem C ::= { PARAMS NULL VALUE INTEGER (0..100) IDENTIFIED BY {1 2 3} }
  Oids C ::= {oidItem}
  intItem N ::= { NULL IDENTIFIED BY 7 }
  Ints N ::= {intItem}
  Signed ::= SEQUENCE {
    toBeSigned NULL,
    algorithmIdentifier SEQUENCE {
      algorithm OBJECT IDENTIFIER,
      parameters C.&Params({Oids}{@algorithmIdentifier.algorithm}) OPTIONAL },
    signature BIT STRING (CONTAINING C.&Value({Oids}{@algorithmIdentifier.algorithm})) }
  Attr ::= SEQUENCE {
    id INTEGER (0..256),
    value N.&T({Ints}{@id}) }
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("src.algorithmIdentifier.algorithm", source);
        Assert.Contains("parent0.algorithmIdentifier.algorithm", source);
        Assert.Contains("std::int32_t key{}", source);
        Assert.Contains("std::int32_t{7}", source);
        Assert.Contains("src.id = key;", source);
    }

    [Fact]
    public void GeneratesLazyAndRetainEncodedWrappers()
    {
        const string asn = @"
Deferred DEFINITIONS AUTOMATIC TAGS ::= BEGIN
Inner ::= SEQUENCE { n INTEGER }
Box ::= SEQUENCE {
  retained Inner,
  deferred Inner
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var box = document.Modules[0].Types.Single(t => t.Name == "Box");
        var sequence = Assert.IsType<SequenceType>(box.Type);
        sequence.Components[0].Options = IrOptions.SetRetainEncoded(null, true);
        sequence.Components[1].Options = IrOptions.SetLazy(null, true);

        var source = new CppBackend().Generate(document).Single().Contents;
        Assert.Contains("#include \"asn1kit/value.hpp\"", source);
        Assert.Contains("#include \"asn1kit/lazy.hpp\"", source);
        Assert.Contains("asn1kit::value<Inner> retained{}", source);
        Assert.Contains("asn1kit::lazy<Inner> deferred{}", source);
        Assert.Contains("read_with_original_encoding<Inner>", source);
        Assert.Contains("read_lazy<Inner>", source);
        Assert.Contains("if (this->retained.has_original_encoding())", source);
        Assert.Contains("w.write_raw(this->retained.original_encoding().span());", source);
        Assert.Contains("this->retained.get()", source);
        Assert.Contains("if (this->deferred.has_encoded())", source);
        Assert.Contains("w.write_raw(this->deferred.encoded().span());", source);
        Assert.Contains("this->deferred.get()", source);
    }

    [Fact]
    public void GenerateMatchesCommittedDeferredGolden()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/cpp-deferred.json");
        var files = new CppBackend().Generate(document);
        var file = Assert.Single(files);
        Assert.Equal("Deferred.hpp", file.RelativePath);

        var expectedPath = TestData.RepoPath("runtime-cpp/generated/deferred/Deferred.hpp");
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var actual = file.Contents.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
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

        var expectedPath = TestData.RepoPath("runtime-cpp/generated/extensible/ExtModule.hpp");
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
    public void ChoiceWithDuplicateCppPayloadTypesEmitsVariantBoxes()
    {
        const string asn = @"
CppTime DEFINITIONS AUTOMATIC TAGS ::= BEGIN
Time ::= CHOICE {
  utcTime UTCTime,
  generalTime GeneralizedTime
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        document.Modules[0].Options = IrOptions.SetCpp(document.Modules[0].Options, "namespace", "cpp::time");
        var source = new CppBackend().Generate(document).Single().Contents;

        Assert.Contains("struct Time_utcTime_box", source);
        Assert.Contains("struct Time_generalTime_box", source);
        Assert.Contains("std::variant<Time_utcTime_box, Time_generalTime_box>", source);
        Assert.Contains("std::get<0>(value).value", source);
        Assert.DoesNotContain(
            "std::variant<asn1kit::utc_date_time, asn1kit::utc_date_time>",
            source);
    }

    [Fact]
    public void CliGenerateLangCppWorks()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/example.json");
        var files = new CodeGenerator(new ILanguageBackend[] { new CppBackend() })
            .Generate(document, "cpp");
        Assert.Contains("struct Person", files.Single().Contents);
    }

    [Fact]
    public void GeneratesPeerIncludesForCrossModuleRefs()
    {
        var document = new IrDocument
        {
            IrVersion = 1,
            Modules =
            {
                new IrModule
                {
                    Name = "A",
                    Options = IrOptions.SetCpp(null, "namespace", "mod::a"),
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Item",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent { Name = "n", Type = new IntegerType() }
                                }
                            }
                        }
                    }
                },
                new IrModule
                {
                    Name = "B",
                    Options = IrOptions.SetCpp(null, "namespace", "mod::b"),
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Box",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "item",
                                        Type = new RefType { Name = "Item", Module = "A" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var files = new CppBackend().Generate(document);
        Assert.Equal(2, files.Count);
        var b = Assert.Single(files, f => f.RelativePath == "B.hpp");
        Assert.Contains("#include \"A.hpp\"", b.Contents);
        Assert.Contains("::mod::a::Item item{}", b.Contents);
        Assert.DoesNotContain(files, f => f.RelativePath == "all.hpp");
    }

    [Fact]
    public void CrossModuleSequenceOfQualifiesElementType()
    {
        var document = new IrDocument
        {
            Modules =
            {
                new IrModule
                {
                    Name = "Common",
                    Options = IrOptions.SetCpp(null, "namespace", "mod::common"),
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Extension",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "id",
                                        Type = new OidType()
                                    }
                                }
                            }
                        },
                        new IrTypeDef
                        {
                            Name = "Extensions",
                            Type = new SequenceOfType
                            {
                                Element = new RefType { Name = "Extension", Module = "Common" }
                            }
                        }
                    }
                },
                new IrModule
                {
                    Name = "Ocsp",
                    Options = IrOptions.SetCpp(null, "namespace", "mod::ocsp"),
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Request",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "exts",
                                        Type = new RefType { Name = "Extensions", Module = "Common" },
                                        Optional = true
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var ocsp = Assert.Single(new CppBackend().Generate(document), f => f.RelativePath == "Ocsp.hpp");
        Assert.Contains(
            "std::optional<std::vector<::mod::common::Extension>> exts{}",
            ocsp.Contents);
        Assert.DoesNotContain("std::vector<Extension>", ocsp.Contents);
    }

    [Fact]
    public void GenerateMatchesCommittedModernGolden()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/modern-pkix-cms.json");
        foreach (var module in document.Modules)
        {
            var identifier = string.Concat(module.Name.Split('-', '_')
                .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
            module.Options = IrOptions.SetCpp(module.Options, "namespace", "asn1kit::modern::" + identifier);
        }

        IrOptionsPatch.ApplyFile(
            document, TestData.RepoPath("compiler/fixtures/ir/modern-pkix-cms.patch.json"));

        var files = new CppBackend().Generate(document)
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(26, files.Count);
        Assert.Contains(files, f => f.RelativePath == "all.hpp");
        Assert.Contains(files, f => f.RelativePath == "certificate.hpp");
        Assert.Contains(files, f => f.RelativePath == "PKIX1Explicit2009.hpp");

        var goldenDir = TestData.RepoPath("runtime-cpp/generated/modern");
        var expectedNames = Directory.GetFiles(goldenDir, "*.hpp")
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expectedNames, files.Select(static f => f.RelativePath).ToList());

        foreach (var file in files)
        {
            var expectedPath = Path.Combine(goldenDir, file.RelativePath);
            var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal);
            var actual = file.Contents.Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Equal(expected, actual);
        }
    }
}

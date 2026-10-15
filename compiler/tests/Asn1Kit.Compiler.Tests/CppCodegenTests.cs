using Asn1Kit.Codegen;
using Asn1Kit.Codegen.Cpp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;
using Xunit;

namespace Asn1Kit.Tests;

public sealed class CppCodegenTests
{
    private static string CppSource(IrDocument document) =>
        new CppBackend().Generate(document)
            .Single(file => !file.RelativePath.EndsWith(".format.hpp", StringComparison.Ordinal))
            .Contents;

    private static string CppSource(string asn) => CppSource(new Asn1Compiler().CompileText(asn));

    private static void AssertGolden(IrDocument document, string goldenRelativeDir)
    {
        var files = new CppBackend().Generate(document)
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
        var goldenDir = TestData.RepoPath(goldenRelativeDir);
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

    [Fact]
    public void CommonAliasKeepsCrossModuleQualification()
    {
        var document = new Asn1Compiler().CompileTexts(new (string Text, string? FileName)[]
        {
            (@"Names DEFINITIONS ::= BEGIN
            A ::= CHOICE { utf8 UTF8String, printable PrintableString }
            B ::= CHOICE { utf8 UTF8String, printable PrintableString }
            END", "names.asn"),
            (@"Consumer DEFINITIONS ::= BEGIN
            IMPORTS B FROM Names;
            Holder ::= SEQUENCE { text B }
            END", "consumer.asn")
        });
        document.Modules[0].Types[1].Options = new System.Text.Json.Nodes.JsonObject { ["aliasOf"] = "A" };
        var source = new CppBackend().Generate(document).Single(f => f.RelativePath == "Consumer.hpp").Contents;
        Assert.Contains("::Names::A text{};", source);
    }

    [Fact]
    public void CommonAliasUnifiesChoiceTypes()
    {
        var document = new Asn1Compiler().CompileText(@"M DEFINITIONS ::= BEGIN
            A ::= CHOICE { utf8 UTF8String, printable PrintableString }
            B ::= CHOICE { utf8 UTF8String, printable PrintableString }
            Holder ::= SEQUENCE { first A, second B }
            END");
        document.Modules[0].Types[0].Options = IrOptions.Set(null, "cpp.typeName", System.Text.Json.Nodes.JsonValue.Create("DirectoryString")!);
        document.Modules[0].Types[1].Options = new System.Text.Json.Nodes.JsonObject { ["aliasOf"] = "A" };
        var source = CppSource(document);
        Assert.Contains("struct DirectoryString {", source);
        Assert.DoesNotContain("struct B {", source);
        Assert.Contains("DirectoryString first{};", source);
        Assert.Contains("DirectoryString second{};", source);
        Assert.Contains("std::string value{};", source);
    }

    [Fact]
    public void GenerateMatchesCommittedExampleGolden()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/example.json");
        AssertGolden(document, "runtime-cpp/generated/example");
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
        var source = CppSource(document);

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
        var source = CppSource(document);

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
        var source = CppSource(document);

        Assert.DoesNotContain("asn1kit/contained.hpp", source);
        Assert.Contains("Inner typed{}", source);
        Assert.DoesNotContain("struct Box_Inline_Content", source);
        Assert.Contains("Inner inline_{}", source);
        Assert.Contains("asn1kit::bytes opaque{}", source);
        Assert.Contains("Inner bits{}", source);
        Assert.Contains(
            "inner.read_octet_string_containing<Inner>(asn1kit::tag::context(0)",
            source);
        Assert.Contains(
            "inner.read_octet_string_containing<Inner>(asn1kit::tag::context(1)",
            source);
        Assert.Contains(
            "inner.read_octet_string(asn1kit::tag::context(2))",
            source);
        Assert.Contains(
            "inner.read_bit_string_containing<Inner>(asn1kit::tag::context(3)",
            source);
        Assert.Contains(
            "w.write_octet_string_containing(asn1kit::tag::context(0), this->typed",
            source);
        Assert.Contains(
            "w.write_bit_string_containing(asn1kit::tag::context(3), this->bits",
            source);
    }

    [Fact]
    public void GeneratesDirectOpenTypeBindings()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
  id-empty OBJECT IDENTIFIER ::= {1 2 3}
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
        var source = CppSource(document);

        Assert.Contains("#include \"asn1kit/codec.hpp\"", source);
        Assert.Contains("struct AlgorithmIdentifier", source);
        Assert.Contains("asn1kit::open_binding<", source);
        Assert.Contains("asn1kit::open_context<\"M.mixed_parameters\">", source);
        Assert.Contains("MOids::IdEmpty.get()", source);
        Assert.DoesNotContain("IdEmpty_oid", source);
        Assert.DoesNotContain("open_codec", source);
        Assert.DoesNotContain("asn1kit::codecs::", source);
        Assert.Contains("std::optional<T> try_decode(", source);
        Assert.Contains("return asn1kit::decode_open<T>(", source);
        Assert.Contains("parameters = asn1kit::encode_open(value);", source);
        Assert.Contains("static AlgorithmIdentifier create(", source);
        Assert.Contains("namespace mixed_parameters_bindings", source);
        Assert.Contains("empty_entry{", source);
        Assert.Contains("data_entry{", source);
        Assert.DoesNotContain("std::function", source);
    }

    [Fact]
    public void OpenBindingKeyUsesCrossModuleOidCatalogAndInclude()
    {
        var document = new Asn1Compiler().CompileTexts(new (string Text, string? FileName)[]
        {
            (@"Oids DEFINITIONS ::= BEGIN
id-empty OBJECT IDENTIFIER ::= { 1 2 3 }
END", "Oids.asn"),
            (@"Params DEFINITIONS ::= BEGIN
IMPORTS id-empty FROM Oids;
C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
NoData ::= NULL
emptyEntry C ::= { NoData IDENTIFIED BY id-empty }
Mixed C ::= {emptyEntry}
AlgorithmIdentifier{C:S} ::= SEQUENCE {
  algorithm C.&id({S}), parameters C.&T({S}{@algorithm}) OPTIONAL }
Holder ::= SEQUENCE { mixed AlgorithmIdentifier{Mixed} }
END", "Params.asn")
        });
        var oids = Assert.Single(document.Modules, m => m.Name == "Oids");
        var paramsModule = Assert.Single(document.Modules, m => m.Name == "Params");
        oids.Options = IrOptions.SetCpp(oids.Options, "namespace", "asn1kit::t::Oids");
        paramsModule.Options = IrOptions.SetCpp(paramsModule.Options, "namespace", "asn1kit::t::Params");
        var paramsFile = Assert.Single(new CppBackend().Generate(document), f => f.RelativePath == "Params.hpp")
            .Contents.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("#include \"Oids.hpp\"", paramsFile);
        Assert.Contains("::asn1kit::t::Oids::OidsOids::IdEmpty.get()", paramsFile);
        Assert.DoesNotContain("oid_literal<\"1.2.3\">", paramsFile);
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
        var source = CppSource(document);

        Assert.Contains("asn1kit::bytes extnValue{}", source);
        Assert.Contains("std::vector<asn1kit::any> values{}", source);
        Assert.Contains("return asn1kit::decode_contained<T>(", source);
        Assert.Contains("extnValue = asn1kit::encode_contained_octets(value);", source);
        Assert.Contains(
            "std::optional<std::vector<T>> try_decode(",
            source);
        Assert.Contains("return asn1kit::decode_each<T>(", source);
        Assert.Contains("values = asn1kit::encode_each(value);", source);
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
        var source = CppSource(document);

        Assert.Contains("struct Outer_Nested_Item", source);
        Assert.Contains(
            "std::optional<T> try_decode_nested(",
            source);
        Assert.Contains("if (!((*this).id == binding.key)) return result;", source);
        Assert.DoesNotContain("static Outer_Nested_Item create(", source);
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
        var source = CppSource(document);

        Assert.Contains("this->algorithmIdentifier.algorithm", source);
        Assert.Contains("(*this).algorithmIdentifier.algorithm", source);
        Assert.Contains("std::int32_t, asn1kit::null_value", source);
        Assert.Contains("std::int32_t{7}", source);
        Assert.Contains("this->id = binding.key;", source);
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

        var source = CppSource(document);
        Assert.Contains("#include \"asn1kit/value.hpp\"", source);
        Assert.Contains("#include \"asn1kit/lazy.hpp\"", source);
        Assert.Contains("asn1kit::value<Inner> retained{}", source);
        Assert.Contains("asn1kit::lazy<Inner> deferred{}", source);
        Assert.Contains("read_with_original_encoding<Inner>(asn1kit::tag::context(0, true))", source);
        Assert.Contains("read_lazy<Inner>(asn1kit::tag::context(1, true))", source);
        Assert.DoesNotContain("Inner item{}", source);
        Assert.DoesNotContain("[&](asn1kit::reader& deferred)", source);
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
        AssertGolden(document, "runtime-cpp/generated/deferred");
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
        var source = CppSource(document);

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
        AssertGolden(document, "runtime-cpp/generated/extensible");
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
        var source = CppSource(document);

        Assert.Contains("struct CppDemoOids", source);
        Assert.Contains("IdDemo", source);
        Assert.Contains("static constexpr auto IdDemo = asn1kit::oid_literal<\"1.2.3.4\">();", source);
        Assert.DoesNotContain("IdDemo_oid", source);
        Assert.DoesNotContain("asn1kit::oid::parse", source);
        Assert.Contains("enum class Color", source);
        Assert.Contains("struct Flags", source);
        Assert.Contains("FlagsFlags", source);
        Assert.Contains("enum class Bag_kind", source);
        Assert.Contains("static constexpr bool match_tag(const asn1kit::tag& t)", source);
        Assert.DoesNotContain("default_tag", source);
        Assert.Contains("write_set_of", source);
        Assert.Contains("read_sequence_of", source);
        // Named SEQUENCE/CHOICE OF items use type-driven helpers (no per-item lambda).
        Assert.Contains("read_sequence_of<Pair>(asn1kit::tag::context(0, true));", source);
        Assert.Contains("write_sequence_of(asn1kit::tag::context(0, true), this->items);", source);
        Assert.Contains("write_set_of(asn1kit::tag::context(1, true), this->bags);", source);
        Assert.Contains("read_set_of<Bag>(asn1kit::tag::context(1, true));", source);
        Assert.DoesNotContain("read_sequence_of<Pair>(asn1kit::tag::context(0, true), [&]", source);
        Assert.Contains("std::optional", source);
        Assert.Contains("encode_Color", source);
        Assert.Contains("Color::red", source);
        Assert.Contains("asn1kit::tag::context(", source);
        Assert.Contains("Flags(asn1kit::bit_string bits)", source);
        Assert.Contains("friend bool operator==(const Flags& a, const asn1kit::bit_string& b)", source);
    }

    [Fact]
    public void TypeDrivenSequenceOfOmitsDefaultCollectionTag()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
Pair ::= SEQUENCE { a INTEGER, b OCTET STRING }
Bag ::= CHOICE { left INTEGER, right BOOLEAN }
BagSeq ::= SEQUENCE {
  items SEQUENCE OF Pair,
  bags SET OF Bag
}
END
";
        var source = CppSource(asn);

        Assert.Contains("read_sequence_of<Pair>();", source);
        Assert.Contains("read_set_of<Bag>();", source);
        Assert.DoesNotContain("read_sequence_of<Pair>(asn1kit::tag::sequence)", source);
        Assert.DoesNotContain("read_set_of<Bag>(asn1kit::tag::set)", source);
        Assert.Contains("write_sequence_of(asn1kit::tag::sequence, this->items);", source);
        Assert.Contains("write_set_of(asn1kit::tag::set, this->bags);", source);
    }

    [Fact]
    public void TaggedChoiceNaturalCodecUsesExplicitWrapper()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
CountryName ::= [APPLICATION 1] CHOICE {
  numeric NumericString,
  printable PrintableString
}
Holder ::= SEQUENCE {
  country CountryName OPTIONAL,
  next INTEGER
}
END
";
        var source = CppSource(asn);

        Assert.Contains("return t.matches_ignore_constructed(asn1kit::tag::application(1, true));", source);
        Assert.Contains("static CountryName decode_content(asn1kit::reader& r)", source);
        Assert.Contains("return decode(r, asn1kit::tag::application(1, true));", source);
        Assert.Contains("value = decode_content(inner);", source);
        Assert.Contains("void encode_content(asn1kit::writer& w) const", source);
        Assert.Contains("encode(w, asn1kit::tag::application(1, true));", source);
        Assert.Contains("encode_content(w);", source);
        Assert.Contains("if (inner.next_matches<CountryName>())", source);
        Assert.DoesNotContain("if (inner.next_is(asn1kit::tag::sequence))", source);
        Assert.Contains("value.country = CountryName::decode(inner);", source);

        // Untagged CHOICE must not emit the content helpers (no wrapper to peel twice).
        const string untagged = @"
U DEFINITIONS ::= BEGIN
Bag ::= CHOICE { a INTEGER, b BOOLEAN }
END
";
        var untaggedSource = CppSource(untagged);
        Assert.Contains("static Bag decode(asn1kit::reader& r) {", untaggedSource);
        Assert.DoesNotContain("decode_content", untaggedSource);
        Assert.DoesNotContain("encode_content", untaggedSource);
        Assert.Contains("return decode(r);", untaggedSource);
        Assert.Contains("encode(w);", untaggedSource);
    }

    [Fact]
    public void NamedBitStringDefaultAssignsViaConvertingConstructor()
    {
        const string asn = @"
M DEFINITIONS AUTOMATIC TAGS ::= BEGIN
Flags ::= BIT STRING { a(0), b(1) }
Holder ::= SEQUENCE { flags Flags DEFAULT {a} }
END
";
        var source = CppSource(asn);
        Assert.Contains("Flags flags = asn1kit::bit_string::copy_from(", source);
        Assert.Contains("if (this->flags != asn1kit::bit_string::copy_from(", source);
        Assert.Contains("Flags(asn1kit::bit_string bits) : value(std::move(bits))", source);
    }

    [Fact]
    public void DefersPeerIncludesUntilFirstDependentType()
    {
        // Include-only triangle (no type-level SCC): A→B→C and C→A via another type.
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
                            Name = "Issuer",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent { Name = "n", Type = new IntegerType() }
                                }
                            }
                        },
                        new IrTypeDef
                        {
                            Name = "Choices",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "attr",
                                        Type = new RefType { Name = "Attr", Module = "B" }
                                    }
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
                            Name = "Attr",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "serial",
                                        Type = new RefType { Name = "Serial", Module = "C" }
                                    }
                                }
                            }
                        }
                    }
                },
                new IrModule
                {
                    Name = "C",
                    Options = IrOptions.SetCpp(null, "namespace", "mod::c"),
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Serial",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent { Name = "n", Type = new IntegerType() }
                                }
                            }
                        },
                        new IrTypeDef
                        {
                            Name = "Entity",
                            Type = new ChoiceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "issuer",
                                        Type = new RefType { Name = "Issuer", Module = "A" }
                                    },
                                    new IrComponent
                                    {
                                        Name = "other",
                                        Type = new OctetStringType()
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var files = new CppBackend().Generate(document);
        Assert.DoesNotContain(files, f => f.RelativePath.StartsWith("cycle_", StringComparison.Ordinal));
        var a = Assert.Single(files, f => f.RelativePath == "A.hpp").Contents;
        var issuerAt = a.IndexOf("struct Issuer", StringComparison.Ordinal);
        var includeBAt = a.IndexOf("#include \"B.hpp\"", StringComparison.Ordinal);
        var choicesAt = a.IndexOf("struct Choices", StringComparison.Ordinal);
        Assert.True(issuerAt >= 0 && includeBAt >= 0 && choicesAt >= 0);
        Assert.True(
            issuerAt < includeBAt,
            "Non-reentrant local types (Issuer) must complete before including reentrant peer B.");
        Assert.True(includeBAt < choicesAt, "B include must precede Choices which depends on B.Attr.");
        Assert.True(issuerAt < choicesAt, "Issuer must be emitted before Choices (reentrant-peer deferral).");
        // Empty namespace block before includes is forbidden; close-after-content then include is OK.
        Assert.DoesNotContain(
            "namespace a {\n\n} // namespace a",
            a.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratesCompileTimeOidDefault()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
Value ::= SEQUENCE { algorithm OBJECT IDENTIFIER DEFAULT { 2 999 128 } }
END
";
        var source = CppSource(asn);
        Assert.Contains("asn1kit::oid_literal<\"2.999.128\">().get()", source);
        Assert.DoesNotContain("asn1kit::oid::parse", source);
    }

    [Fact]
    public void StructuredDefaultEmitsNamedHelpersAndOidCatalog()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
id-alg OBJECT IDENTIFIER ::= { 2 999 1 }
AlgorithmIdentifier ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters NULL
}
Params ::= SEQUENCE {
  prf AlgorithmIdentifier DEFAULT { algorithm id-alg, parameters NULL }
}
END
";
        var source = CppSource(asn).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("prf = default_prf();", source);
        Assert.Contains("static AlgorithmIdentifier default_prf() {", source);
        Assert.Contains("static bool is_default_prf(const AlgorithmIdentifier& value) {", source);
        Assert.Contains("value.prf = default_prf();", source);
        Assert.Contains("if (!is_default_prf(this->prf)) {", source);
        Assert.Contains("MOids::IdAlg.get()", source);
        Assert.DoesNotContain("v{};         v.", source);
        Assert.DoesNotContain("([&]{ ", source);
    }

    [Fact]
    public void StructuredDefaultUsesCrossModuleOidCatalogAndInclude()
    {
        var document = new Asn1Compiler().CompileTexts(new (string Text, string? FileName)[]
        {
            (@"Oids DEFINITIONS ::= BEGIN
id-sha1 OBJECT IDENTIFIER ::= { 1 3 14 3 2 26 }
END", "Oids.asn"),
            (@"Params DEFINITIONS ::= BEGIN
IMPORTS id-sha1 FROM Oids;
AlgorithmIdentifier ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters NULL
}
Holder ::= SEQUENCE {
  hash AlgorithmIdentifier DEFAULT { algorithm id-sha1, parameters NULL }
}
END", "Params.asn")
        });
        var oids = Assert.Single(document.Modules, m => m.Name == "Oids");
        var paramsModule = Assert.Single(document.Modules, m => m.Name == "Params");
        oids.Options = IrOptions.SetCpp(oids.Options, "namespace", "asn1kit::t::Oids");
        paramsModule.Options = IrOptions.SetCpp(paramsModule.Options, "namespace", "asn1kit::t::Params");
        var files = new CppBackend().Generate(document);
        var paramsFile = Assert.Single(files, f => f.RelativePath == "Params.hpp").Contents
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("#include \"Oids.hpp\"", paramsFile);
        Assert.Contains("::asn1kit::t::Oids::OidsOids::IdSha1.get()", paramsFile);
        Assert.DoesNotContain("oid_literal<\"1.3.14.3.2.26\">", paramsFile);
    }

    [Fact]
    public void ChoiceWithOneCppPayloadTypeEmitsDirectValue()
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
        var source = CppSource(document);

        Assert.Contains("Time_kind kind{};", source);
        Assert.Contains("asn1kit::utc_date_time value{};", source);
        Assert.Contains("static constexpr Time from_utcTime(asn1kit::utc_date_time v)", source);
        Assert.Contains("static constexpr Time from_generalTime(asn1kit::utc_date_time v)", source);
        Assert.Contains("static constexpr bool match_tag(const asn1kit::tag& t)", source);
        Assert.Contains("const auto& alt = value;", source);
        Assert.Contains("result.value = std::move(v);", source);
        Assert.Contains("return from_utcTime(std::move(alt));", source);
        Assert.Contains("return from_generalTime(std::move(alt));", source);
        Assert.Contains("time_form::utc", source);
        Assert.Contains("time_form::generalized", source);
        Assert.DoesNotContain("_box", source);
        Assert.DoesNotContain("std::variant<", source);
        Assert.DoesNotContain("std::get<", source);
    }

    [Fact]
    public void ChoiceWithMixedPayloadTypesSharesVariantSlots()
    {
        const string asn = @"
M DEFINITIONS AUTOMATIC TAGS ::= BEGIN
Text ::= UTF8String
Pick ::= CHOICE { first Text, flag BOOLEAN, second IA5String, data OCTET STRING, ... }
END
";
        var source = CppSource(asn);
        Assert.Contains("std::variant<std::string, bool, asn1kit::bytes> value{};", source);
        Assert.DoesNotContain("_box", source);
        Assert.Contains("result.kind = Pick_kind::second;\n        result.value.emplace<0>(std::move(v));",
            source.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("case Pick_kind::second:\n            {\n                const auto& alt = std::get<0>(value);",
            source.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("return from_data(std::move(alt));", source);
        Assert.Contains("const auto& alt = std::get<2>(value);", source);
        Assert.DoesNotContain("std::get<3>", source);
        Assert.Contains("return from_unknown(r.read_any());", source);
        Assert.Contains("static Pick from_first(std::string v)", source);
        Assert.DoesNotContain("static constexpr Pick from_", source);
        Assert.Contains("static constexpr bool match_tag(const asn1kit::tag& t)", source);
    }

    [Fact]
    public void ChoiceFactoriesAreConstexprOnlyForLiteralPayloads()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
Mode ::= ENUMERATED { first(0), second(1) }
LiteralPick ::= CHOICE { flag BOOLEAN, count INTEGER (0..10), mode Mode }
OwnedPick ::= CHOICE { flag BOOLEAN, data OCTET STRING }
END
";
        var source = CppSource(asn);

        Assert.Contains("static constexpr LiteralPick from_flag(bool v)", source);
        Assert.Contains("static constexpr LiteralPick from_count(std::int32_t v)", source);
        Assert.Contains("static constexpr LiteralPick from_mode(Mode v)", source);
        Assert.Contains("static OwnedPick from_flag(bool v)", source);
        Assert.Contains("static OwnedPick from_data(asn1kit::bytes v)", source);
        Assert.DoesNotContain("static constexpr OwnedPick from_", source);
    }

    [Fact]
    public void CliGenerateLangCppWorks()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/example.json");
        var files = new CodeGenerator(new ILanguageBackend[] { new CppBackend() })
            .Generate(document, "cpp");
        Assert.Contains("struct Person", files.Single(f => f.RelativePath == "ExampleModule.hpp").Contents);
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
        Assert.Equal(4, files.Count);
        var b = Assert.Single(files, f => f.RelativePath == "B.hpp");
        Assert.Contains("#include \"A.hpp\"", b.Contents);
        Assert.Contains("::mod::a::Item item{}", b.Contents);

        var bFormat = Assert.Single(files, f => f.RelativePath == "B.format.hpp");
        Assert.Contains("#include \"A.format.hpp\"", bFormat.Contents);
        Assert.Contains("to_string(value.item, out, indent + 1);", bFormat.Contents);
        Assert.DoesNotContain(files, f => f.RelativePath.StartsWith("cycle_", StringComparison.Ordinal));
    }

    [Fact]
    public void EmitsCycleAmalgamForCrossModuleTypeCycle()
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
                            Name = "Foo",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "bar",
                                        Type = new RefType { Name = "Bar", Module = "B" }
                                    }
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
                            Name = "Bar",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "foo",
                                        Type = new RefType { Name = "Foo", Module = "A" },
                                        Optional = true
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var files = new CppBackend().Generate(document)
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
        var cycle = Assert.Single(files, f => f.RelativePath.StartsWith("cycle_", StringComparison.Ordinal));
        Assert.Equal("cycle_A_Foo_B_Bar.hpp", cycle.RelativePath);
        Assert.Contains("struct Foo", cycle.Contents);
        Assert.Contains("struct Bar", cycle.Contents);
        Assert.DoesNotContain("#include \"A.hpp\"", cycle.Contents);
        Assert.DoesNotContain("#include \"B.hpp\"", cycle.Contents);
        Assert.DoesNotContain("#include \"A.types.hpp\"", cycle.Contents);
        Assert.DoesNotContain("#include \"B.types.hpp\"", cycle.Contents);
        Assert.DoesNotContain(files, f => f.RelativePath == "A.types.hpp");
        Assert.DoesNotContain(files, f => f.RelativePath == "B.types.hpp");
        Assert.DoesNotContain("struct Foo", Assert.Single(files, f => f.RelativePath == "A.hpp").Contents);
        Assert.DoesNotContain("struct Bar", Assert.Single(files, f => f.RelativePath == "B.hpp").Contents);
        Assert.Contains("#include \"cycle_A_Foo_B_Bar.hpp\"", Assert.Single(files, f => f.RelativePath == "A.hpp").Contents);
        Assert.Contains("#include \"cycle_A_Foo_B_Bar.hpp\"", Assert.Single(files, f => f.RelativePath == "B.hpp").Contents);
        Assert.DoesNotContain(files, f => f.RelativePath == "all.hpp");
        Assert.DoesNotContain(files, f => f.RelativePath == "certificate.hpp");
    }

    [Fact]
    public void OpenTypeBindingsDoNotCreateLayoutCycles()
    {
        var document = new IrDocument
        {
            IrVersion = 1,
            Modules =
            {
                new IrModule
                {
                    Name = "Cms",
                    Options = IrOptions.SetCpp(null, "namespace", "mod::cms"),
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "EncapsulatedContentInfo",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent { Name = "eContentType", Type = new OidType() },
                                    new IrComponent
                                    {
                                        Name = "eContent",
                                        Optional = true,
                                        Type = new OctetStringType
                                        {
                                            Containing = new AnyType
                                            {
                                                Selector = new IrOpenTypeSelector
                                                {
                                                    Levels = 0,
                                                    Path = { "eContentType" }
                                                },
                                                Bindings = new List<IrOpenTypeBinding>
                                                {
                                                    new IrOpenTypeBinding
                                                    {
                                                        Key = "1.2.840.113549.1.7.2",
                                                        Name = "ct-SignedData",
                                                        Type = new RefType
                                                        {
                                                            Name = "SignedData",
                                                            Module = "Cms"
                                                        }
                                                    }
                                                },
                                                Table = "ContentSet",
                                                TableExtensible = true
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        new IrTypeDef
                        {
                            Name = "SignedData",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "encapContentInfo",
                                        Type = new RefType
                                        {
                                            Name = "EncapsulatedContentInfo",
                                            Module = "Cms"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var files = new CppBackend().Generate(document);
        Assert.DoesNotContain(files, f => f.RelativePath.StartsWith("cycle_", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.RelativePath.EndsWith(".types.hpp", StringComparison.Ordinal));
        var cms = Assert.Single(files, f => f.RelativePath == "Cms.hpp");
        Assert.Contains("struct EncapsulatedContentInfo", cms.Contents);
        Assert.Contains("struct SignedData", cms.Contents);
        Assert.Contains("std::optional<asn1kit::bytes> eContent{}", cms.Contents);
    }

    [Fact]
    public void CycleAmalgamIncludesTypesFileForLeafDepsOnly()
    {
        var document = new IrDocument
        {
            IrVersion = 1,
            Modules =
            {
                new IrModule
                {
                    Name = "C",
                    Options = IrOptions.SetCpp(null, "namespace", "mod::c"),
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Leaf",
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
                    Name = "A",
                    Options = IrOptions.SetCpp(null, "namespace", "mod::a"),
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Foo",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "bar",
                                        Type = new RefType { Name = "Bar", Module = "B" }
                                    },
                                    new IrComponent
                                    {
                                        Name = "leaf",
                                        Type = new RefType { Name = "Leaf", Module = "C" }
                                    }
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
                            Name = "Bar",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "foo",
                                        Type = new RefType { Name = "Foo", Module = "A" },
                                        Optional = true
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var files = new CppBackend().Generate(document)
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
        var cycle = Assert.Single(files, f => f.RelativePath.StartsWith("cycle_", StringComparison.Ordinal));
        Assert.Equal("cycle_A_Foo_B_Bar.hpp", cycle.RelativePath);
        // Leaf module has no facade content → single C.hpp (no C.types.hpp).
        Assert.Contains("#include \"C.hpp\"", cycle.Contents);
        Assert.DoesNotContain("#include \"C.types.hpp\"", cycle.Contents);
        Assert.DoesNotContain("struct Leaf", cycle.Contents);
        var leaf = Assert.Single(files, f => f.RelativePath == "C.hpp");
        Assert.Contains("struct Leaf", leaf.Contents);
        Assert.DoesNotContain(files, f => f.RelativePath.EndsWith(".types.hpp", StringComparison.Ordinal));
    }

    [Fact]
    public void OidCatalogLivesInTypesFileWhenModuleHasFacade()
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
                    Values =
                    {
                        new IrValueDef
                        {
                            Name = "id-leaf",
                            Type = new OidType(),
                            Value = new IrOidValue { Value = "1.2.3" }
                        }
                    },
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Foo",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "bar",
                                        Type = new RefType { Name = "Bar", Module = "B" }
                                    },
                                    new IrComponent
                                    {
                                        Name = "n",
                                        Type = new IntegerType()
                                    }
                                }
                            }
                        },
                        new IrTypeDef
                        {
                            Name = "Helper",
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
                            Name = "Bar",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "foo",
                                        Type = new RefType { Name = "Foo", Module = "A" },
                                        Optional = true
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var files = new CppBackend().Generate(document);
        // Foo is in cycle; Helper is core + module needs facade → A.types.hpp holds Helper + OIDs.
        var types = Assert.Single(files, f => f.RelativePath == "A.types.hpp");
        Assert.Contains("struct Helper", types.Contents);
        Assert.Contains("struct AOids", types.Contents);
        Assert.Contains("IdLeaf", types.Contents);
        var facade = Assert.Single(files, f => f.RelativePath == "A.hpp");
        Assert.Contains("#include \"A.types.hpp\"", facade.Contents);
        Assert.DoesNotContain("struct AOids", facade.Contents);
        Assert.DoesNotContain("struct Helper", facade.Contents);
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

    private static IrDocument LoadModernPkixCms()
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
        return document;
    }

    [Fact]
    public void ModernCrossModuleNamesAreQualified()
    {
        var files = new CppBackend().Generate(LoadModernPkixCms())
            .ToDictionary(f => f.RelativePath, f => f.Contents, StringComparer.Ordinal);

        // Synthesized nested owner referenced from another module's OID catalog.
        Assert.Contains(
            "std::vector<::asn1kit::modern::PKIX1Implicit2009::PolicyMappings_Item>",
            files["PKIXCommonTypes2009.hpp"]);
        // Collapsible-alias named type resolved into a different module.
        Assert.Contains(
            "::asn1kit::modern::PKIXCRMF2009::CertId>",
            files["PKIXCommonTypes2009.hpp"]);
        Assert.Contains(
            "::asn1kit::modern::PKIXCRMF2009::CertRequest>",
            files["PKIXCommonTypes2009.hpp"]);
        // Cross-module ENUMERATED codec helper names.
        Assert.Contains(
            "::asn1kit::modern::PKIX1Implicit2009::decode_CRLReason(",
            files["OCSP2009.types.hpp"]);
        Assert.Contains(
            "::asn1kit::modern::PKIX1Implicit2009::encode_CRLReason(",
            files["EnrollmentMessageSyntax2009.types.hpp"]);

        Assert.Contains(
            "ExtensionAttribute_EaTeletexPersonalName>",
            files["PKIXX400Address2009.hpp"]);
        Assert.Contains(
            "std::optional<std::vector<T>> try_decode_signed_attrs(",
            files["CryptographicMessageSyntax2009.types.hpp"]);
        Assert.Contains(
            "open_context<\"CryptographicMessageSyntax-2009.signedAttributesSet\">",
            files["CryptographicMessageSyntax2009.types.hpp"]);
        Assert.DoesNotContain(
            "try_decode_revoked_certificates",
            files["PKIX1Explicit2009.types.hpp"]);
        Assert.Contains(
            "try_decode_issuer",
            files["PKIX1Explicit2009.types.hpp"]);
        Assert.Contains(
            "try_decode_extensions",
            files["EnrollmentMessageSyntax2009.types.hpp"]);
        Assert.Contains(
            "try_decode_attrs",
            files["PKIXAttributeCertificate2009.types.hpp"]);
        Assert.Contains(
            "return asn1kit::try_decode_unique(",
            files["CryptographicMessageSyntax2009.types.hpp"]);
        Assert.DoesNotContain(
            "Multiple values match open-type contextual decode.",
            files["CryptographicMessageSyntax2009.types.hpp"]);
        Assert.Contains(
            "struct PKIMessage;",
            files["cycle_PKIXCMP2009_PKIBody_PKIXCMP2009_PKIMessage.hpp"]);
    }

    [Fact]
    public void GenerateMatchesCommittedModernGolden()
    {
        var document = LoadModernPkixCms();
        var files = new CppBackend().Generate(document)
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
        Assert.Contains(files, f => f.RelativePath == "PKIX1Explicit2009.hpp");
        Assert.Contains(files, f => f.RelativePath.StartsWith("cycle_", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.RelativePath == "all.hpp");
        Assert.DoesNotContain(files, f => f.RelativePath == "certificate.hpp");
        var explicitTypes = files.Single(f => f.RelativePath == "PKIX1Explicit2009.types.hpp").Contents;
        Assert.DoesNotContain("Certificate_AlgorithmIdentifier", explicitTypes, StringComparison.Ordinal);
        Assert.Contains(
            "AlgorithmInformation2009::AlgorithmIdentifier algorithmIdentifier",
            explicitTypes,
            StringComparison.Ordinal);

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

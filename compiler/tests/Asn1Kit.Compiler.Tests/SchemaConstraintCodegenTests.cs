using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Codegen.Cpp;
using Xunit;

namespace Asn1Kit.Tests;

public sealed class SchemaConstraintCodegenTests
{
    [Fact]
    public void CSharp_EmitsSchemaConstraintComments()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/schema-constraints.json");
        var source = string.Concat(new CSharpBackend().Generate(document).Select(f => f.Contents));

        Assert.Contains(
            "/// <summary>ASN.1 alias OGRN ::= NumericString. ASN.1 SIZE (13). Not enforced by runtime.</summary>",
            source);
        Assert.Contains(
            "/// <summary>ASN.1 alias SignedAttrs ::= SET OF INTEGER. ASN.1 SIZE (1..MAX). Not enforced by runtime.</summary>",
            source);
        Assert.Contains(
            "/// <summary>ASN.1 alias BodyPartID ::= INTEGER. ASN.1 value range (0..255). Not enforced by runtime.</summary>",
            source);
        Assert.Contains(
            "/// <summary>ASN.1 alias PolicyQualifierId ::= OBJECT IDENTIFIER. ASN.1 constraint unsupported: (id-qt-cps | id-qt-unotice). Not enforced by runtime.</summary>",
            source);
        Assert.Contains(
            "/// <summary>ASN.1 SIZE (1..200). Not enforced by runtime.</summary>",
            source);
        Assert.Contains(
            "/// <summary>ASN.1 object set ClosedSet (closed). Membership not enforced by runtime.</summary>",
            source);
        Assert.Contains(
            "/// <summary>ASN.1 object set OpenSet (extensible). Membership not enforced by runtime.</summary>",
            source);
    }

    [Fact]
    public void Cpp_EmitsSchemaConstraintComments()
    {
        var document = TestData.LoadIr("compiler/fixtures/ir/schema-constraints.json");
        var source = new CppBackend().Generate(document)
            .Single(f => f.RelativePath == "SchemaConstraints.hpp")
            .Contents;

        Assert.Contains(
            "// ASN.1 alias OGRN ::= NumericString. ASN.1 SIZE (13). Not enforced by runtime.",
            source);
        Assert.Contains(
            "// ASN.1 alias SignedAttrs ::= SET OF INTEGER. ASN.1 SIZE (1..MAX). Not enforced by runtime.",
            source);
        Assert.Contains(
            "// ASN.1 alias BodyPartID ::= INTEGER. ASN.1 value range (0..255). Not enforced by runtime.",
            source);
        Assert.Contains(
            "// ASN.1 alias PolicyQualifierId ::= OBJECT IDENTIFIER. ASN.1 constraint unsupported: (id-qt-cps | id-qt-unotice). Not enforced by runtime.",
            source);
        Assert.Contains(
            "// ASN.1 SIZE (1..200). Not enforced by runtime.",
            source);
        Assert.Contains(
            "// ASN.1 object set ClosedSet (closed). Membership not enforced by runtime.",
            source);
        Assert.Contains(
            "// ASN.1 object set OpenSet (extensible). Membership not enforced by runtime.",
            source);
    }
}

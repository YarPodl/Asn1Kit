using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class InlineCompositeDeduperTests
{
    [Fact]
    public void CollapsesInlineSequenceMatchingNamedTypedefOntoRefWithOpenTypes()
    {
        const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
ALG ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &Params OPTIONAL } WITH SYNTAX { [&Params] IDENTIFIED BY &id }
a-null ALG ::= { NULL IDENTIFIED BY {1 2 3} }
Algs ALG ::= { a-null, ... }
AlgorithmIdentifier{ALG:Set} ::= SEQUENCE {
  algorithm ALG.&id({Set}),
  parameters ALG.&Params({Set}{@algorithm}) OPTIONAL
}
Shared ::= AlgorithmIdentifier{Algs}
Wrapper ::= SEQUENCE {
  algorithmIdentifier SEQUENCE {
    algorithm ALG.&id({Algs}),
    parameters ALG.&Params({Algs}{@algorithmIdentifier.algorithm}) OPTIONAL
  }
}
END";
        var document = new Asn1Compiler().CompileText(asn);
        var module = Assert.Single(document.Modules);
        Assert.Contains(module.Types, t => t.Name == "Shared");
        var wrapper = module.Types.Single(t => t.Name == "Wrapper");
        var sequence = Assert.IsType<SequenceType>(wrapper.Type);
        var field = Assert.Single(sequence.Components);
        Assert.Equal("algorithmIdentifier", field.Name);
        var reference = Assert.IsType<RefType>(field.Type);
        Assert.Equal("Shared", reference.Name);
        var use = Assert.Single(reference.OpenTypes!);
        Assert.Equal(new[] { "parameters" }, use.Path);
        Assert.Equal("1.2.3", Assert.Single(use.Bindings).Key);
        Assert.True(use.TableExtensible);
    }

    [Fact]
    public void KeepsInlineSequenceWhenOptionalityDiffers()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
Named ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters ANY OPTIONAL
}
Different ::= SEQUENCE {
  algorithmIdentifier SEQUENCE {
    algorithm OBJECT IDENTIFIER,
    parameters ANY
  }
}
END";
        var document = new Asn1Compiler().CompileText(asn);
        var module = Assert.Single(document.Modules);
        var different = module.Types.Single(t => t.Name == "Different");
        var field = Assert.Single(Assert.IsType<SequenceType>(different.Type).Components);
        Assert.IsType<SequenceType>(field.Type);
    }
}

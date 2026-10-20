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

    [Theory]
    [InlineData("lazy")]
    [InlineData("retainEncoded")]
    public void KeepsInlineSequenceWhenNestedFieldOptionDiffers(string option)
    {
        var namedField = new IrComponent { Name = "n", Type = new IntegerType() };
        var inlineField = new IrComponent { Name = "n", Type = new IntegerType() };
        namedField.Options = option switch
        {
            "lazy" => IrOptions.SetLazy(null, true),
            "retainEncoded" => IrOptions.SetRetainEncoded(null, true),
            _ => throw new InvalidOperationException(option)
        };

        var document = DocumentWithNamedAndInline(namedField, inlineField);
        InlineCompositeDeduper.Dedup(document);

        var body = Assert.Single(Assert.IsType<SequenceType>(
            document.Modules[0].Types.Single(t => t.Name == "Wrapper").Type).Components);
        Assert.IsType<SequenceType>(body.Type);
    }

    [Fact]
    public void KeepsInlineSequenceWhenIntegerRepresentationDiffers()
    {
        var namedField = new IrComponent
        {
            Name = "n",
            Type = new IntegerType
            {
                Options = IrOptions.SetIntegerRepresentation(null, IrOptions.IntegerRepresentations.Der)
            }
        };
        var inlineField = new IrComponent
        {
            Name = "n",
            Type = new IntegerType
            {
                Options = IrOptions.SetIntegerRepresentation(null, IrOptions.IntegerRepresentations.Int32)
            }
        };

        var document = DocumentWithNamedAndInline(namedField, inlineField);
        InlineCompositeDeduper.Dedup(document);

        var body = Assert.Single(Assert.IsType<SequenceType>(
            document.Modules[0].Types.Single(t => t.Name == "Wrapper").Type).Components);
        Assert.IsType<SequenceType>(body.Type);
    }

    [Fact]
    public void CollapsesInlineSequenceWhenNestedFieldOptionsMatch()
    {
        var namedField = new IrComponent
        {
            Name = "n",
            Type = new IntegerType(),
            Options = IrOptions.SetLazy(null, true)
        };
        var inlineField = new IrComponent
        {
            Name = "n",
            Type = new IntegerType(),
            Options = IrOptions.SetLazy(null, true)
        };

        var document = DocumentWithNamedAndInline(namedField, inlineField);
        InlineCompositeDeduper.Dedup(document);

        var body = Assert.Single(Assert.IsType<SequenceType>(
            document.Modules[0].Types.Single(t => t.Name == "Wrapper").Type).Components);
        var reference = Assert.IsType<RefType>(body.Type);
        Assert.Equal("Named", reference.Name);
    }

    [Fact]
    public void CollapsesInlineSequenceWhenBothSidesHaveNoOptions()
    {
        var document = DocumentWithNamedAndInline(
            new IrComponent { Name = "n", Type = new IntegerType() },
            new IrComponent { Name = "n", Type = new IntegerType() });
        InlineCompositeDeduper.Dedup(document);

        var body = Assert.Single(Assert.IsType<SequenceType>(
            document.Modules[0].Types.Single(t => t.Name == "Wrapper").Type).Components);
        var reference = Assert.IsType<RefType>(body.Type);
        Assert.Equal("Named", reference.Name);
    }

    private static IrDocument DocumentWithNamedAndInline(IrComponent namedField, IrComponent inlineField) =>
        new()
        {
            IrVersion = 1,
            Modules =
            {
                new IrModule
                {
                    Name = "M",
                    TagDefault = TagDefaults.Explicit,
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Named",
                            Type = new SequenceType { Components = { namedField } }
                        },
                        new IrTypeDef
                        {
                            Name = "Wrapper",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent
                                    {
                                        Name = "body",
                                        Type = new SequenceType { Components = { inlineField } }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
}

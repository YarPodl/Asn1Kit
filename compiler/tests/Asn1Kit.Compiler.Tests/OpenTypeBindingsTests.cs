using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class OpenTypeBindingsTests
{
    [Fact]
    public void AnyBindings_RoundTripThroughSerializer()
    {
        var document = new IrDocument
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
                            Name = "Alg",
                            Type = new SequenceType
                            {
                                Components =
                                {
                                    new IrComponent { Name = "algorithm", Type = new OidType() },
                                    new IrComponent
                                    {
                                        Name = "parameters",
                                        Optional = true,
                                        Type = new AnyType
                                        {
                                            DefinedBy = "algorithm",
                                            Bindings = new List<IrOpenTypeBinding>
                                            {
                                                new IrOpenTypeBinding
                                                {
                                                    Key = "1.2.840.113549.1.1.11",
                                                    Type = new NullType()
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        var again = IrSerializer.FromJson(json);
        var parameters = Assert.IsType<SequenceType>(again.Modules[0].Types[0].Type)
            .Components.Single(c => c.Name == "parameters");
        var any = Assert.IsType<AnyType>(parameters.Type);
        Assert.Equal("algorithm", any.DefinedBy);
        var binding = Assert.Single(any.Bindings!);
        Assert.Equal("1.2.840.113549.1.1.11", binding.Key);
        Assert.IsType<NullType>(binding.Type);
    }

    [Fact]
    public void Validator_RejectsDuplicateBindingKeys()
    {
        var document = MinimalAlgDocument();
        var any = (AnyType)((SequenceType)document.Modules[0].Types[0].Type)
            .Components.Single(c => c.Name == "parameters").Type;
        any.Bindings = new List<IrOpenTypeBinding>
        {
            new() { Key = "1.2.3", Type = new NullType() },
            new() { Key = "1.2.3", Type = new NullType() }
        };

        var ex = Assert.Throws<IrException>(() => IrValidator.Validate(document));
        Assert.Contains("duplicated", ex.Message);
    }

    [Fact]
    public void Validator_RejectsBindingsWithoutDefinedBy()
    {
        var document = MinimalAlgDocument();
        var any = (AnyType)((SequenceType)document.Modules[0].Types[0].Type)
            .Components.Single(c => c.Name == "parameters").Type;
        any.DefinedBy = null;
        any.Bindings = new List<IrOpenTypeBinding>
        {
            new() { Key = "1.2.3", Type = new NullType() }
        };

        var ex = Assert.Throws<IrException>(() => IrValidator.Validate(document));
        Assert.Contains("definedBy", ex.Message);
    }

    [Fact]
    public void Overlay_AppliesBindingsToCompiledAny()
    {
        const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
Alg ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters ANY DEFINED BY algorithm OPTIONAL
}
END
";
        var document = new Asn1Kit.Compiler.Asn1Compiler().CompileText(asn);
        OpenTypeBindings.ApplyJson(document, @"{
  ""M.Alg.parameters"": [
    { ""key"": ""1.2.840.113549.1.1.11"", ""type"": { ""kind"": ""null"" } }
  ]
}");

        var any = Assert.IsType<AnyType>(
            Assert.IsType<SequenceType>(document.Modules[0].Types[0].Type)
                .Components.Single(c => c.Name == "parameters").Type);
        Assert.Single(any.Bindings!);
        Assert.IsType<NullType>(any.Bindings![0].Type);
    }

    [Fact]
    public void Overlay_RejectsUnknownPath()
    {
        const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
Alg ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters ANY DEFINED BY algorithm OPTIONAL
}
END
";
        var document = new Asn1Kit.Compiler.Asn1Compiler().CompileText(asn);
        var ex = Assert.Throws<IrException>(() => OpenTypeBindings.ApplyJson(document, @"{
  ""M.Missing.parameters"": [
    { ""key"": ""1.2.3"", ""type"": { ""kind"": ""null"" } }
  ]
}"));
        Assert.Contains("not found", ex.Message);
    }

    private static IrDocument MinimalAlgDocument() => new()
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
                        Name = "Alg",
                        Type = new SequenceType
                        {
                            Components =
                            {
                                new IrComponent { Name = "algorithm", Type = new OidType() },
                                new IrComponent
                                {
                                    Name = "parameters",
                                    Optional = true,
                                    Type = new AnyType { DefinedBy = "algorithm" }
                                }
                            }
                        }
                    }
                }
            }
        }
    };
}

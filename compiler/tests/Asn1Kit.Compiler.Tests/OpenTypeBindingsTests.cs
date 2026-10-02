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
                                                    Name = "Sha256WithRsaParameters",
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
        Assert.Equal("Sha256WithRsaParameters", binding.Name);
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
    public void Validator_RejectsEmptyBindingName()
    {
        var document = MinimalAlgDocument();
        var any = (AnyType)((SequenceType)document.Modules[0].Types[0].Type)
            .Components.Single(c => c.Name == "parameters").Type;
        any.Bindings = new List<IrOpenTypeBinding>
        {
            new() { Key = "1.2.3", Name = " ", Type = new NullType() }
        };

        var ex = Assert.Throws<IrException>(() => IrValidator.Validate(document));
        Assert.Contains("empty name", ex.Message);
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
    public void Overlay_LocalizesAliasedAnyAndSuppliesDefinedBy()
    {
        const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
AttributeValue ::= ANY
AttributeTypeAndValue ::= SEQUENCE {
  type OBJECT IDENTIFIER,
  value AttributeValue
}
Attribute ::= SEQUENCE {
  type OBJECT IDENTIFIER,
  values SET OF AttributeValue
}
END
";
        var document = new Asn1Kit.Compiler.Asn1Compiler().CompileText(asn);
        OpenTypeBindings.ApplyJson(document, @"{
  ""M.AttributeTypeAndValue.value"": {
    ""definedBy"": ""type"",
    ""bindings"": [
      { ""key"": ""2.5.4.3"", ""type"": { ""kind"": ""string"", ""stringType"": ""utf8"" } }
    ]
  }
}");

        var module = Assert.Single(document.Modules);
        var types = module.Types.ToDictionary(type => type.Name);
        var attributeValue = Assert.IsType<AnyType>(types["AttributeValue"].Type);
        Assert.Null(attributeValue.DefinedBy);
        Assert.Null(attributeValue.Bindings);

        var atv = Assert.IsType<SequenceType>(types["AttributeTypeAndValue"].Type);
        var localized = Assert.IsType<AnyType>(atv.Components.Single(c => c.Name == "value").Type);
        Assert.Equal("type", localized.DefinedBy);
        var binding = Assert.Single(localized.Bindings!);
        Assert.Equal("2.5.4.3", binding.Key);
        Assert.Equal(StringTypes.Utf8, Assert.IsType<StringType>(binding.Type).Form);

        var attribute = Assert.IsType<SequenceType>(types["Attribute"].Type);
        var values = Assert.IsType<SetOfType>(attribute.Components.Single(c => c.Name == "values").Type);
        var untouchedReference = Assert.IsType<RefType>(values.Element);
        Assert.Equal("AttributeValue", untouchedReference.Name);
    }

    [Fact]
    public void Overlay_ResolvesImportedAnyAlias()
    {
        const string valuesAsn = @"
Values DEFINITIONS EXPLICIT TAGS ::= BEGIN
AttributeValue ::= ANY
END
";
        const string namesAsn = @"
Names DEFINITIONS EXPLICIT TAGS ::= BEGIN
IMPORTS AttributeValue FROM Values;
AttributeTypeAndValue ::= SEQUENCE {
  type OBJECT IDENTIFIER,
  value AttributeValue
}
END
";
        var document = new Asn1Kit.Compiler.Asn1Compiler().CompileTexts(
            new (string Text, string? FileName)[]
            {
                (valuesAsn, "values.asn"),
                (namesAsn, "names.asn")
            });

        OpenTypeBindings.ApplyJson(document, @"{
  ""Names.AttributeTypeAndValue.value"": {
    ""definedBy"": ""type"",
    ""bindings"": [
      { ""key"": ""2.5.4.3"", ""type"": { ""kind"": ""string"", ""stringType"": ""utf8"" } }
    ]
  }
}");

        var names = document.Modules.Single(module => module.Name == "Names");
        var atv = Assert.IsType<SequenceType>(
            names.Types.Single(type => type.Name == "AttributeTypeAndValue").Type);
        var localized = Assert.IsType<AnyType>(atv.Components.Single(c => c.Name == "value").Type);
        Assert.Equal("type", localized.DefinedBy);
        Assert.Single(localized.Bindings!);
    }

    [Fact]
    public void Overlay_AliasedAnyRequiresDefinedByInObjectForm()
    {
        const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
AttributeValue ::= ANY
AttributeTypeAndValue ::= SEQUENCE {
  type OBJECT IDENTIFIER,
  value AttributeValue
}
END
";
        var document = new Asn1Kit.Compiler.Asn1Compiler().CompileText(asn);

        var ex = Assert.Throws<IrException>(() => OpenTypeBindings.ApplyJson(document, @"{
  ""M.AttributeTypeAndValue.value"": [
    { ""key"": ""2.5.4.3"", ""type"": { ""kind"": ""string"", ""stringType"": ""utf8"" } }
  ]
}"));

        Assert.Contains("use the object form", ex.Message);
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

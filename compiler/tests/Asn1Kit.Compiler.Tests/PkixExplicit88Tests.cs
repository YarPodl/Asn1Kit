using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class PkixExplicit88Tests
{
    [Fact]
    public void CompilesAndMatchesGoldenIr()
    {
        var document = PkixExplicit88TestData.CompileWithBindings();
        var actual = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(actual);

        var expected = IrSerializer.ToJson(TestData.LoadIr("compiler/fixtures/ir/pkix1-explicit88.json"));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DnAttributeValueBindingsCoverRfc5280NamingAttributes()
    {
        var document = PkixExplicit88TestData.CompileWithBindings();

        var module = Assert.Single(document.Modules);
        var atv = Assert.IsType<SequenceType>(
            module.Types.Single(type => type.Name == "AttributeTypeAndValue").Type);
        var value = Assert.IsType<AnyType>(atv.Components.Single(component => component.Name == "value").Type);
        Assert.Equal("type", value.DefinedBy);
        Assert.NotNull(value.Selector);
        Assert.Equal(new[] { "type" }, value.Selector!.Path);
        // Shared AttributeTypeAndValue: tables live on refs; body keeps selector only.
        Assert.Null(value.Bindings);
        var table = FindAttributeTypeAndValueOpenTypes(document)
            ?? throw new Xunit.Sdk.XunitException("Expected ref.openTypes on AttributeTypeAndValue uses.");
        Assert.Equal(17, table.Bindings.Count);
        Assert.Equal(12, table.Bindings.Count(binding =>
            binding.Type is RefType { Name: "DirectoryString" }));
        Assert.Equal(3, table.Bindings.Count(binding =>
            binding.Type is StringType { Form: StringTypes.Printable }));
        Assert.Equal(2, table.Bindings.Count(binding =>
            binding.Type is StringType { Form: StringTypes.Ia5 }));
    }

    private static IrOpenTypeUse? FindAttributeTypeAndValueOpenTypes(IrDocument document)
    {
        IrOpenTypeUse? found = null;
        foreach (var module in document.Modules)
        foreach (var type in module.Types)
            Walk(type.Type);
        return found;

        void Walk(TypeExpr expression)
        {
            if (found is not null) return;
            switch (expression)
            {
                case RefType { Name: "AttributeTypeAndValue", OpenTypes: { Count: > 0 } openTypes }:
                    found = openTypes[0];
                    break;
                case RefType { OpenTypes: { } nested }:
                    foreach (var use in nested)
                    foreach (var binding in use.Bindings)
                        Walk(binding.Type);
                    break;
                case SequenceType sequence:
                    foreach (var component in sequence.Components) Walk(component.Type);
                    break;
                case SetType set:
                    foreach (var component in set.Components) Walk(component.Type);
                    break;
                case ChoiceType choice:
                    foreach (var component in choice.Components) Walk(component.Type);
                    break;
                case SequenceOfType of:
                    Walk(of.Element);
                    break;
                case SetOfType of:
                    Walk(of.Element);
                    break;
            }
        }
    }

    [Fact]
    public void SpotChecksPkixShapes()
    {
        var document = PkixExplicit88TestData.CompileRaw();
        var module = document.Modules.Single();
        Assert.Equal("PKIX1Explicit88", module.Name);
        Assert.Equal("1.3.6.1.5.5.7.0.18", module.Oid);
        Assert.Equal(TagDefaults.Explicit, module.TagDefault);

        var types = module.Types.ToDictionary(t => t.Name);
        var values = module.Values.ToDictionary(v => v.Name);

        var tbs = Assert.IsType<SequenceType>(types["TBSCertificate"].Type);
        var version = tbs.Components.Single(c => c.Name == "version");
        Assert.Equal(0, version.Type.Tag!.Number);
        Assert.Equal(TagClasses.Context, version.Type.Tag.Class);
        Assert.Equal(TagModes.Explicit, version.Type.Tag.Mode);
        Assert.Equal(0, Assert.IsType<IrIntegerValue>(version.Default!).Value);

        var extension = Assert.IsType<SequenceType>(types["Extension"].Type);
        var critical = extension.Components.Single(c => c.Name == "critical");
        Assert.False(Assert.IsType<IrBooleanValue>(critical.Default!).Value);

        var algorithm = Assert.IsType<SequenceType>(types["AlgorithmIdentifier"].Type);
        var parameters = algorithm.Components.Single(c => c.Name == "parameters");
        var parametersAny = Assert.IsType<AnyType>(parameters.Type);
        Assert.Equal("algorithm", parametersAny.DefinedBy);
        Assert.True(parameters.Optional);
        Assert.Null(parametersAny.Bindings);

        var attribute = Assert.IsType<SequenceType>(types["Attribute"].Type);
        Assert.IsType<SetOfType>(attribute.Components.Single(c => c.Name == "values").Type);

        var rdn = Assert.IsType<SetOfType>(types["RelativeDistinguishedName"].Type);
        Assert.Equal(1, rdn.Constraint!.Size!.Min);
        Assert.Null(rdn.Constraint.Size.Max);

        var universal = Assert.IsType<OctetStringType>(types["UniversalString"].Type);
        Assert.Equal(28, universal.Tag!.Number);
        Assert.Equal(TagClasses.Universal, universal.Tag.Class);
        Assert.Equal(TagModes.Implicit, universal.Tag.Mode);

        var email = Assert.IsType<StringType>(types["EmailAddress"].Type);
        Assert.Equal(StringTypes.Ia5, email.Form);
        Assert.Equal(1, email.Constraint!.Size!.Min);
        Assert.Equal(255, email.Constraint.Size.Max);

        var country = Assert.IsType<StringType>(types["X520countryName"].Type);
        Assert.Equal(StringTypes.Printable, country.Form);
        Assert.Equal(2, country.Constraint!.Size!.Min);
        Assert.Equal(2, country.Constraint.Size.Max);

        var time = Assert.IsType<ChoiceType>(types["Time"].Type);
        Assert.Equal(TimeTypes.Utc, Assert.IsType<TimeType>(time.Components[0].Type).Form);
        Assert.Equal(TimeTypes.Generalized, Assert.IsType<TimeType>(time.Components[1].Type).Form);

        Assert.Equal("1.3.6.1.5.5.7.48.1", Assert.IsType<IrOidValue>(values["id-ad-ocsp"].Value).Value);
        Assert.Equal(32768, Assert.IsType<IrIntegerValue>(values["ub-name"].Value).Value);
    }
}

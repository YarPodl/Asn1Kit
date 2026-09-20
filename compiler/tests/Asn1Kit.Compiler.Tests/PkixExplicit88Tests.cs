using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class PkixExplicit88Tests
{
    [Fact]
    public void CompilesAndMatchesGoldenIr()
    {
        var asnPath = TestData.RepoPath("compiler/fixtures/asn1/pkix1-explicit88.asn");
        var goldenPath = TestData.RepoPath("compiler/fixtures/ir/pkix1-explicit88.json");
        var document = new Asn1Compiler().CompileFiles(new[] { asnPath });
        var actual = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(actual);

        var golden = File.ReadAllText(goldenPath);
        IrSerializer.ValidateSchema(golden);
        var expected = IrSerializer.ToJson(IrSerializer.FromJson(golden));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SpotChecksPkixShapes()
    {
        var document = new Asn1Compiler().CompileFiles(new[]
        {
            TestData.RepoPath("compiler/fixtures/asn1/pkix1-explicit88.asn")
        });
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
        Assert.Equal("algorithm", Assert.IsType<AnyType>(parameters.Type).DefinedBy);
        Assert.True(parameters.Optional);

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

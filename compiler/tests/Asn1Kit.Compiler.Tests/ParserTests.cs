using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class ParserTests
{
    [Fact]
    public void ParsesBitStringNamedBitsSetOfAnyDefaultAndStrings()
    {
        const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
Flags ::= BIT STRING { a(0), b(1) }
Bag ::= SET { x INTEGER OPTIONAL, y BOOLEAN DEFAULT TRUE }
List ::= SET SIZE (1..MAX) OF INTEGER
SeqList ::= SEQUENCE SIZE (1..ub) OF PrintableString
Id ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters ANY DEFINED BY algorithm OPTIONAL
}
Name ::= IA5String (SIZE (1..ub))
When ::= CHOICE { u UTCTime, g GeneralizedTime }
ub INTEGER ::= 10
END
";

        var document = new Asn1Compiler().CompileText(asn, "snippet.asn");
        var types = document.Modules[0].Types.ToDictionary(t => t.Name);

        var flags = Assert.IsType<BitStringType>(types["Flags"].Type);
        Assert.Equal(2, flags.NamedBits!.Count);

        var bag = Assert.IsType<SetType>(types["Bag"].Type);
        Assert.True(bag.Components[0].Optional);
        Assert.True(bag.Components[1].Optional);
        Assert.True(Assert.IsType<IrBooleanValue>(bag.Components[1].Default!).Value);

        var list = Assert.IsType<SetOfType>(types["List"].Type);
        Assert.Equal(1, list.Constraint!.Size!.Min);
        Assert.Null(list.Constraint.Size.Max);

        var seqList = Assert.IsType<SequenceOfType>(types["SeqList"].Type);
        Assert.Equal(10, seqList.Constraint!.Size!.Max);

        var id = Assert.IsType<SequenceType>(types["Id"].Type);
        var any = Assert.IsType<AnyType>(id.Components[1].Type);
        Assert.Equal("algorithm", any.DefinedBy);

        var name = Assert.IsType<StringType>(types["Name"].Type);
        Assert.Equal(StringTypes.Ia5, name.Form);

        var when = Assert.IsType<ChoiceType>(types["When"].Type);
        Assert.Equal(TimeTypes.Utc, Assert.IsType<TimeType>(when.Components[0].Type).Form);
        Assert.Equal(TimeTypes.Generalized, Assert.IsType<TimeType>(when.Components[1].Type).Form);
    }

    [Fact]
    public void RejectsClassOutsideProfile()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
Foo ::= CLASS
END
";

        var ex = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(asn));
        Assert.Contains("outside the Asn1Kit compiler profile", ex.Message);
    }

    [Fact]
    public void RejectsComponentsOf()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
Base ::= SEQUENCE { a INTEGER }
Ext ::= SEQUENCE { COMPONENTS OF Base, b BOOLEAN }
END
";

        var ex = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(asn));
        Assert.Contains("COMPONENTS OF", ex.Message);
    }

    [Fact]
    public void PreservesUnionValueConstraintAsUnsupported()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
id-a OBJECT IDENTIFIER ::= { 1 2 3 }
id-b OBJECT IDENTIFIER ::= { 1 2 4 }
PolicyQualifierId ::= OBJECT IDENTIFIER ( id-a | id-b )
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var type = document.Modules[0].Types.Single(t => t.Name == "PolicyQualifierId").Type;
        Assert.IsType<OidType>(type);
        Assert.Contains("|", type.Constraint!.Unsupported);
        Assert.Contains("id-a", type.Constraint.Unsupported);
        Assert.Contains("id-b", type.Constraint.Unsupported);
    }

    [Fact]
    public void BackendGeneratesAnyAlias()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
S ::= ANY
Holder ::= SEQUENCE { v S }
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new Asn1Kit.Codegen.CSharp.CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("Asn1Any", source);
        Assert.Contains("WriteAny", source);
        Assert.Contains("ReadAny", source);
        Assert.Contains("ASN.1 alias S ::= ANY.", source);
        Assert.DoesNotContain("class S", source);
        Assert.DoesNotContain("does not support kind 'any'", source);
    }

    [Fact]
    public void BackendEmitsNestedSequenceOfItemTypeWithUnderscore()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
PolicyMappings ::= SEQUENCE OF SEQUENCE {
  a INTEGER,
  b INTEGER
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new Asn1Kit.Codegen.CSharp.CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("List<PolicyMappings_Item>", source);
        Assert.Contains("public sealed class PolicyMappings_Item", source);
        Assert.DoesNotContain("class PolicyMappingsItem", source);
    }

    [Fact]
    public void BackendRenamesPropertyMatchingEnclosingType()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
DistributionPoint ::= SEQUENCE {
  distributionPoint INTEGER OPTIONAL,
  reasons BOOLEAN OPTIONAL
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new Asn1Kit.Codegen.CSharp.CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("public Asn1Integer? DistributionPointValue", source);
        Assert.DoesNotContain("public Asn1Integer? DistributionPoint {", source);
    }

    [Fact]
    public void BackendSanitizesHyphenatedFieldNamesInPeekLocals()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
S ::= SEQUENCE {
  country-name INTEGER OPTIONAL,
  built-in-domain INTEGER OPTIONAL
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new Asn1Kit.Codegen.CSharp.CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("out var tag_CountryName", source);
        Assert.Contains("out var tag_BuiltInDomain", source);
        Assert.DoesNotContain("tag_country-name", source);
        Assert.DoesNotContain("tag_built-in-domain", source);
    }
}

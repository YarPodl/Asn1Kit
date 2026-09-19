using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class ValueResolutionTests
{
    [Fact]
    public void ResolvesOidChainsAndUpperBoundsDeclaredLater()
    {
        const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
id-root OBJECT IDENTIFIER ::= { iso(1) org(3) 6 }
id-child OBJECT IDENTIFIER ::= { id-root 7 }
Name ::= PrintableString (SIZE (1..ub-name))
ub-name INTEGER ::= 32
END
";

        var document = new Asn1Compiler().CompileText(asn);
        var module = document.Modules[0];
        var child = module.Values.Single(v => v.Name == "id-child");
        Assert.Equal(new[] { 1, 3, 6, 7 }, Assert.IsType<IrOidValue>(child.Value).Arcs);

        var name = Assert.IsType<StringType>(module.Types.Single(t => t.Name == "Name").Type);
        Assert.Equal(1, name.Constraint!.Size!.Min);
        Assert.Equal(32, name.Constraint.Size.Max);
    }

    [Fact]
    public void ResolvesDefaultNamedNumberThroughTypeReference()
    {
        const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
Version ::= INTEGER { v1(0), v2(1) }
TBS ::= SEQUENCE { version [0] EXPLICIT Version DEFAULT v1 }
END
";

        var document = new Asn1Compiler().CompileText(asn);
        var tbs = Assert.IsType<SequenceType>(document.Modules[0].Types.Single(t => t.Name == "TBS").Type);
        var version = tbs.Components[0];
        Assert.Equal(0, Assert.IsType<IrIntegerValue>(version.Default!).Value);
        Assert.Equal(0, version.Type.Tag!.Number);
        Assert.Equal(TagModes.Explicit, version.Type.Tag.Mode);
    }

    [Fact]
    public void RejectsUnknownValueReference()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
id-a OBJECT IDENTIFIER ::= { id-missing 1 }
END
";

        var ex = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(asn));
        Assert.Contains("Unknown OID value reference", ex.Message);
    }

    [Fact]
    public void RejectsCyclicOid()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
id-a OBJECT IDENTIFIER ::= { id-b 1 }
id-b OBJECT IDENTIFIER ::= { id-a 2 }
END
";

        var ex = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(asn));
        Assert.Contains("Cyclic OBJECT IDENTIFIER", ex.Message);
    }

    [Fact]
    public void RejectsUnknownDefault()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
S ::= SEQUENCE { n INTEGER DEFAULT missing }
END
";

        var ex = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(asn));
        Assert.Contains("Unknown DEFAULT value", ex.Message);
    }

    [Fact]
    public void RejectsAnyDefinedByUnknownField()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
S ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters ANY DEFINED BY missing OPTIONAL
}
END
";

        var ex = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(asn));
        Assert.Contains("ANY DEFINED BY", ex.Message);
    }

    [Fact]
    public void RejectsDuplicateValueName()
    {
        const string asn = @"
M DEFINITIONS ::= BEGIN
ub INTEGER ::= 1
ub INTEGER ::= 2
END
";

        var ex = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(asn));
        Assert.Contains("Duplicate value", ex.Message);
    }
}

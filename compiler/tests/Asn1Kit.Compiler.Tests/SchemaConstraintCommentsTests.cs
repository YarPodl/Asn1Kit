using Asn1Kit.Codegen;
using Asn1Kit.Ir;
using Xunit;

namespace Asn1Kit.Tests;

public sealed class SchemaConstraintCommentsTests
{
    [Fact]
    public void TryFormatConstraint_SizeExact()
    {
        var text = SchemaConstraintComments.TryFormatConstraint(new IrConstraint
        {
            Size = new IrBound { Min = 13, Max = 13 }
        });
        Assert.Equal("ASN.1 SIZE (13). Not enforced by runtime.", text);
    }

    [Fact]
    public void TryFormatConstraint_SizeRange()
    {
        var text = SchemaConstraintComments.TryFormatConstraint(new IrConstraint
        {
            Size = new IrBound { Min = 1, Max = 200 }
        });
        Assert.Equal("ASN.1 SIZE (1..200). Not enforced by runtime.", text);
    }

    [Fact]
    public void TryFormatConstraint_SizeMax()
    {
        var text = SchemaConstraintComments.TryFormatConstraint(new IrConstraint
        {
            Size = new IrBound { Min = 1, Max = null }
        });
        Assert.Equal("ASN.1 SIZE (1..MAX). Not enforced by runtime.", text);
    }

    [Fact]
    public void TryFormatConstraint_ValueRange()
    {
        var text = SchemaConstraintComments.TryFormatConstraint(new IrConstraint
        {
            Value = new IrBound { Min = 0, Max = 4294967295 }
        });
        Assert.Equal("ASN.1 value range (0..4294967295). Not enforced by runtime.", text);
    }

    [Fact]
    public void TryFormatConstraint_Unsupported()
    {
        var text = SchemaConstraintComments.TryFormatConstraint(new IrConstraint
        {
            Unsupported = "(id-qt-cps | id-qt-unotice)"
        });
        Assert.Equal(
            "ASN.1 constraint unsupported: (id-qt-cps | id-qt-unotice). Not enforced by runtime.",
            text);
    }

    [Fact]
    public void TryFormatConstraint_Empty_ReturnsNull()
    {
        Assert.Null(SchemaConstraintComments.TryFormatConstraint(null));
        Assert.Null(SchemaConstraintComments.TryFormatConstraint(new IrConstraint()));
    }

    [Fact]
    public void TryFormatConstraint_CombinedSizeAndUnsupported()
    {
        var text = SchemaConstraintComments.TryFormatConstraint(new IrConstraint
        {
            Size = new IrBound { Min = 1, Max = 64 },
            Unsupported = "(FROM (\"a\"..\"z\"))"
        });
        Assert.Equal(
            "ASN.1 SIZE (1..64). ASN.1 constraint unsupported: (FROM (\"a\"..\"z\")). Not enforced by runtime.",
            text);
    }

    [Fact]
    public void TryFormatObjectSet_ClosedWithTable()
    {
        var text = SchemaConstraintComments.TryFormatObjectSet("CertExtensions", tableExtensible: false);
        Assert.Equal(
            "ASN.1 object set CertExtensions (closed). Membership not enforced by runtime.",
            text);
    }

    [Fact]
    public void TryFormatObjectSet_ExtensibleWithoutTable()
    {
        var text = SchemaConstraintComments.TryFormatObjectSet(null, tableExtensible: true);
        Assert.Equal(
            "ASN.1 object set (extensible). Membership not enforced by runtime.",
            text);
    }

    [Fact]
    public void TryFormatObjectSet_MissingExtensible_ReturnsNull()
    {
        Assert.Null(SchemaConstraintComments.TryFormatObjectSet("CertExtensions", tableExtensible: null));
    }

    [Fact]
    public void CollectForTypeExpr_InlineSize()
    {
        var type = new StringType
        {
            Form = StringTypes.Utf8,
            Constraint = new IrConstraint { Size = new IrBound { Min = 1, Max = 200 } }
        };
        Assert.Equal(
            "ASN.1 SIZE (1..200). Not enforced by runtime.",
            SchemaConstraintComments.CollectForTypeExpr(type));
    }

    [Fact]
    public void CollectForTypeExpr_CollapsibleAliasConstraint()
    {
        var ogrn = new IrTypeDef
        {
            Name = "OGRN",
            Type = new StringType
            {
                Form = StringTypes.Numeric,
                Constraint = new IrConstraint { Size = new IrBound { Min = 13, Max = 13 } }
            }
        };
        var field = new RefType { Name = "OGRN", Module = "M" };
        string? text = SchemaConstraintComments.CollectForTypeExpr(
            field,
            _ => ogrn);
        Assert.Equal("ASN.1 SIZE (13). Not enforced by runtime.", text);
    }

    [Fact]
    public void CollectForTypeExpr_DoesNotEnterNamedSequence()
    {
        var sequence = new IrTypeDef
        {
            Name = "Holder",
            Type = new SequenceType
            {
                Components =
                {
                    new IrComponent
                    {
                        Name = "value",
                        Type = new StringType
                        {
                            Form = StringTypes.Utf8,
                            Constraint = new IrConstraint { Size = new IrBound { Min = 1, Max = 10 } }
                        }
                    }
                }
            }
        };
        var field = new RefType { Name = "Holder", Module = "M" };
        Assert.Null(SchemaConstraintComments.CollectForTypeExpr(field, _ => sequence));
    }

    [Fact]
    public void CollectForTypeExpr_AnyTableAndContaining()
    {
        var type = new OctetStringType
        {
            Containing = new AnyType
            {
                Table = "Mixed",
                TableExtensible = false
            }
        };
        Assert.Equal(
            "ASN.1 object set Mixed (closed). Membership not enforced by runtime.",
            SchemaConstraintComments.CollectForTypeExpr(type));
    }

    [Fact]
    public void CollectForTypeExpr_RefOpenTypes()
    {
        var type = new RefType
        {
            Name = "Extension",
            OpenTypes = new List<IrOpenTypeUse>
            {
                new IrOpenTypeUse
                {
                    Path = new List<string> { "extnValue" },
                    Table = "CertExtensions",
                    TableExtensible = true,
                    Bindings = new List<IrOpenTypeBinding>()
                }
            }
        };
        Assert.Equal(
            "ASN.1 object set CertExtensions (extensible). Membership not enforced by runtime.",
            SchemaConstraintComments.CollectForTypeExpr(type));
    }

    [Fact]
    public void CollectForTypeExpr_ConstraintAndObjectSet()
    {
        var type = new AnyType
        {
            Constraint = new IrConstraint { Size = new IrBound { Min = 1, Max = 1 } },
            Table = "Mixed",
            TableExtensible = false
        };
        Assert.Equal(
            "ASN.1 SIZE (1). Not enforced by runtime. ASN.1 object set Mixed (closed). Membership not enforced by runtime.",
            SchemaConstraintComments.CollectForTypeExpr(type));
    }
}

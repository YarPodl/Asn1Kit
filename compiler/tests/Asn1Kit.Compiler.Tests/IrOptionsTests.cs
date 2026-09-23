using System.Text.Json.Nodes;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class IrOptionsTests
{
    [Fact]
    public void Set_CreatesNestedPath_AndPreservesSiblingKeys()
    {
        var options = IrOptions.Set(null, "csharp.typeName", JsonValue.Create("KeepMe")!);
        options = IrOptions.Set(options, "csharp.namespace", JsonValue.Create("Example.Ns")!);

        Assert.Equal("Example.Ns", IrOptions.CSharpNamespace(options));
        Assert.Equal("KeepMe", IrOptions.CSharpTypeName(options));
    }

    [Fact]
    public void Set_RejectsEmptyOrBrokenPath()
    {
        Assert.Throws<IrException>(() => IrOptions.Set(null, "", JsonValue.Create("x")!));
        Assert.Throws<IrException>(() => IrOptions.Set(null, "csharp..namespace", JsonValue.Create("x")!));
    }

    [Theory]
    [InlineData("Asn1Kit.Pkix", "Asn1Kit.Pkix")]
    [InlineData("\"quoted\"", "quoted")]
    public void ParseCliValue_Strings(string raw, string expected)
    {
        var node = IrOptions.ParseCliValue(raw);
        Assert.Equal(expected, node.GetValue<string>());
    }

    [Fact]
    public void ParseCliValue_BoolAndNumber()
    {
        Assert.True(IrOptions.ParseCliValue("true").GetValue<bool>());
        Assert.False(IrOptions.ParseCliValue("false").GetValue<bool>());
        Assert.Equal(42, IrOptions.ParseCliValue("42").GetValue<int>());
    }

    [Fact]
    public void ParseCliAssignment_SplitsPathAndValue()
    {
        var (path, value) = IrOptions.ParseCliAssignment("csharp.namespace=Asn1Kit.Pkix");
        Assert.Equal("csharp.namespace", path);
        Assert.Equal("Asn1Kit.Pkix", value.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("=value")]
    [InlineData("csharp.namespace")]
    public void ParseCliAssignment_RejectsInvalid(string assignment)
    {
        Assert.Throws<IrException>(() => IrOptions.ParseCliAssignment(assignment));
    }

    [Fact]
    public void ApplyToModules_SetsAllModules()
    {
        var document = new IrDocument
        {
            Modules =
            {
                new IrModule { Name = "A" },
                new IrModule { Name = "B", Options = IrOptions.SetCSharp(null, "typeName", "Keep") }
            }
        };

        IrOptions.ApplyToModules(document, new[] { "csharp.namespace=Shared.Ns" });

        Assert.Equal("Shared.Ns", IrOptions.CSharpNamespace(document.Modules[0].Options));
        Assert.Equal("Shared.Ns", IrOptions.CSharpNamespace(document.Modules[1].Options));
        Assert.Equal("Keep", IrOptions.CSharpTypeName(document.Modules[1].Options));
    }

    [Fact]
    public void OpenTypeMismatch_DefaultsToSoft_AndAcceptsStrict()
    {
        Assert.Equal(IrOptions.OpenTypeMismatchModes.Soft, IrOptions.OpenTypeMismatch(null));
        var soft = IrOptions.SetOpenTypeMismatch(null, "soft");
        Assert.Equal(IrOptions.OpenTypeMismatchModes.Soft, IrOptions.OpenTypeMismatch(soft));
        var strict = IrOptions.SetOpenTypeMismatch(null, "strict");
        Assert.Equal(IrOptions.OpenTypeMismatchModes.Strict, IrOptions.OpenTypeMismatch(strict));
        Assert.Throws<IrException>(() =>
            IrOptions.OpenTypeMismatch(IrOptions.SetOpenTypeMismatch(null, "nope")));
    }

    [Fact]
    public void Lazy_DefaultsFalse_AndAcceptsBoolOrString()
    {
        Assert.False(IrOptions.IsLazy(null));
        Assert.True(IrOptions.IsLazy(IrOptions.SetLazy(null, true)));
        Assert.False(IrOptions.IsLazy(IrOptions.SetLazy(null, false)));
        Assert.True(IrOptions.IsLazy(IrOptions.Set(null, "lazy", JsonValue.Create("true")!)));
        Assert.False(IrOptions.IsLazy(IrOptions.Set(null, "lazy", JsonValue.Create("false")!)));
    }

    [Fact]
    public void RetainEncoded_DefaultsFalse_AndAcceptsBoolOrString()
    {
        Assert.False(IrOptions.IsRetainEncoded(null));
        Assert.True(IrOptions.IsRetainEncoded(IrOptions.SetRetainEncoded(null, true)));
        Assert.False(IrOptions.IsRetainEncoded(IrOptions.SetRetainEncoded(null, false)));
        Assert.True(IrOptions.IsRetainEncoded(IrOptions.Set(null, "retainEncoded", JsonValue.Create("true")!)));
    }

    [Fact]
    public void CSharpValueType_AcceptsBoolOrString()
    {
        Assert.False(IrOptions.IsCSharpValueType(null));
        Assert.True(IrOptions.IsCSharpValueType(IrOptions.SetCSharpValueType(null, true)));
        Assert.True(IrOptions.IsCSharpValueType(
            IrOptions.Set(null, "csharp.valueType", JsonValue.Create(true)!)));
    }

    [Fact]
    public void ApplyToModules_SetsLazy()
    {
        var document = new IrDocument
        {
            Modules = { new IrModule { Name = "M" } }
        };
        IrOptions.ApplyToModules(document, new[] { "lazy=true" });
        Assert.True(IrOptions.IsLazy(document.Modules[0].Options));
    }
}

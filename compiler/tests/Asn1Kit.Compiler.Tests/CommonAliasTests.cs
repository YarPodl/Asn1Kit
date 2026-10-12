using System.Text.Json.Nodes;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class CommonAliasTests
{
    private static IrDocument Document() => new Asn1Compiler().CompileText(@"M DEFINITIONS ::= BEGIN
        A ::= CHOICE { utf8 UTF8String, printable PrintableString }
        B ::= CHOICE { utf8 UTF8String, printable PrintableString }
        C ::= CHOICE { utf8 UTF8String, printable PrintableString }
        END");

    [Fact]
    public void CommonAliasRoundTripsWithUnknownOptionsAndChain()
    {
        var document = Document();
        document.Modules[0].Types[1].Options = new JsonObject { ["aliasOf"] = "A", ["future"] = 42 };
        document.Modules[0].Types[2].Options = new JsonObject { ["aliasOf"] = "B" };
        var copy = IrSerializer.FromJson(IrSerializer.ToJson(document));
        var options = copy.Modules[0].Types[1].Options;
        Assert.Equal("A", IrOptions.AliasOf(options));
        Assert.Equal("A", IrOptions.CSharpAliasOf(options));
        Assert.Equal("A", IrOptions.CppAliasOf(options));
        Assert.Equal(42, options!["future"]!.GetValue<int>());
        Assert.IsType<ChoiceType>(copy.Modules[0].Types[1].Type);
    }

    [Theory]
    [InlineData("aliasOf")]
    [InlineData("csharp.aliasOf")]
    [InlineData("cpp.aliasOf")]
    public void AliasRejectsMissingDisabledAndCyclicTargets(string path)
    {
        var document = Document();
        var a = document.Modules[0].Types[0];
        var b = document.Modules[0].Types[1];
        b.Options = IrOptions.Set(null, path, JsonValue.Create("Missing")!);
        Assert.Contains("does not resolve", Assert.Throws<IrException>(() => IrValidator.Validate(document)).Message);
        b.Options = IrOptions.Set(null, path, JsonValue.Create("A")!);
        a.Options = new JsonObject { ["generate"] = false };
        Assert.Contains("generate: false", Assert.Throws<IrException>(() => IrValidator.Validate(document)).Message);
        a.Options = IrOptions.Set(null, path, JsonValue.Create("B")!);
        Assert.Contains("cycle", Assert.Throws<IrException>(() => IrValidator.Validate(document)).Message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("{}")]
    public void InvalidCommonAliasReportsOwner(string json)
    {
        var document = Document();
        document.Modules[0].Types[1].Options = new JsonObject { ["aliasOf"] = JsonNode.Parse(json) };
        Assert.Contains("M.B", Assert.Throws<IrException>(() => IrValidator.Validate(document)).Message);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("cpp")]
    public void MixedCommonAndLegacyCycleIsRejected(string language)
    {
        var document = Document();
        document.Modules[0].Types[0].Options = new JsonObject { ["aliasOf"] = "B" };
        document.Modules[0].Types[1].Options = IrOptions.Set(null, language + ".aliasOf", JsonValue.Create("A")!);
        Assert.Contains("cycle", Assert.Throws<IrException>(() => IrValidator.Validate(document)).Message);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("cpp")]
    public void CommonAliasValidatesLegacyConflictsAndTypeName(string language)
    {
        var document = Document();
        var b = document.Modules[0].Types[1];
        b.Options = IrOptions.Set(new JsonObject { ["aliasOf"] = "A" }, language + ".aliasOf", JsonValue.Create("A")!);
        IrValidator.Validate(document);
        b.Options = IrOptions.Set(b.Options, language + ".aliasOf", JsonValue.Create("C")!);
        Assert.Contains("Conflicting", Assert.Throws<IrException>(() => IrValidator.Validate(document)).Message);
        b.Options = IrOptions.Set(new JsonObject { ["aliasOf"] = "A" }, language + ".typeName", JsonValue.Create("Other")!);
        Assert.Contains("typeName", Assert.Throws<IrException>(() => IrValidator.Validate(document)).Message);
    }
}

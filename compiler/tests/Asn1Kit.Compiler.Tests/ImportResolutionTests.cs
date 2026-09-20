using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class ImportResolutionTests
{
    [Fact]
    public void RejectsImportFromMissingModule()
    {
        const string consumer = @"
Consumer DEFINITIONS ::= BEGIN
IMPORTS Foo FROM Missing;
Bar ::= Foo
END
";
        var ex = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(consumer));
        Assert.Contains("Imported module 'Missing' was not found", ex.Message);
    }

    [Fact]
    public void RejectsImportOfAbsentTypeFromPresentModule()
    {
        const string provider = @"
Provider DEFINITIONS ::= BEGIN
Present ::= INTEGER
END
";
        const string consumer = @"
Consumer DEFINITIONS ::= BEGIN
IMPORTS Absent FROM Provider;
Bar ::= Absent
END
";
        var ex = Assert.Throws<CompileException>(() =>
            new Asn1Compiler().CompileTexts(new (string Text, string? FileName)[]
            {
                (provider, "provider.asn"),
                (consumer, "consumer.asn")
            }));
        Assert.Contains("Imported type 'Absent' from 'Provider'", ex.Message);
        Assert.Contains("was not found in module 'Provider'", ex.Message);
    }

    [Fact]
    public void ResolvesImportedTypeAndValueAcrossModules()
    {
        const string provider = @"
Provider DEFINITIONS EXPLICIT TAGS ::= BEGIN
Shared ::= INTEGER
id-arc OBJECT IDENTIFIER ::= { 1 2 3 }
END
";
        const string consumer = @"
Consumer DEFINITIONS IMPLICIT TAGS ::= BEGIN
IMPORTS Shared, id-arc FROM Provider;
Wrapped ::= SEQUENCE { value Shared }
leaf OBJECT IDENTIFIER ::= { id-arc 4 }
END
";
        var document = new Asn1Compiler().CompileTexts(new (string Text, string? FileName)[]
        {
            (provider, "provider.asn"),
            (consumer, "consumer.asn")
        });

        Assert.Equal(2, document.Modules.Count);
        var consumerModule = document.Modules.Single(m => m.Name == "Consumer");
        var import = Assert.Single(consumerModule.Imports);
        Assert.Equal("Provider", import.Module);
        Assert.Contains("Shared", import.Types);
        Assert.Contains("id-arc", import.Values!);

        var wrapped = Assert.IsType<SequenceType>(
            consumerModule.Types.Single(t => t.Name == "Wrapped").Type);
        Assert.Equal("Shared", Assert.IsType<RefType>(wrapped.Components[0].Type).Name);

        var leaf = consumerModule.Values.Single(v => v.Name == "leaf");
        Assert.Equal("1.2.3.4", Assert.IsType<IrOidValue>(leaf.Value).Value);
    }

    [Fact]
    public void BackendQualifiesCrossModuleRefWhenNamespacesDiffer()
    {
        const string provider = @"
Provider DEFINITIONS EXPLICIT TAGS ::= BEGIN
Shared ::= SEQUENCE { n INTEGER }
END
";
        const string consumer = @"
Consumer DEFINITIONS IMPLICIT TAGS ::= BEGIN
IMPORTS Shared FROM Provider;
Wrapped ::= SEQUENCE { value Shared }
END
";
        var document = new Asn1Compiler().CompileTexts(new (string Text, string? FileName)[]
        {
            (provider, "provider.asn"),
            (consumer, "consumer.asn")
        });

        var files = new Asn1Kit.Codegen.CSharp.CSharpBackend().Generate(document);
        Assert.Equal(2, files.Count);
        var consumerSource = files.Single(f => f.RelativePath == "Consumer.g.cs").Contents;
        Assert.Contains("public Provider.Shared Value", consumerSource);
        Assert.DoesNotContain("public Shared Value", consumerSource);
    }

    [Fact]
    public void BackendKeepsUnqualifiedRefWhenNamespacesMatch()
    {
        const string provider = @"
Provider DEFINITIONS EXPLICIT TAGS ::= BEGIN
Shared ::= SEQUENCE { n INTEGER }
END
";
        const string consumer = @"
Consumer DEFINITIONS IMPLICIT TAGS ::= BEGIN
IMPORTS Shared FROM Provider;
Wrapped ::= SEQUENCE { value Shared }
END
";
        var document = new Asn1Compiler().CompileTexts(new (string Text, string? FileName)[]
        {
            (provider, "provider.asn"),
            (consumer, "consumer.asn")
        });
        foreach (var module in document.Modules)
        {
            module.Options = IrOptions.SetCSharp(module.Options, "namespace", "Same.Ns");
        }

        var files = new Asn1Kit.Codegen.CSharp.CSharpBackend().Generate(document);
        var consumerSource = files.Single(f => f.RelativePath == "Consumer.g.cs").Contents;
        Assert.Contains("namespace Same.Ns;", consumerSource);
        Assert.Contains("public Shared Value", consumerSource);
        Assert.DoesNotContain("public Same.Ns.Shared Value", consumerSource);
    }
}

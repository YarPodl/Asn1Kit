using System.Numerics;
using System.Reflection;
using System.Text;
using Asn1Kit.Codegen;
using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;
using Asn1Kit.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Asn1Kit.Tests;

public sealed class IrSchemaTests
{
    [Fact]
    public void ExampleJson_MatchesSchemaAndRoundTrips()
    {
        var json = File.ReadAllText(TestData.RepoPath("fixtures/ir/example.json"));
        IrSerializer.ValidateSchema(json);
        var document = IrSerializer.FromJson(json);
        Assert.Equal(1, document.IrVersion);
        Assert.Equal("ExampleModule", document.Modules[0].Name);
        var person = Assert.IsType<SequenceType>(document.Modules[0].Types[0].Type);
        Assert.Equal(3, person.Components.Count);
        Assert.True(person.Components[2].Optional);
        Assert.Equal("Nickname", IrOptions.CSharpPropertyName(person.Components[2].Options));

        var again = IrSerializer.FromJson(IrSerializer.ToJson(document));
        Assert.Equal("Person", again.Modules[0].Types[0].Name);
    }

    [Fact]
    public void UnknownOptions_ArePreserved()
    {
        var json = File.ReadAllText(TestData.RepoPath("fixtures/ir/example.json"));
        var document = IrSerializer.FromJson(json);
        document.Modules[0].Options!["extra"] = "keep-me";
        var roundTrip = IrSerializer.FromJson(IrSerializer.ToJson(document));
        Assert.Equal("keep-me", roundTrip.Modules[0].Options!["extra"]!.ToString());
    }
}

public sealed class CompilerTests
{
    [Fact]
    public void ExampleAsn_GetsAutomaticTags()
    {
        var document = new Asn1Compiler().CompileFiles(new[] { TestData.RepoPath("fixtures/asn1/example.asn") });
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var person = Assert.IsType<SequenceType>(document.Modules[0].Types[0].Type);
        Assert.Equal(0, person.Components[0].Type.Tag!.Number);
        Assert.Equal(1, person.Components[1].Type.Tag!.Number);
        Assert.Equal(2, person.Components[2].Type.Tag!.Number);
        Assert.Equal(TagModes.Implicit, person.Components[0].Type.Tag!.Mode);
        Assert.True(person.Components[2].Optional);
        Assert.IsType<IntegerType>(person.Components[0].Type);
        Assert.IsType<OctetStringType>(person.Components[1].Type);
    }
}

public sealed class RuntimeTests
{
    [Fact]
    public void Der_RoundTripsPersonShape()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequence(Asn1Tag.Sequence, inner =>
        {
            Asn1Integer.Encode(inner, 42, new Asn1Tag(Asn1TagClass.ContextSpecific, 0));
            Asn1OctetString.Encode(inner, Encoding.UTF8.GetBytes("Ann"), new Asn1Tag(Asn1TagClass.ContextSpecific, 1));
        });
        var bytes = writer.Encode();
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        reader.ReadSequence(Asn1Tag.Sequence, inner =>
        {
            Assert.Equal(42, Asn1Integer.Decode(inner, new Asn1Tag(Asn1TagClass.ContextSpecific, 0)));
            Assert.Equal("Ann", Encoding.UTF8.GetString(Asn1OctetString.Decode(inner, new Asn1Tag(Asn1TagClass.ContextSpecific, 1))));
            Assert.True(inner.Eof);
        });
    }

    [Fact]
    public void Ber_ReadsIndefiniteLengthOctetString()
    {
        var ber = new byte[] { 0x24, 0x80, 0x04, 0x03, 0x41, 0x6E, 0x6E, 0x00, 0x00 };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        var value = reader.ReadOctetString(Asn1Tag.OctetString);
        Assert.Equal("Ann", Encoding.UTF8.GetString(value));
    }

    [Fact]
    public void Der_RejectsIndefiniteLength()
    {
        var ber = new byte[] { 0x30, 0x80, 0x00, 0x00 };
        var reader = new Asn1Reader(ber, Asn1Encoding.Der);
        Assert.Throws<Asn1Exception>(() => reader.ReadSequence(Asn1Tag.Sequence, _ => { }));
    }
}

public sealed class RoundTripTests
{
    [Fact]
    public void GeneratedCSharp_CompilesAndRoundTripsPerson()
    {
        var document = IrSerializer.Load(TestData.RepoPath("fixtures/ir/example.json"));
        var files = new CSharpBackend().Generate(document);
        var source = files.Single().Contents;
        Assert.Contains("class Person", source);
        Assert.Contains("Nickname", source);

        var assembly = CompileGenerated(source);
        var type = assembly.GetType("Example.Asn1.Person");
        Assert.NotNull(type);
        var person = Activator.CreateInstance(type!)!;
        type!.GetProperty("Id")!.SetValue(person, new BigInteger(42));
        type.GetProperty("Name")!.SetValue(person, Encoding.UTF8.GetBytes("Ann"));
        type.GetProperty("Nickname")!.SetValue(person, Encoding.UTF8.GetBytes("A"));

        var writer = new Asn1Writer(Asn1Encoding.Der);
        type.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(person, new object[] { writer });
        var encoded = writer.Encode();

        var reader = new Asn1Reader(encoded, Asn1Encoding.Der);
        var decoded = type.GetMethod("Decode", new[] { typeof(Asn1Reader) })!.Invoke(null, new object[] { reader })!;
        Assert.Equal(new BigInteger(42), type.GetProperty("Id")!.GetValue(decoded));
        Assert.Equal("Ann", Encoding.UTF8.GetString((byte[])type.GetProperty("Name")!.GetValue(decoded)!));
        Assert.Equal("A", Encoding.UTF8.GetString((byte[])type.GetProperty("Nickname")!.GetValue(decoded)!));
    }

    [Fact]
    public void CompileThenGenerateFromAsn_Works()
    {
        var document = new Asn1Compiler().CompileFiles(new[] { TestData.RepoPath("fixtures/asn1/example.asn") });
        var files = new CodeGenerator(new ILanguageBackend[] { new CSharpBackend() }).Generate(document, "csharp");
        Assert.Contains("class Person", files.Single().Contents);
    }

    private static Assembly CompileGenerated(string source)
    {
        var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = tpa.Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(p => MetadataReference.CreateFromFile(p))
            .Concat(new[] { MetadataReference.CreateFromFile(typeof(Asn1Writer).Assembly.Location) })
            .ToList();

        var compilation = CSharpCompilation.Create(
            "GeneratedAsn1",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException(errors);
        }

        return Assembly.Load(stream.ToArray());
    }
}

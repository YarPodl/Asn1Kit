using System;
using System.Linq;
using System.Reflection;
using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;
using Asn1Kit.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Asn1Kit.Tests;

public sealed class OptionalChoiceDecodeTests
{
    [Fact]
    public void GeneratedCSharp_OptionalUntaggedChoice_PeeksAlternativeTags()
    {
        const string asn = @"
OptionalTimeMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
Time ::= CHOICE { utcTime UTCTime, generalTime GeneralizedTime }
Form ::= SEQUENCE {
  thisUpdate Time,
  nextUpdate Time OPTIONAL,
  leftover INTEGER
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;

        Assert.Contains(
            "if (reader.TryPeekTag(out var tag_NextUpdate) && (tag_NextUpdate.MatchesIgnoreConstructed(Asn1Tag.UtcTime) || tag_NextUpdate.MatchesIgnoreConstructed(Asn1Tag.GeneralizedTime)))",
            source);
        Assert.DoesNotContain(
            "tag_NextUpdate.MatchesIgnoreConstructed(Asn1Tag.Sequence)",
            source);

        var assembly = CompileGenerated(source);
        var timeType = assembly.GetType("OptionalTimeMod.Time")!;
        var formType = assembly.GetType("OptionalTimeMod.Form")!;
        var instant = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var next = new DateTimeOffset(2021, 6, 7, 8, 9, 10, TimeSpan.Zero);
        var thisUpdate = timeType.GetMethod("FromUtcTime", new[] { typeof(DateTimeOffset) })!
            .Invoke(null, new object[] { instant })!;
        var nextUpdate = timeType.GetMethod("FromUtcTime", new[] { typeof(DateTimeOffset) })!
            .Invoke(null, new object[] { next })!;

        var withNext = Activator.CreateInstance(formType)!;
        formType.GetProperty("ThisUpdate")!.SetValue(withNext, thisUpdate);
        formType.GetProperty("NextUpdate")!.SetValue(withNext, nextUpdate);
        formType.GetProperty("Leftover")!.SetValue(withNext, Asn1Integer.FromInt32(42));

        var writer = new Asn1Writer(Asn1Encoding.Der);
        formType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(withNext, new object[] { writer });
        var encoded = writer.Encode();

        var decoded = formType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(encoded, Asn1Encoding.Der) })!;
        Assert.NotNull(formType.GetProperty("NextUpdate")!.GetValue(decoded));
        Assert.Equal(next, timeType.GetProperty("Value")!.GetValue(formType.GetProperty("NextUpdate")!.GetValue(decoded)!));
        Assert.Equal(Asn1Integer.FromInt32(42), formType.GetProperty("Leftover")!.GetValue(decoded));

        var withoutNext = Activator.CreateInstance(formType)!;
        formType.GetProperty("ThisUpdate")!.SetValue(withoutNext, thisUpdate);
        formType.GetProperty("Leftover")!.SetValue(withoutNext, Asn1Integer.FromInt32(7));
        var writer2 = new Asn1Writer(Asn1Encoding.Der);
        formType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(withoutNext, new object[] { writer2 });
        var decoded2 = formType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(writer2.Encode(), Asn1Encoding.Der) })!;
        Assert.Null(formType.GetProperty("NextUpdate")!.GetValue(decoded2));
        Assert.Equal(Asn1Integer.FromInt32(7), formType.GetProperty("Leftover")!.GetValue(decoded2));
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
            "GeneratedOptionalChoice",
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

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
        var json = File.ReadAllText(TestData.RepoPath("compiler/fixtures/ir/example.json"));
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
        var json = File.ReadAllText(TestData.RepoPath("compiler/fixtures/ir/example.json"));
        var document = IrSerializer.FromJson(json);
        document.Modules[0].Options!["extra"] = "keep-me";
        var roundTrip = IrSerializer.FromJson(IrSerializer.ToJson(document));
        Assert.Equal("keep-me", roundTrip.Modules[0].Options!["extra"]!.ToString());
    }

    [Fact]
    public void TimeFractionDigits_RoundTripsAndRejectsUtcNonZero()
    {
        var document = new IrDocument
        {
            IrVersion = 1,
            Modules =
            {
                new IrModule
                {
                    Name = "TimeMod",
                    TagDefault = TagDefaults.Explicit,
                    Types =
                    {
                        new IrTypeDef
                        {
                            Name = "Stamp",
                            Type = new TimeType { Form = TimeTypes.Generalized, FractionDigits = 5 }
                        }
                    }
                }
            }
        };

        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        var again = IrSerializer.FromJson(json);
        Assert.Equal(5, Assert.IsType<TimeType>(again.Modules[0].Types[0].Type).FractionDigits);

        document.Modules[0].Types[0].Type = new TimeType { Form = TimeTypes.Utc, FractionDigits = 3 };
        var ex = Assert.Throws<IrException>(() => IrValidator.Validate(document));
        Assert.Contains("utc", ex.Message);
        Assert.Contains("fractionDigits", ex.Message);
    }
}

public sealed class CompilerTests
{
    [Fact]
    public void ExampleAsn_GetsAutomaticTags()
    {
        var document = new Asn1Compiler().CompileFiles(new[] { TestData.RepoPath("compiler/fixtures/asn1/example.asn") });
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

    [Fact]
    public void CSharpTypeName_OmittedWhenSameAsAsnName()
    {
        const string asn = @"
HyphenModule DEFINITIONS AUTOMATIC TAGS ::= BEGIN
PlainName ::= INTEGER
Some-Type ::= INTEGER
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var types = document.Modules[0].Types.ToDictionary(t => t.Name);
        Assert.Null(IrOptions.CSharpTypeName(types["PlainName"].Options));
        Assert.Equal("SomeType", IrOptions.CSharpTypeName(types["Some-Type"].Options));
    }
}

public sealed class RoundTripTests
{
    [Fact]
    public void GeneratedCSharp_CompilesAndRoundTripsPerson()
    {
        var document = IrSerializer.Load(TestData.RepoPath("compiler/fixtures/ir/example.json"));
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
        var document = new Asn1Compiler().CompileFiles(new[] { TestData.RepoPath("compiler/fixtures/asn1/example.asn") });
        var files = new CodeGenerator(new ILanguageBackend[] { new CSharpBackend() }).Generate(document, "csharp");
        Assert.Contains("class Person", files.Single().Contents);
    }

    [Fact]
    public void PrimitivesAsn_GeneratesAndRoundTrips()
    {
        var document = new Asn1Compiler().CompileFiles(new[] { TestData.RepoPath("compiler/fixtures/asn1/primitives.asn") });
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("Bit_DigitalSignature", source);
        Assert.Contains("WriteString", source);
        Assert.Contains("WriteTime", source);
        Assert.Contains("Asn1TimeForm.Generalized, 3)", source);
        Assert.Contains("WriteBitString", source);
        Assert.Contains("WriteExplicit", source);

        var assembly = CompileGenerated(source);
        var sampleType = assembly.GetType("PrimitivesModule.Sample")!;
        var keyUsageType = assembly.GetType("PrimitivesModule.KeyUsage")!;
        Assert.Equal(0, keyUsageType.GetField("Bit_DigitalSignature")!.GetValue(null));

        var utc = new DateTimeOffset(2017, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var sample = Activator.CreateInstance(sampleType)!;
        sampleType.GetProperty("Note")!.SetValue(sample, "N");
        var flags = Activator.CreateInstance(keyUsageType)!;
        keyUsageType.GetProperty("Value")!.SetValue(flags, Asn1BitString.FromBits(new[] { true }));
        sampleType.GetProperty("Flags")!.SetValue(sample, flags);
        sampleType.GetProperty("Name")!.SetValue(sample, "Ann");
        sampleType.GetProperty("Email")!.SetValue(sample, "a@b.c");
        sampleType.GetProperty("NotBefore")!.SetValue(sample, utc);
        sampleType.GetProperty("NotAfter")!.SetValue(sample, utc);
        sampleType.GetProperty("Nickname")!.SetValue(sample, null);

        // EXPLICIT TAGS: only note has a context tag; the rest use universal tags.
        var expectedWithoutOptional = new byte[]
        {
            0x30, 0x35,
            0xA0, 0x03, 0x0C, 0x01, 0x4E,
            0x03, 0x02, 0x07, 0x80,
            0x13, 0x03, 0x41, 0x6E, 0x6E,
            0x16, 0x05, 0x61, 0x40, 0x62, 0x2E, 0x63,
            0x17, 0x0D,
            0x31, 0x37, 0x30, 0x31, 0x30, 0x32, 0x30, 0x33, 0x30, 0x34, 0x30, 0x35, 0x5A,
            0x18, 0x0F,
            0x32, 0x30, 0x31, 0x37, 0x30, 0x31, 0x30, 0x32, 0x30, 0x33, 0x30, 0x34, 0x30, 0x35, 0x5A
        };

        var writer = new Asn1Writer(Asn1Encoding.Der);
        sampleType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(sample, new object[] { writer });
        var encoded = writer.Encode();
        Assert.Equal(expectedWithoutOptional, encoded);

        var decoded = sampleType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(encoded, Asn1Encoding.Der) })!;
        Assert.Null(sampleType.GetProperty("Nickname")!.GetValue(decoded));
        Assert.Equal("Ann", sampleType.GetProperty("Name")!.GetValue(decoded));
        Assert.Equal(utc, sampleType.GetProperty("NotBefore")!.GetValue(decoded));

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        sampleType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decoded, new object[] { rewrite });
        Assert.Equal(expectedWithoutOptional, rewrite.Encode());

        sampleType.GetProperty("Nickname")!.SetValue(sample, "X");
        var withOptional = new Asn1Writer(Asn1Encoding.Der);
        sampleType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(sample, new object[] { withOptional });
        var encodedOptional = withOptional.Encode();
        Assert.Equal(0x38, encodedOptional[1]);
        Assert.Equal(new byte[] { 0x0C, 0x01, 0x58 }, encodedOptional.AsSpan(encodedOptional.Length - 3).ToArray());

        var decodedOptional = sampleType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(encodedOptional, Asn1Encoding.Der) })!;
        Assert.Equal("X", sampleType.GetProperty("Nickname")!.GetValue(decodedOptional));

        var broken = (byte[])encoded.Clone();
        broken[2] = 0xA1; // wrong explicit tag
        var ex = Assert.Throws<TargetInvocationException>(() =>
            sampleType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
                .Invoke(null, new object[] { new Asn1Reader(broken, Asn1Encoding.Der) }));
        Assert.IsType<Asn1Exception>(ex.InnerException);
    }

    [Fact]
    public void GeneratedCSharp_SetAndSetOf_RoundTripAndDerOrder()
    {
        const string asn = @"
SetMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
Bag ::= SET {
  a INTEGER,
  b [0] BOOLEAN OPTIONAL
}
List ::= SET OF INTEGER
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("WriteSet", source);
        Assert.Contains("WriteSetOf", source);
        Assert.Contains("Asn1Tag.Set", source);

        var assembly = CompileGenerated(source);
        var bagType = assembly.GetType("SetMod.Bag")!;
        var listType = assembly.GetType("SetMod.List")!;

        var bag = Activator.CreateInstance(bagType)!;
        bagType.GetProperty("A")!.SetValue(bag, new BigInteger(42));
        bagType.GetProperty("B")!.SetValue(bag, true);

        var expectedWithOptional = new byte[]
        {
            0x31, 0x08,
            0x02, 0x01, 0x2A,
            0xA0, 0x03, 0x01, 0x01, 0xFF
        };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        bagType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(bag, new object[] { writer });
        Assert.Equal(expectedWithOptional, writer.Encode());

        var decoded = bagType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expectedWithOptional, Asn1Encoding.Der) })!;
        Assert.Equal(new BigInteger(42), bagType.GetProperty("A")!.GetValue(decoded));
        Assert.Equal(true, bagType.GetProperty("B")!.GetValue(decoded));

        // BER may present components in reverse tag order.
        var berReversed = new byte[]
        {
            0x31, 0x08,
            0xA0, 0x03, 0x01, 0x01, 0xFF,
            0x02, 0x01, 0x2A
        };
        var fromBer = bagType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(berReversed, Asn1Encoding.Ber) })!;
        Assert.Equal(new BigInteger(42), bagType.GetProperty("A")!.GetValue(fromBer));
        Assert.Equal(true, bagType.GetProperty("B")!.GetValue(fromBer));
        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        bagType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(fromBer, new object[] { rewrite });
        Assert.Equal(expectedWithOptional, rewrite.Encode());

        bagType.GetProperty("B")!.SetValue(bag, null);
        var withoutOptional = new byte[] { 0x31, 0x03, 0x02, 0x01, 0x2A };
        var writerNoOpt = new Asn1Writer(Asn1Encoding.Der);
        bagType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(bag, new object[] { writerNoOpt });
        Assert.Equal(withoutOptional, writerNoOpt.Encode());
        var decodedNoOpt = bagType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(withoutOptional, Asn1Encoding.Der) })!;
        Assert.Null(bagType.GetProperty("B")!.GetValue(decodedNoOpt));

        var unknown = new byte[] { 0x31, 0x02, 0x05, 0x00 };
        var unknownEx = Assert.Throws<TargetInvocationException>(() =>
            bagType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
                .Invoke(null, new object[] { new Asn1Reader(unknown, Asn1Encoding.Der) }));
        Assert.IsType<Asn1Exception>(unknownEx.InnerException);

        var list = Activator.CreateInstance(listType)!;
        var items = (System.Collections.IList)listType.GetProperty("Items")!.GetValue(list)!;
        items.Add(new BigInteger(2));
        items.Add(new BigInteger(1));
        var expectedSetOf = new byte[] { 0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02 };
        var listWriter = new Asn1Writer(Asn1Encoding.Der);
        listType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(list, new object[] { listWriter });
        Assert.Equal(expectedSetOf, listWriter.Encode());

        var decodedList = listType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expectedSetOf, Asn1Encoding.Der) })!;
        var decodedItems = (System.Collections.IList)listType.GetProperty("Items")!.GetValue(decodedList)!;
        Assert.Equal(2, decodedItems.Count);
        Assert.Equal(new BigInteger(1), decodedItems[0]);
        Assert.Equal(new BigInteger(2), decodedItems[1]);

        var listRewrite = new Asn1Writer(Asn1Encoding.Der);
        listType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decodedList, new object[] { listRewrite });
        Assert.Equal(expectedSetOf, listRewrite.Encode());
    }

    [Fact]
    public void GeneratedCSharp_Any_RoundTripsAlgorithmIdentifierAndAttributeValue()
    {
        const string asn = @"
AnyMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
AlgorithmIdentifier ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters ANY DEFINED BY algorithm OPTIONAL
}
AttributeValue ::= ANY
AttributeTypeAndValue ::= SEQUENCE {
  type OBJECT IDENTIFIER,
  value AttributeValue
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("Asn1Any", source);
        Assert.Contains("if (!inner.Eof)", source);
        Assert.Contains("WriteAny", source);

        var assembly = CompileGenerated(source);
        var algType = assembly.GetType("AnyMod.AlgorithmIdentifier")!;
        var attrType = assembly.GetType("AnyMod.AttributeTypeAndValue")!;
        var valueType = assembly.GetType("AnyMod.AttributeValue")!;

        // OID 1.2.840.113549.1.1.1 = rsaEncryption, no parameters
        var alg = Activator.CreateInstance(algType)!;
        algType.GetProperty("Algorithm")!.SetValue(alg, "1.2.840.113549.1.1.1");
        algType.GetProperty("Parameters")!.SetValue(alg, null);

        var expectedNoParams = new byte[]
        {
            0x30, 0x0B,
            0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01
        };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        algType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(alg, new object[] { writer });
        Assert.Equal(expectedNoParams, writer.Encode());

        var decodedNoParams = algType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expectedNoParams, Asn1Encoding.Der) })!;
        Assert.Equal("1.2.840.113549.1.1.1", algType.GetProperty("Algorithm")!.GetValue(decodedNoParams));
        Assert.Null(algType.GetProperty("Parameters")!.GetValue(decodedNoParams));

        // With NULL parameters
        var nullAny = new Asn1Any(Asn1Tag.Null, Array.Empty<byte>());
        algType.GetProperty("Parameters")!.SetValue(alg, nullAny);
        var expectedWithNull = new byte[]
        {
            0x30, 0x0D,
            0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01,
            0x05, 0x00
        };
        var writerWith = new Asn1Writer(Asn1Encoding.Der);
        algType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(alg, new object[] { writerWith });
        Assert.Equal(expectedWithNull, writerWith.Encode());

        var decodedWith = algType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expectedWithNull, Asn1Encoding.Der) })!;
        var parameters = (Asn1Any)algType.GetProperty("Parameters")!.GetValue(decodedWith)!;
        Assert.Equal(Asn1Tag.Null, parameters.Tag);
        Assert.Empty(parameters.Contents);

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        algType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decodedWith, new object[] { rewrite });
        Assert.Equal(expectedWithNull, rewrite.Encode());

        // Named ANY alias via AttributeTypeAndValue
        var attr = Activator.CreateInstance(attrType)!;
        attrType.GetProperty("Type")!.SetValue(attr, "2.5.4.3");
        var attrValue = Activator.CreateInstance(valueType)!;
        valueType.GetProperty("Value")!.SetValue(attrValue, new Asn1Any(Asn1Tag.Utf8String, Encoding.UTF8.GetBytes("Ann")));
        attrType.GetProperty("Value")!.SetValue(attr, attrValue);

        var expectedAttr = new byte[]
        {
            0x30, 0x0A,
            0x06, 0x03, 0x55, 0x04, 0x03,
            0x0C, 0x03, 0x41, 0x6E, 0x6E
        };
        var attrWriter = new Asn1Writer(Asn1Encoding.Der);
        attrType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(attr, new object[] { attrWriter });
        Assert.Equal(expectedAttr, attrWriter.Encode());

        var decodedAttr = attrType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expectedAttr, Asn1Encoding.Der) })!;
        var decodedValueWrapper = attrType.GetProperty("Value")!.GetValue(decodedAttr)!;
        var decodedAny = (Asn1Any)valueType.GetProperty("Value")!.GetValue(decodedValueWrapper)!;
        Assert.Equal(Asn1Tag.Utf8String, decodedAny.Tag);
        Assert.Equal("Ann", Encoding.UTF8.GetString(decodedAny.Contents));

        var broken = new byte[] { 0x30, 0x02, 0x05, 0x00 };
        var ex = Assert.Throws<TargetInvocationException>(() =>
            algType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
                .Invoke(null, new object[] { new Asn1Reader(broken, Asn1Encoding.Der) }));
        Assert.IsType<Asn1Exception>(ex.InnerException);
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

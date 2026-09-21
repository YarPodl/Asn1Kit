using System;
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
        type!.GetProperty("Id")!.SetValue(person, Asn1Integer.FromInt32(42));
        type.GetProperty("Name")!.SetValue(person, (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes("Ann"));
        type.GetProperty("Nickname")!.SetValue(person, (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes("A"));

        var writer = new Asn1Writer(Asn1Encoding.Der);
        type.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(person, new object[] { writer });
        var encoded = writer.Encode();

        var reader = new Asn1Reader(encoded, Asn1Encoding.Der);
        var decoded = type.GetMethod("Decode", new[] { typeof(Asn1Reader) })!.Invoke(null, new object[] { reader })!;
        Assert.Equal(Asn1Integer.FromInt32(42), type.GetProperty("Id")!.GetValue(decoded));
        Assert.Equal("Ann", Encoding.UTF8.GetString(((ReadOnlyMemory<byte>)type.GetProperty("Name")!.GetValue(decoded)!).Span));
        Assert.Equal("A", Encoding.UTF8.GetString(((ReadOnlyMemory<byte>)type.GetProperty("Nickname")!.GetValue(decoded)!).Span));
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
        Assert.Contains("KeyUsageFlags", source);
        Assert.Contains("[Flags]", source);
        Assert.DoesNotContain("Bit_DigitalSignature", source);
        Assert.Contains("WriteString", source);
        Assert.Contains("WriteTime", source);
        Assert.Contains("Asn1TimeForm.Generalized, 3)", source);
        Assert.Contains("WriteBitString", source);
        Assert.Contains("WriteExplicit", source);

        var assembly = CompileGenerated(source);
        var sampleType = assembly.GetType("PrimitivesModule.Sample")!;
        var keyUsageType = assembly.GetType("PrimitivesModule.KeyUsage")!;
        var flagsEnum = assembly.GetType("PrimitivesModule.KeyUsageFlags")!;
        Assert.Equal(1, Convert.ToInt32(Enum.Parse(flagsEnum, "DigitalSignature")));

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
Holder ::= SEQUENCE {
  items List
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("WriteSet", source);
        Assert.Contains("WriteSetOf", source);
        Assert.Contains("Asn1Tag.Set", source);
        Assert.DoesNotContain("class List", source);
        Assert.Contains("ASN.1 alias List ::= SET OF INTEGER.", source);
        Assert.Contains("List<Asn1Integer> Items", source);

        var assembly = CompileGenerated(source);
        var bagType = assembly.GetType("SetMod.Bag")!;
        var holderType = assembly.GetType("SetMod.Holder")!;
        Assert.Null(assembly.GetType("SetMod.List"));

        var bag = Activator.CreateInstance(bagType)!;
        bagType.GetProperty("A")!.SetValue(bag, Asn1Integer.FromInt32(42));
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
        Assert.Equal(Asn1Integer.FromInt32(42), bagType.GetProperty("A")!.GetValue(decoded));
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
        Assert.Equal(Asn1Integer.FromInt32(42), bagType.GetProperty("A")!.GetValue(fromBer));
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

        var holder = Activator.CreateInstance(holderType)!;
        var items = new List<Asn1Integer>
        {
            Asn1Integer.FromInt32(2),
            Asn1Integer.FromInt32(1),
        };
        holderType.GetProperty("Items")!.SetValue(holder, items);
        // SET OF inside SEQUENCE: outer SEQUENCE tag, inner SET OF sorted in DER.
        var expectedHolder = new byte[]
        {
            0x30, 0x08,
            0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02
        };
        var listWriter = new Asn1Writer(Asn1Encoding.Der);
        holderType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(holder, new object[] { listWriter });
        Assert.Equal(expectedHolder, listWriter.Encode());

        var decodedHolder = holderType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expectedHolder, Asn1Encoding.Der) })!;
        var decodedItems = (List<Asn1Integer>)holderType.GetProperty("Items")!.GetValue(decodedHolder)!;
        Assert.Equal(2, decodedItems.Count);
        Assert.Equal(Asn1Integer.FromInt32(1), decodedItems[0]);
        Assert.Equal(Asn1Integer.FromInt32(2), decodedItems[1]);

        var listRewrite = new Asn1Writer(Asn1Encoding.Der);
        holderType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decodedHolder, new object[] { listRewrite });
        Assert.Equal(expectedHolder, listRewrite.Encode());
    }

    [Fact]
    public void GeneratedCSharp_CollapsesSequenceOfAliasToList()
    {
        const string asn = @"
OfMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
Seq ::= SEQUENCE OF INTEGER
Holder ::= SEQUENCE {
  values Seq
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;

        Assert.DoesNotContain("class Seq", source);
        Assert.Contains("ASN.1 alias Seq ::= SEQUENCE OF INTEGER.", source);
        Assert.Contains("List<Asn1Integer> Values", source);
        Assert.Contains("WriteSequenceOf", source);
        Assert.Contains("ReadSequenceOf", source);

        var assembly = CompileGenerated(source);
        Assert.Null(assembly.GetType("OfMod.Seq"));
        var holderType = assembly.GetType("OfMod.Holder")!;

        var holder = Activator.CreateInstance(holderType)!;
        holderType.GetProperty("Values")!.SetValue(
            holder,
            new List<Asn1Integer> { Asn1Integer.FromInt32(1), Asn1Integer.FromInt32(2) });

        var expected = new byte[]
        {
            0x30, 0x08,
            0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02
        };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        holderType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(holder, new object[] { writer });
        Assert.Equal(expected, writer.Encode());

        var decoded = holderType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expected, Asn1Encoding.Der) })!;
        var values = (List<Asn1Integer>)holderType.GetProperty("Values")!.GetValue(decoded)!;
        Assert.Equal(2, values.Count);
        Assert.Equal(Asn1Integer.FromInt32(1), values[0]);
        Assert.Equal(Asn1Integer.FromInt32(2), values[1]);
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
        Assert.DoesNotContain("class AttributeValue", source);
        Assert.Contains("ASN.1 alias AttributeValue ::= ANY.", source);

        var assembly = CompileGenerated(source);
        var algType = assembly.GetType("AnyMod.AlgorithmIdentifier")!;
        var attrType = assembly.GetType("AnyMod.AttributeTypeAndValue")!;
        Assert.Null(assembly.GetType("AnyMod.AttributeValue"));

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
        Assert.Equal(0, parameters.Span.Length);

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        algType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decodedWith, new object[] { rewrite });
        Assert.Equal(expectedWithNull, rewrite.Encode());

        // Collapsed ANY alias: Value is Asn1Any directly
        var attr = Activator.CreateInstance(attrType)!;
        attrType.GetProperty("Type")!.SetValue(attr, "2.5.4.3");
        attrType.GetProperty("Value")!.SetValue(attr, new Asn1Any(Asn1Tag.Utf8String, Encoding.UTF8.GetBytes("Ann")));

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
        var decodedAny = (Asn1Any)attrType.GetProperty("Value")!.GetValue(decodedAttr)!;
        Assert.Equal(Asn1Tag.Utf8String, decodedAny.Tag);
        Assert.Equal("Ann", Encoding.UTF8.GetString(decodedAny.Span));

        var broken = new byte[] { 0x30, 0x02, 0x05, 0x00 };
        var ex = Assert.Throws<TargetInvocationException>(() =>
            algType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
                .Invoke(null, new object[] { new Asn1Reader(broken, Asn1Encoding.Der) }));
        Assert.IsType<Asn1Exception>(ex.InnerException);
    }

    [Fact]
    public void GeneratedCSharp_OpenTypeBindings_ResolveNullAndFallbackToAsn1Any()
    {
        const string asn = @"
OpenMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
CPSuri ::= IA5String
PolicyQualifierInfo ::= SEQUENCE {
  policyQualifierId OBJECT IDENTIFIER,
  qualifier ANY DEFINED BY policyQualifierId
}
AlgorithmIdentifier ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters ANY DEFINED BY algorithm OPTIONAL
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        OpenTypeBindings.ApplyJson(document, @"{
  ""OpenMod.AlgorithmIdentifier.parameters"": [
    { ""key"": ""1.2.840.113549.1.1.11"", ""type"": { ""kind"": ""null"" } }
  ],
  ""OpenMod.PolicyQualifierInfo.qualifier"": [
    { ""key"": ""1.3.6.1.5.5.7.2.1"", ""type"": { ""kind"": ""ref"", ""name"": ""CPSuri"" } }
  ]
}");
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("AlgorithmIdentifier_Parameters", source);
        Assert.Contains("FromNull", source);
        Assert.Contains("FromUnknown", source);
        Assert.DoesNotContain("ParametersKind", source);
        Assert.Contains("1.2.840.113549.1.1.11", source);

        var assembly = CompileGenerated(source);
        var algType = assembly.GetType("OpenMod.AlgorithmIdentifier")!;
        var paramsType = assembly.GetType("OpenMod.AlgorithmIdentifier_Parameters")!;
        var pqiType = assembly.GetType("OpenMod.PolicyQualifierInfo")!;
        var qualifierType = assembly.GetType("OpenMod.PolicyQualifierInfo_Qualifier")!;
        Assert.Null(assembly.GetType("OpenMod.AlgorithmIdentifier_ParametersKind"));
        Assert.Null(assembly.GetType("OpenMod.PolicyQualifierInfo_QualifierKind"));

        var withNull = new byte[]
        {
            0x30, 0x0D,
            0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x0B,
            0x05, 0x00
        };
        var decodedAlg = algType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(withNull, Asn1Encoding.Der) })!;
        Assert.Equal("1.2.840.113549.1.1.11", algType.GetProperty("Algorithm")!.GetValue(decodedAlg));
        var parameters = algType.GetProperty("Parameters")!.GetValue(decodedAlg)!;
        Assert.Equal(Asn1Null.Value, parameters.GetType().GetProperty("Null")!.GetValue(parameters));
        Assert.Null(parameters.GetType().GetProperty("Unknown")!.GetValue(parameters));

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        algType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decodedAlg, new object[] { rewrite });
        Assert.Equal(withNull, rewrite.Encode());

        var unknownOid = new byte[]
        {
            0x30, 0x0D,
            0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01,
            0x05, 0x00
        };
        var decodedUnknown = algType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(unknownOid, Asn1Encoding.Der) })!;
        var unknownParams = algType.GetProperty("Parameters")!.GetValue(decodedUnknown)!;
        Assert.Null(unknownParams.GetType().GetProperty("Null")!.GetValue(unknownParams));
        var unknownAny = Assert.IsType<Asn1Any>(unknownParams.GetType().GetProperty("Unknown")!.GetValue(unknownParams)!);
        Assert.Equal(Asn1Tag.Null, unknownAny.Tag);

        // Soft mismatch: known OID but content is INTEGER, not NULL
        var mismatch = new byte[]
        {
            0x30, 0x0E,
            0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x0B,
            0x02, 0x01, 0x01
        };
        var softDecoded = algType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(mismatch, Asn1Encoding.Der) })!;
        var softParams = algType.GetProperty("Parameters")!.GetValue(softDecoded)!;
        Assert.Null(softParams.GetType().GetProperty("Null")!.GetValue(softParams));
        Assert.NotNull(softParams.GetType().GetProperty("Unknown")!.GetValue(softParams));

        var cps = new byte[]
        {
            0x30, 0x12,
            0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x02, 0x01,
            0x16, 0x06, 0x68, 0x74, 0x74, 0x70, 0x73, 0x3A
        };
        var decodedPqi = pqiType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(cps, Asn1Encoding.Der) })!;
        Assert.Equal("1.3.6.1.5.5.7.2.1", pqiType.GetProperty("PolicyQualifierId")!.GetValue(decodedPqi));
        var qualifier = pqiType.GetProperty("Qualifier")!.GetValue(decodedPqi)!;
        Assert.Equal("https:", qualifier.GetType().GetProperty("CPSuri")!.GetValue(qualifier));
        Assert.Null(qualifier.GetType().GetProperty("Unknown")!.GetValue(qualifier));
        Assert.NotNull(paramsType);
        Assert.NotNull(qualifierType);
    }

    [Fact]
    public void GeneratedCSharp_OpenTypeBindings_StrictMismatchThrows()
    {
        const string asn = @"
StrictMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
AlgorithmIdentifier ::= SEQUENCE {
  algorithm OBJECT IDENTIFIER,
  parameters ANY DEFINED BY algorithm OPTIONAL
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        document.Modules[0].Options = IrOptions.SetOpenTypeMismatch(document.Modules[0].Options, "strict");
        OpenTypeBindings.ApplyJson(document, @"{
  ""StrictMod.AlgorithmIdentifier.parameters"": [
    { ""key"": ""1.2.840.113549.1.1.11"", ""type"": { ""kind"": ""null"" } }
  ]
}");
        var source = new CSharpBackend().Generate(document).Single().Contents;
        var assembly = CompileGenerated(source);
        var algType = assembly.GetType("StrictMod.AlgorithmIdentifier")!;
        var mismatch = new byte[]
        {
            0x30, 0x0E,
            0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x0B,
            0x02, 0x01, 0x01
        };
        var ex = Assert.Throws<TargetInvocationException>(() =>
            algType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
                .Invoke(null, new object[] { new Asn1Reader(mismatch, Asn1Encoding.Der) }));
        Assert.IsType<Asn1Exception>(ex.InnerException);
        Assert.Contains("does not match bound type", ex.InnerException!.Message);
    }

    [Fact]
    public void GeneratedCSharp_CollapsesAliasesAndKeepsNamedBitStringFlags()
    {
        const string asn = @"
AliasMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
AttributeType ::= OBJECT IDENTIFIER
Serial ::= INTEGER
Rdn ::= SEQUENCE { attr AttributeType }
Name ::= Rdn
KeyUsage ::= BIT STRING {
  digitalSignature(0),
  keyEncipherment(2)
}
Holder ::= SEQUENCE {
  type AttributeType,
  id Serial,
  who Name,
  usage KeyUsage
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;

        Assert.DoesNotContain("class AttributeType", source);
        Assert.DoesNotContain("class Serial", source);
        Assert.DoesNotContain("class Name", source);
        Assert.Contains("class Rdn", source);
        Assert.Contains("class KeyUsage", source);
        Assert.Contains("enum KeyUsageFlags", source);
        Assert.Contains("ASN.1 alias AttributeType ::= OBJECT IDENTIFIER.", source);
        Assert.Contains("ASN.1 alias Serial ::= INTEGER.", source);
        Assert.Contains("ASN.1 alias Name ::= Rdn.", source);
        Assert.Contains("DigitalSignature = 1 << 0", source);
        Assert.Contains("KeyEncipherment = 1 << 2", source);
        Assert.DoesNotContain("Bit_DigitalSignature", source);

        var assembly = CompileGenerated(source);
        Assert.Null(assembly.GetType("AliasMod.AttributeType"));
        Assert.Null(assembly.GetType("AliasMod.Serial"));
        Assert.Null(assembly.GetType("AliasMod.Name"));

        var holderType = assembly.GetType("AliasMod.Holder")!;
        var rdnType = assembly.GetType("AliasMod.Rdn")!;
        var keyUsageType = assembly.GetType("AliasMod.KeyUsage")!;
        var flagsEnum = assembly.GetType("AliasMod.KeyUsageFlags")!;

        Assert.Equal(typeof(string), holderType.GetProperty("Type")!.PropertyType);
        Assert.Equal(typeof(Asn1Integer), holderType.GetProperty("Id")!.PropertyType);
        Assert.Equal(rdnType, holderType.GetProperty("Who")!.PropertyType);
        Assert.Equal(keyUsageType, holderType.GetProperty("Usage")!.PropertyType);

        var digital = Enum.Parse(flagsEnum, "DigitalSignature");
        var encipher = Enum.Parse(flagsEnum, "KeyEncipherment");
        var combined = Enum.ToObject(flagsEnum, Convert.ToInt32(digital) | Convert.ToInt32(encipher));

        var fromFlags = keyUsageType.GetMethod("FromFlags")!.Invoke(null, new[] { combined })!;
        var bits = (Asn1BitString)fromFlags;
        Assert.True(bits[0]);
        Assert.True(bits[2]);
        Assert.Equal(3, bits.BitLength);

        var toFlags = keyUsageType.GetMethod("ToFlags")!.Invoke(null, new object[] { bits })!;
        Assert.Equal(combined, toFlags);

        var usage = Activator.CreateInstance(keyUsageType)!;
        keyUsageType.GetProperty("Flags")!.SetValue(usage, combined);
        Assert.Equal(bits, keyUsageType.GetProperty("Value")!.GetValue(usage));

        var rdn = Activator.CreateInstance(rdnType)!;
        rdnType.GetProperty("Attr")!.SetValue(rdn, "2.5.4.3");

        var holder = Activator.CreateInstance(holderType)!;
        holderType.GetProperty("Type")!.SetValue(holder, "1.2.3");
        holderType.GetProperty("Id")!.SetValue(holder, Asn1Integer.FromInt32(7));
        holderType.GetProperty("Who")!.SetValue(holder, rdn);
        holderType.GetProperty("Usage")!.SetValue(holder, usage);

        var writer = new Asn1Writer(Asn1Encoding.Der);
        holderType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(holder, new object[] { writer });
        var encoded = writer.Encode();

        var decoded = holderType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(encoded, Asn1Encoding.Der) })!;
        Assert.Equal("1.2.3", holderType.GetProperty("Type")!.GetValue(decoded));
        Assert.Equal(Asn1Integer.FromInt32(7), holderType.GetProperty("Id")!.GetValue(decoded));
        var decodedRdn = holderType.GetProperty("Who")!.GetValue(decoded)!;
        Assert.Equal("2.5.4.3", rdnType.GetProperty("Attr")!.GetValue(decodedRdn));
        var decodedUsage = holderType.GetProperty("Usage")!.GetValue(decoded)!;
        Assert.Equal(combined, keyUsageType.GetProperty("Flags")!.GetValue(decodedUsage));

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        holderType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decoded, new object[] { rewrite });
        Assert.Equal(encoded, rewrite.Encode());
    }

    [Fact]
    public void GeneratedCSharp_CollapsesSingleAlternativeChoice()
    {
        const string asn = @"
ChoiceMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
Relative ::= SEQUENCE { id INTEGER }
RdnSequence ::= SEQUENCE OF Relative
Name ::= CHOICE { rdnSequence RdnSequence }
Multi ::= CHOICE { a INTEGER, b UTF8String }
Holder ::= SEQUENCE {
  who Name,
  pick Multi
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;

        Assert.DoesNotContain("class Name", source);
        Assert.DoesNotContain("NameKind", source);
        Assert.Contains("ASN.1 alias Name ::= CHOICE { rdnSequence RdnSequence }.", source);
        Assert.Contains("List<Relative> Who", source);
        Assert.Contains("class Multi", source);
        Assert.Contains("enum MultiKind", source);
        Assert.Contains("public static Multi FromA(Asn1Integer a) => new Multi", source);
        Assert.Contains("public static Multi FromB(string b) => new Multi", source);
        Assert.Contains("public Asn1Integer? A { get; private set; }", source);
        Assert.Contains("public string? B { get; private set; }", source);
        Assert.DoesNotContain("public Asn1Integer Value", source);

        var assembly = CompileGenerated(source);
        Assert.Null(assembly.GetType("ChoiceMod.Name"));
        Assert.NotNull(assembly.GetType("ChoiceMod.Multi"));

        var holderType = assembly.GetType("ChoiceMod.Holder")!;
        var relativeType = assembly.GetType("ChoiceMod.Relative")!;
        Assert.Equal(typeof(List<>).MakeGenericType(relativeType), holderType.GetProperty("Who")!.PropertyType);

        var relative = Activator.CreateInstance(relativeType)!;
        relativeType.GetProperty("Id")!.SetValue(relative, Asn1Integer.FromInt32(7));

        var who = Activator.CreateInstance(typeof(List<>).MakeGenericType(relativeType))!;
        typeof(List<>).MakeGenericType(relativeType).GetMethod("Add")!.Invoke(who, new[] { relative });

        var multiType = assembly.GetType("ChoiceMod.Multi")!;
        var multiKind = assembly.GetType("ChoiceMod.MultiKind")!;
        var multi = multiType.GetMethod("FromA", new[] { typeof(Asn1Integer) })!
            .Invoke(null, new object[] { Asn1Integer.FromInt32(7) })!;
        Assert.Equal(Enum.Parse(multiKind, "A"), multiType.GetProperty("Kind")!.GetValue(multi));
        Assert.Equal(Asn1Integer.FromInt32(7), multiType.GetProperty("A")!.GetValue(multi));
        Assert.Null(multiType.GetProperty("B")!.GetValue(multi));

        var fromB = multiType.GetMethod("FromB", new[] { typeof(string) })!
            .Invoke(null, new object[] { "hi" })!;
        Assert.Equal(Enum.Parse(multiKind, "B"), multiType.GetProperty("Kind")!.GetValue(fromB));
        Assert.Equal("hi", multiType.GetProperty("B")!.GetValue(fromB));
        var fromBWriter = new Asn1Writer(Asn1Encoding.Der);
        multiType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(fromB, new object[] { fromBWriter });
        Assert.Equal(new byte[] { 0x0C, 0x02, 0x68, 0x69 }, fromBWriter.Encode());

        var holder = Activator.CreateInstance(holderType)!;
        holderType.GetProperty("Who")!.SetValue(holder, who);
        holderType.GetProperty("Pick")!.SetValue(holder, multi);

        var expected = new byte[]
        {
            0x30, 0x0A,
            0x30, 0x05, 0x30, 0x03, 0x02, 0x01, 0x07,
            0x02, 0x01, 0x07
        };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        holderType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(holder, new object[] { writer });
        Assert.Equal(expected, writer.Encode());

        var decoded = holderType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expected, Asn1Encoding.Der) })!;
        var decodedWho = (System.Collections.IList)holderType.GetProperty("Who")!.GetValue(decoded)!;
        Assert.Equal(1, decodedWho.Count);
        Assert.Equal(Asn1Integer.FromInt32(7), relativeType.GetProperty("Id")!.GetValue(decodedWho[0]));
        var decodedPick = holderType.GetProperty("Pick")!.GetValue(decoded)!;
        Assert.Equal(Enum.Parse(multiKind, "A"), multiType.GetProperty("Kind")!.GetValue(decodedPick));
        Assert.Equal(Asn1Integer.FromInt32(7), multiType.GetProperty("A")!.GetValue(decodedPick));
    }

    [Fact]
    public void GeneratedCSharp_CollapsesHomogeneousChoiceToValue()
    {
        const string asn = @"
HomogeneousMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
When ::= CHOICE { u UTCTime, g GeneralizedTime }
Text ::= CHOICE { a UTF8String, b PrintableString }
Bag ::= SEQUENCE { when When, text Text }
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;

        Assert.Contains("enum WhenKind", source);
        Assert.Contains("public DateTimeOffset Value { get; private set; }", source);
        Assert.DoesNotContain("public DateTimeOffset? U { get; private set; }", source);
        Assert.DoesNotContain("public DateTimeOffset? G { get; private set; }", source);
        Assert.Contains("public static When FromU(DateTimeOffset u) => new When", source);
        Assert.Contains("Value = u,", source);

        Assert.Contains("enum TextKind", source);
        Assert.Contains("public string Value { get; private set; } = \"\";", source);
        Assert.DoesNotContain("public string? A { get; private set; }", source);
        Assert.DoesNotContain("public string? B { get; private set; }", source);
        Assert.Contains("public static Text FromA(string a) => new Text", source);

        var assembly = CompileGenerated(source);
        var whenType = assembly.GetType("HomogeneousMod.When")!;
        var whenKind = assembly.GetType("HomogeneousMod.WhenKind")!;
        var textType = assembly.GetType("HomogeneousMod.Text")!;
        var textKind = assembly.GetType("HomogeneousMod.TextKind")!;
        var formType = assembly.GetType("HomogeneousMod.Bag")!;

        var instant = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var when = whenType.GetMethod("FromU", new[] { typeof(DateTimeOffset) })!
            .Invoke(null, new object[] { instant })!;
        Assert.Equal(Enum.Parse(whenKind, "U"), whenType.GetProperty("Kind")!.GetValue(when));
        Assert.Equal(instant, whenType.GetProperty("Value")!.GetValue(when));

        var text = textType.GetMethod("FromA", new[] { typeof(string) })!
            .Invoke(null, new object[] { "hi" })!;
        Assert.Equal(Enum.Parse(textKind, "A"), textType.GetProperty("Kind")!.GetValue(text));
        Assert.Equal("hi", textType.GetProperty("Value")!.GetValue(text));

        var form = Activator.CreateInstance(formType)!;
        formType.GetProperty("When")!.SetValue(form, when);
        formType.GetProperty("Text")!.SetValue(form, text);

        var writer = new Asn1Writer(Asn1Encoding.Der);
        formType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(form, new object[] { writer });
        var encoded = writer.Encode();

        var decoded = formType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(encoded, Asn1Encoding.Der) })!;
        var decodedWhen = formType.GetProperty("When")!.GetValue(decoded)!;
        var decodedText = formType.GetProperty("Text")!.GetValue(decoded)!;
        Assert.Equal(Enum.Parse(whenKind, "U"), whenType.GetProperty("Kind")!.GetValue(decodedWhen));
        Assert.Equal(instant, whenType.GetProperty("Value")!.GetValue(decodedWhen));
        Assert.Equal(Enum.Parse(textKind, "A"), textType.GetProperty("Kind")!.GetValue(decodedText));
        Assert.Equal("hi", textType.GetProperty("Value")!.GetValue(decodedText));

        var roundTrip = new Asn1Writer(Asn1Encoding.Der);
        formType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decoded, new object[] { roundTrip });
        Assert.Equal(encoded, roundTrip.Encode());

        var general = whenType.GetMethod("FromG", new[] { typeof(DateTimeOffset) })!
            .Invoke(null, new object[] { instant })!;
        Assert.Equal(Enum.Parse(whenKind, "G"), whenType.GetProperty("Kind")!.GetValue(general));
        var generalWriter = new Asn1Writer(Asn1Encoding.Der);
        whenType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(general, new object[] { generalWriter });
        var generalBytes = generalWriter.Encode();
        Assert.Equal(0x18, generalBytes[0]); // GeneralizedTime

        var printable = textType.GetMethod("FromB", new[] { typeof(string) })!
            .Invoke(null, new object[] { "OK" })!;
        var printableWriter = new Asn1Writer(Asn1Encoding.Der);
        textType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(printable, new object[] { printableWriter });
        Assert.Equal(new byte[] { 0x13, 0x02, 0x4F, 0x4B }, printableWriter.Encode());
    }

    [Fact]
    public void GeneratedCSharp_Enumerated_EmitsEnumAndRoundTrips()
    {
        const string asn = @"
EnumMod DEFINITIONS EXPLICIT TAGS ::= BEGIN
Reason ::= ENUMERATED {
  ok(0),
  bad(1),
  worse(10)
}
Big ::= ENUMERATED { tiny(0), huge(2147483648) }
Entry ::= SEQUENCE {
  reason Reason,
  note UTF8String OPTIONAL,
  inline ENUMERATED { a(0), b(2) }
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var source = new CSharpBackend().Generate(document).Single().Contents;

        Assert.Contains("public enum Reason", source);
        Assert.Contains("Ok = 0", source);
        Assert.Contains("Bad = 1", source);
        Assert.Contains("Worse = 10", source);
        Assert.Contains("public enum Big : long", source);
        Assert.Contains("Huge = 2147483648L", source);
        Assert.Contains("public enum Entry_Inline", source);
        Assert.Contains("WriteEnumerated", source);
        Assert.Contains("ReadEnumerated", source);
        Assert.Contains("Asn1Tag.Enumerated", source);
        Assert.DoesNotContain("class Reason", source);

        var assembly = CompileGenerated(source);
        var reasonType = assembly.GetType("EnumMod.Reason")!;
        Assert.True(reasonType.IsEnum);
        var entryType = assembly.GetType("EnumMod.Entry")!;
        var inlineType = assembly.GetType("EnumMod.Entry_Inline")!;
        Assert.True(inlineType.IsEnum);

        Assert.Equal(reasonType, entryType.GetProperty("Reason")!.PropertyType);
        Assert.Equal(typeof(string), entryType.GetProperty("Note")!.PropertyType);
        Assert.Equal(inlineType, entryType.GetProperty("Inline")!.PropertyType);

        var bad = Enum.Parse(reasonType, "Bad");
        var inlineB = Enum.Parse(inlineType, "B");
        var entry = Activator.CreateInstance(entryType)!;
        entryType.GetProperty("Reason")!.SetValue(entry, bad);
        entryType.GetProperty("Inline")!.SetValue(entry, inlineB);

        var expectedWithoutOptional = new byte[]
        {
            0x30, 0x06,
            0x0A, 0x01, 0x01,
            0x0A, 0x01, 0x02
        };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        entryType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(entry, new object[] { writer });
        Assert.Equal(expectedWithoutOptional, writer.Encode());

        var decoded = entryType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(expectedWithoutOptional, Asn1Encoding.Der) })!;
        Assert.Equal(bad, entryType.GetProperty("Reason")!.GetValue(decoded));
        Assert.Null(entryType.GetProperty("Note")!.GetValue(decoded));
        Assert.Equal(inlineB, entryType.GetProperty("Inline")!.GetValue(decoded));

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        entryType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(decoded, new object[] { rewrite });
        Assert.Equal(expectedWithoutOptional, rewrite.Encode());

        entryType.GetProperty("Note")!.SetValue(entry, "X");
        var withOptional = new Asn1Writer(Asn1Encoding.Der);
        entryType.GetMethod("Encode", new[] { typeof(Asn1Writer) })!.Invoke(entry, new object[] { withOptional });
        var encodedOptional = withOptional.Encode();
        Assert.Equal(
            new byte[] { 0x30, 0x09, 0x0A, 0x01, 0x01, 0x0C, 0x01, 0x58, 0x0A, 0x01, 0x02 },
            encodedOptional);

        var decodedOptional = entryType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(encodedOptional, Asn1Encoding.Der) })!;
        Assert.Equal("X", entryType.GetProperty("Note")!.GetValue(decodedOptional));

        // Soft: unknown enumerated number is accepted via cast.
        var unknown = new byte[] { 0x30, 0x06, 0x0A, 0x01, 0x07, 0x0A, 0x01, 0x00 };
        var decodedUnknown = entryType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
            .Invoke(null, new object[] { new Asn1Reader(unknown, Asn1Encoding.Der) })!;
        Assert.Equal(7, Convert.ToInt32(entryType.GetProperty("Reason")!.GetValue(decodedUnknown)));

        var wrongTag = new byte[] { 0x30, 0x06, 0x02, 0x01, 0x01, 0x0A, 0x01, 0x00 };
        var ex = Assert.Throws<TargetInvocationException>(() =>
            entryType.GetMethod("Decode", new[] { typeof(Asn1Reader) })!
                .Invoke(null, new object[] { new Asn1Reader(wrongTag, Asn1Encoding.Der) }));
        Assert.IsType<Asn1Exception>(ex.InnerException);
    }

    [Fact]
    public void IntegerRepresentation_InfersFixedWidthAndHonorsOptions()
    {
        const string asn = @"
IntMod DEFINITIONS ::= BEGIN
Small ::= INTEGER (0..100)
Wide ::= INTEGER (0..3000000000)
Open ::= INTEGER
Holder ::= SEQUENCE {
  a Small,
  b Wide,
  c Open
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("public int A { get; set; }", source);
        Assert.Contains("public uint B { get; set; }", source);
        Assert.Contains("public Asn1Integer C { get; set; }", source);
        Assert.DoesNotContain("Asn1Integer.FromInt32(0)", source);
        Assert.Contains("ReadInt32", source);
        Assert.Contains("ReadUInt32", source);
        Assert.Contains("ReadIntegerValue", source);

        document.Modules[0].Options = IrOptions.SetIntegerRepresentation(null, IrOptions.IntegerRepresentations.BigInt);
        var forced = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("public BigInteger A { get; set; }", forced);
        Assert.Contains("ReadInteger(", forced);

        var open = document.Modules[0].Types.Single(t => t.Name == "Open");
        open.Options = IrOptions.SetIntegerRepresentation(null, IrOptions.IntegerRepresentations.Int32);
        document.Modules[0].Options = null;
        var typedefOverride = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("public int C { get; set; }", typedefOverride);

        document.Modules[0].Options = IrOptions.SetIntegerRepresentation(null, "nope");
        var ex = Assert.Throws<NotSupportedException>(() => new CSharpBackend().Generate(document));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void NamedInteger_DefaultsToIntAndEmitsConstants()
    {
        const string asn = @"
NamedIntMod DEFINITIONS ::= BEGIN
Version ::= INTEGER { v1(0), v2(1), v3(2) }
Holder ::= SEQUENCE {
  version Version,
  inline INTEGER { a(1), b(2) }
}
END
";
        var document = new Asn1Compiler().CompileText(asn);
        var source = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("public static class Version", source);
        Assert.Contains("public const int V1 = 0;", source);
        Assert.Contains("public const int V2 = 1;", source);
        Assert.Contains("public const int V3 = 2;", source);
        Assert.Contains("public int Version { get; set; }", source);
        Assert.Contains("public static class Holder_Inline", source);
        Assert.Contains("public const int A = 1;", source);
        Assert.Contains("public int Inline { get; set; }", source);
        Assert.Contains("ReadInt32", source);
        Assert.DoesNotContain("Asn1Integer", source);

        document.Modules[0].Types.Single(t => t.Name == "Version").Options =
            IrOptions.SetIntegerRepresentation(null, IrOptions.IntegerRepresentations.Der);
        var overridden = new CSharpBackend().Generate(document).Single().Contents;
        Assert.Contains("public Asn1Integer Version { get; set; }", overridden);
        Assert.DoesNotContain("Asn1Integer.FromInt32(0)", overridden);
        Assert.Contains("public const int V1 = 0;", overridden);
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

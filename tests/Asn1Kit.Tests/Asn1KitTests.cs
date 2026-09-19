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

    [Fact]
    public void Set_TagIsUniversal17()
    {
        Assert.Equal(Asn1TagClass.Universal, Asn1Tag.Set.TagClass);
        Assert.Equal(17, Asn1Tag.Set.Number);
        Assert.True(Asn1Tag.Set.Constructed);
    }

    [Fact]
    public void WriteSetOf_DerSortsElementEncodings()
    {
        // INTEGER 2 then INTEGER 1 — DER must emit 1 then 2 (X.690 §11.6).
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSetOf(Asn1Tag.Set, inner =>
        {
            Asn1Integer.Encode(inner, 2);
            Asn1Integer.Encode(inner, 1);
        });
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        reader.ReadSet(Asn1Tag.Set, inner =>
        {
            Assert.Equal(1, Asn1Integer.Decode(inner));
            Assert.Equal(2, Asn1Integer.Decode(inner));
            Assert.True(inner.Eof);
        });
    }

    [Fact]
    public void WriteSetOf_BerPreservesElementOrder()
    {
        var writer = new Asn1Writer(Asn1Encoding.Ber);
        writer.WriteSetOf(Asn1Tag.Set, inner =>
        {
            Asn1Integer.Encode(inner, 2);
            Asn1Integer.Encode(inner, 1);
        });
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x31, 0x06, 0x02, 0x01, 0x02, 0x02, 0x01, 0x01 }, bytes);
    }

    [Fact]
    public void Any_DerRoundTripsTagAndContents()
    {
        var value = new Asn1Any(Asn1Tag.Integer, new byte[] { 0x05 });
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteAny(value);
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x02, 0x01, 0x05 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var decoded = reader.ReadAny();
        Assert.Equal(Asn1Tag.Integer, decoded.Tag);
        Assert.Equal(new byte[] { 0x05 }, decoded.Contents);
        Assert.True(reader.Eof);

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        rewrite.WriteAny(decoded);
        Assert.Equal(bytes, rewrite.Encode());
    }

    [Fact]
    public void Any_ReadAnyExpectedTag_RejectsMismatch()
    {
        var bytes = new byte[] { 0x02, 0x01, 0x05 };
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var ex = Assert.Throws<Asn1Exception>(() => reader.ReadAny(Asn1Tag.Null));
        Assert.Contains("Expected tag", ex.Message);
    }

    [Fact]
    public void Any_ImplicitTag_RoundTrips()
    {
        var value = new Asn1Any(Asn1Tag.Null, Array.Empty<byte>());
        var context = new Asn1Tag(Asn1TagClass.ContextSpecific, 0);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteAny(context, value);
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x80, 0x00 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var decoded = reader.ReadAny(context);
        Assert.Equal(context, decoded.Tag);
        Assert.Empty(decoded.Contents);
    }

    [Fact]
    public void Any_BerReadsIndefiniteLength()
    {
        // SEQUENCE { INTEGER 1 } with indefinite length as ANY
        var ber = new byte[] { 0x30, 0x80, 0x02, 0x01, 0x01, 0x00, 0x00 };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        var decoded = reader.ReadAny();
        Assert.Equal(Asn1Tag.Sequence, decoded.Tag);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x01 }, decoded.Contents);
        Assert.True(reader.Eof);
    }

    [Fact]
    public void ObjectIdentifier_RoundTripsDottedString()
    {
        const string oid = "1.2.840.113549";
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1ObjectIdentifier.Encode(writer, oid);
        var reader = new Asn1Reader(writer.Encode(), Asn1Encoding.Der);
        Assert.Equal(oid, Asn1ObjectIdentifier.Decode(reader));
    }

    [Fact]
    public void ObjectIdentifier_ParseArcsAndEncodeContents()
    {
        const string oid = "1.2.840.113549";
        Assert.Equal(new[] { 1, 2, 840, 113549 }, Asn1ObjectIdentifier.ParseArcs(oid));
        Assert.Equal(new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d }, Asn1ObjectIdentifier.EncodeContents(oid));
    }

    [Fact]
    public void ObjectIdentifier_RejectsInvalidDottedStrings()
    {
        Assert.Throws<Asn1Exception>(() => Asn1ObjectIdentifier.ParseArcs(""));
        Assert.Throws<Asn1Exception>(() => Asn1ObjectIdentifier.ParseArcs("1"));
        Assert.Throws<Asn1Exception>(() => Asn1ObjectIdentifier.ParseArcs("1.2.x"));
        Assert.Throws<Asn1Exception>(() => Asn1ObjectIdentifier.ParseArcs("1.-2"));
        Assert.Throws<Asn1Exception>(() => Asn1ObjectIdentifier.EncodeContents("1"));
    }

    [Fact]
    public void BitString_DerRoundTripsAndMatchesVector()
    {
        var value = Asn1BitString.FromBits(new[] { true, false, true });
        Assert.Equal(3, value.BitLength);
        Assert.Equal(5, value.UnusedBits);
        Assert.True(value[0]);
        Assert.False(value[1]);
        Assert.True(value[2]);

        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1BitString.Encode(writer, value);
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x03, 0x02, 0x05, 0xA0 }, bytes);

        var decoded = Asn1BitString.Decode(new Asn1Reader(bytes, Asn1Encoding.Der));
        Assert.Equal(value, decoded);

        var emptyWriter = new Asn1Writer(Asn1Encoding.Der);
        Asn1BitString.Encode(emptyWriter, default);
        Assert.Equal(new byte[] { 0x03, 0x01, 0x00 }, emptyWriter.Encode());
    }

    [Fact]
    public void BitString_BerReadsConstructedIndefinite()
    {
        // constructed indefinite: segment "10" (unused=6, 0x80) + segment "1" (unused=7, 0x80)
        var ber = new byte[]
        {
            0x23, 0x80,
            0x03, 0x02, 0x00, 0xA0,
            0x03, 0x02, 0x05, 0x00,
            0x00, 0x00
        };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        var value = reader.ReadBitString(Asn1Tag.BitString);
        Assert.Equal(new byte[] { 0xA0, 0x00 }, value.Span.ToArray());
        Assert.Equal(5, value.UnusedBits);
    }

    [Fact]
    public void BitString_RejectsInvalidForms()
    {
        Assert.Throws<Asn1Exception>(() => new Asn1BitString(new byte[] { 0xFF }, 8));
        Assert.Throws<Asn1Exception>(() => new Asn1BitString(ReadOnlySpan<byte>.Empty, 1));

        var emptyContents = new byte[] { 0x03, 0x00 };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(emptyContents, Asn1Encoding.Der).ReadBitString(Asn1Tag.BitString));

        var badUnused = new byte[] { 0x03, 0x02, 0x08, 0x00 };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(badUnused, Asn1Encoding.Der).ReadBitString(Asn1Tag.BitString));

        var trailingBits = new Asn1BitString(new byte[] { 0xA1 }, 5); // low 5 bits not zero
        Assert.Throws<Asn1Exception>(() =>
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            Asn1BitString.Encode(writer, trailingBits);
        });

        var berTrailing = new byte[] { 0x03, 0x02, 0x05, 0xA1 };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(berTrailing, Asn1Encoding.Der).ReadBitString(Asn1Tag.BitString));
    }

    [Fact]
    public void String_DerRoundTripsForms()
    {
        RoundTripString(Asn1StringForm.Utf8, "Привет", Asn1Tag.Utf8String);
        RoundTripString(Asn1StringForm.Printable, "Ann-1", Asn1Tag.PrintableString);
        RoundTripString(Asn1StringForm.Ia5, "user@host", Asn1Tag.Ia5String);
        RoundTripString(Asn1StringForm.Numeric, "12 34", Asn1Tag.NumericString);
        RoundTripString(Asn1StringForm.Visible, "Hello!", Asn1Tag.VisibleString);
        RoundTripString(Asn1StringForm.Bmp, "Hi", Asn1Tag.BmpString);
        RoundTripString(Asn1StringForm.Universal, "A", Asn1Tag.UniversalString);
        RoundTripString(Asn1StringForm.Teletex, "caf\u00e9", Asn1Tag.TeletexString);

        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1String.Encode(writer, "Ann", Asn1StringForm.Printable);
        Assert.Equal(new byte[] { 0x13, 0x03, 0x41, 0x6E, 0x6E }, writer.Encode());
    }

    [Fact]
    public void String_BerReadsConstructedUtf8()
    {
        var ber = new byte[]
        {
            0x2C, 0x80,
            0x0C, 0x02, 0x41, 0x6E,
            0x0C, 0x01, 0x6E,
            0x00, 0x00
        };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        Assert.Equal("Ann", reader.ReadString(Asn1Tag.Utf8String, Asn1StringForm.Utf8));
    }

    [Fact]
    public void String_RejectsInvalidCharactersAndLengths()
    {
        Assert.Throws<Asn1Exception>(() =>
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            Asn1String.Encode(writer, "Ann@", Asn1StringForm.Printable);
        });
        Assert.Throws<Asn1Exception>(() =>
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            Asn1String.Encode(writer, "12a", Asn1StringForm.Numeric);
        });
        Assert.Throws<Asn1Exception>(() =>
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            Asn1String.Encode(writer, "\u0080", Asn1StringForm.Ia5);
        });

        var oddBmp = new byte[] { 0x1E, 0x01, 0x00 };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(oddBmp, Asn1Encoding.Der).ReadString(Asn1Tag.BmpString, Asn1StringForm.Bmp));

        var badPrintable = new byte[] { 0x13, 0x01, (byte)'@' };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(badPrintable, Asn1Encoding.Der).ReadString(Asn1Tag.PrintableString, Asn1StringForm.Printable));
    }

    [Fact]
    public void Time_DerRoundTripsUtcAndGeneralized()
    {
        var utc = new DateTimeOffset(2017, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Time.Encode(writer, utc, Asn1TimeForm.Utc);
        var bytes = writer.Encode();
        Assert.Equal(
            new byte[]
            {
                0x17, 0x0D,
                0x31, 0x37, 0x30, 0x31, 0x30, 0x32, 0x30, 0x33, 0x30, 0x34, 0x30, 0x35, 0x5A
            },
            bytes);
        Assert.Equal(utc, Asn1Time.Decode(new Asn1Reader(bytes, Asn1Encoding.Der), Asn1TimeForm.Utc));

        // Default fractionDigits = 3: 120 ms → ".12Z" after trim.
        var withFraction = new DateTimeOffset(2017, 1, 2, 3, 4, 5, 120, TimeSpan.Zero);
        var gWriter = new Asn1Writer(Asn1Encoding.Der);
        Asn1Time.Encode(gWriter, withFraction, Asn1TimeForm.Generalized);
        var gBytes = gWriter.Encode();
        Assert.Equal(
            Encoding.ASCII.GetBytes("20170102030405.12Z"),
            gBytes.AsSpan(2).ToArray());
        Assert.Equal(withFraction, Asn1Time.Decode(new Asn1Reader(gBytes, Asn1Encoding.Der), Asn1TimeForm.Generalized));
    }

    [Fact]
    public void Time_GeneralizedFraction_ReadsOneToSevenDigits_WritesDer()
    {
        // Read: 1..7 fractional digits (BER and DER).
        Assert.Equal(1_000_000, FractionTicksOf("20170102030405.1Z"));
        Assert.Equal(1_200_000, FractionTicksOf("20170102030405.12Z"));
        Assert.Equal(1_230_000, FractionTicksOf("20170102030405.123Z"));
        Assert.Equal(1_234_000, FractionTicksOf("20170102030405.1234Z"));
        Assert.Equal(1_234_500, FractionTicksOf("20170102030405.12345Z"));
        Assert.Equal(1_234_560, FractionTicksOf("20170102030405.123456Z"));
        Assert.Equal(1_234_567, FractionTicksOf("20170102030405.1234567Z"));

        Assert.Throws<Asn1Exception>(() => FractionTicksOf("20170102030405.Z"));
        Assert.Throws<Asn1Exception>(() => FractionTicksOf("20170102030405.12345678Z"));

        // DER read accepts trailing zeros (soft profile).
        Assert.Equal(
            1_200_000,
            FractionTicksOf("20170102030405.120Z", Asn1Encoding.Der));

        // Write default (=3): round to ms, then DER-trim trailing zeros.
        AssertDerFraction(0, "20170102030405Z");
        AssertDerFraction(1_000_000, "20170102030405.1Z");
        AssertDerFraction(1_200_000, "20170102030405.12Z");
        AssertDerFraction(1_230_000, "20170102030405.123Z");
        AssertDerFraction(1_234_567, "20170102030405.123Z"); // rounds 0.1234567 → 0.123

        // Write with fractionDigits = 0: drop subseconds.
        AssertDerFraction(1_234_567, "20170102030405Z", fractionDigits: 0);
        AssertDerFraction(6_000_000, "20170102030406Z", fractionDigits: 0); // 0.6s → round up

        // Write with fractionDigits = 7: full tick precision, trim zeros.
        AssertDerFraction(0, "20170102030405Z", fractionDigits: 7);
        AssertDerFraction(1_000_000, "20170102030405.1Z", fractionDigits: 7);
        AssertDerFraction(1_200_000, "20170102030405.12Z", fractionDigits: 7);
        AssertDerFraction(1_234_567, "20170102030405.1234567Z", fractionDigits: 7);
    }

    private static int FractionTicksOf(string generalized, Asn1Encoding encoding = Asn1Encoding.Ber)
    {
        var bytes = WrapTime(Asn1Tag.GeneralizedTime, generalized);
        var value = Asn1Time.Decode(new Asn1Reader(bytes, encoding), Asn1TimeForm.Generalized);
        return (int)(value.Ticks % TimeSpan.TicksPerSecond);
    }

    private static void AssertDerFraction(int fractionTicks, string expectedText, int fractionDigits = 3)
    {
        var value = new DateTimeOffset(2017, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(fractionTicks);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Time.Encode(writer, value, Asn1TimeForm.Generalized, fractionDigits: fractionDigits);
        Assert.Equal(Encoding.ASCII.GetBytes(expectedText), writer.Encode().AsSpan(2).ToArray());
    }

    [Fact]
    public void Time_YearPivotAndBerOffset()
    {
        var y49 = Asn1Time.Decode(
            new Asn1Reader(WrapTime(Asn1Tag.UtcTime, "490102030405Z"), Asn1Encoding.Der),
            Asn1TimeForm.Utc);
        Assert.Equal(2049, y49.Year);

        var y50 = Asn1Time.Decode(
            new Asn1Reader(WrapTime(Asn1Tag.UtcTime, "500102030405Z"), Asn1Encoding.Der),
            Asn1TimeForm.Utc);
        Assert.Equal(1950, y50.Year);

        var berOffset = WrapTime(Asn1Tag.UtcTime, "1701020304+0500");
        var decoded = Asn1Time.Decode(new Asn1Reader(berOffset, Asn1Encoding.Ber), Asn1TimeForm.Utc);
        Assert.Equal(new DateTimeOffset(2017, 1, 1, 22, 4, 0, TimeSpan.Zero), decoded);

        Assert.Throws<Asn1Exception>(() =>
            Asn1Time.Decode(new Asn1Reader(berOffset, Asn1Encoding.Der), Asn1TimeForm.Utc));

        var noSeconds = WrapTime(Asn1Tag.GeneralizedTime, "201701020304Z");
        Assert.Throws<Asn1Exception>(() =>
            Asn1Time.Decode(new Asn1Reader(noSeconds, Asn1Encoding.Der), Asn1TimeForm.Generalized));
        Assert.Equal(
            new DateTimeOffset(2017, 1, 2, 3, 4, 0, TimeSpan.Zero),
            Asn1Time.Decode(new Asn1Reader(noSeconds, Asn1Encoding.Ber), Asn1TimeForm.Generalized));
    }

    private static void RoundTripString(Asn1StringForm form, string value, Asn1Tag tag)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1String.Encode(writer, value, form, tag);
        var bytes = writer.Encode();
        var again = new Asn1Writer(Asn1Encoding.Der);
        Asn1String.Encode(again, Asn1String.Decode(new Asn1Reader(bytes, Asn1Encoding.Der), form, tag), form, tag);
        Assert.Equal(bytes, again.Encode());
        Assert.Equal(value, Asn1String.Decode(new Asn1Reader(bytes, Asn1Encoding.Der), form, tag));
    }

    private static byte[] WrapTime(Asn1Tag tag, string text)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteString(tag, text, Asn1StringForm.Visible);
        return writer.Encode();
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

    [Fact]
    public void PrimitivesAsn_GeneratesAndRoundTrips()
    {
        var document = new Asn1Compiler().CompileFiles(new[] { TestData.RepoPath("fixtures/asn1/primitives.asn") });
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

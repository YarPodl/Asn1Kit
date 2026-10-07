using System.Reflection;
using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class OpenTypeBindingTests
{
    private const string Source = @"M DEFINITIONS ::= BEGIN
      C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
      integerEntry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 3} }
      anotherEntry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 4} }
      booleanEntry C ::= { BOOLEAN IDENTIFIED BY {1 2 5} }
      Numbers C ::= { integerEntry | anotherEntry } Flags C ::= {booleanEntry}
      Extension{C:S} ::= SEQUENCE {
        oid C.&id({S}), critical BOOLEAN DEFAULT FALSE,
        payload OCTET STRING (CONTAINING C.&T({S}{@oid})) }
      Attribute{C:S} ::= SEQUENCE { oid C.&id({S}), values SET OF C.&T({S}{@oid}) }
      Holder ::= SEQUENCE {
        extensions SEQUENCE OF Extension{Numbers} OPTIONAL,
        other SEQUENCE OF Extension{Flags} OPTIONAL,
        signedAttrs SET OF Attribute{Numbers} OPTIONAL,
        unsignedAttrs SET OF Attribute{Flags} OPTIONAL }
      END";

    private static object? Run(string body, Action<IrDocument>? configure = null, string source = Source)
    {
        var probe = @"using System; using Asn1Kit.Runtime; namespace M;
          public static class Probe { public static object Run() { " + body + " } }";
        Assembly assembly;
        if (configure is null)
        {
            // Cache CompileText+Generate+Emit of the ASN module; only probe is re-emitted.
            assembly = GeneratedCompilation.CompileProbeAgainstCachedModule(
                source,
                () =>
                {
                    var document = new Asn1Compiler().CompileText(source);
                    IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
                    return new CSharpBackend().Generate(document).Select(f => f.Contents).ToArray();
                },
                probe);
        }
        else
        {
            var document = new Asn1Compiler().CompileText(source);
            configure(document);
            IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
            var files = new CSharpBackend().Generate(document).Select(f => f.Contents).Append(probe).ToArray();
            assembly = ModernAsn1Tests.CompileGenerated(files);
        }

        try { return assembly.GetType("M.Probe")!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Fact]
    public void NullAlgorithmBindingsDoNotGenerateDataWrappersOrEmptySourceMethods()
    {
        const string source = @"M DEFINITIONS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          NoData ::= NULL
          emptyEntry C ::= { NoData IDENTIFIED BY {1 2 3} }
          dataEntry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 4} }
          Mixed C ::= {emptyEntry | dataEntry} Empty C ::= {emptyEntry}
          AlgorithmIdentifier{C:S} ::= SEQUENCE {
            algorithm C.&id({S}), parameters C.&T({S}{@algorithm}) OPTIONAL }
          Holder ::= SEQUENCE { mixed AlgorithmIdentifier{Mixed}, empty AlgorithmIdentifier{Empty} }
          END";
        var document = new Asn1Compiler().CompileText(source);
        document.Modules[0].Types.Single(t => t.Name == "AlgorithmIdentifier").Options =
            IrOptions.SetCSharp(null, "typeName", "AlgorithmInfo");
        var files = new CSharpBackend().Generate(document);
        var generated = Assert.Single(files).Contents;
        Assert.Contains("public sealed class AlgorithmInfo", generated);
        Assert.DoesNotContain("public sealed class DataEntryAlgorithmInfo", generated);
        Assert.DoesNotContain("public sealed class EmptyEntryAlgorithmInfo", generated);
        Assert.Contains("public sealed record MixedParametersBinding<T>", generated);
        Assert.DoesNotContain("BindingCodec", generated);
        Assert.DoesNotContain("Decode0(", generated);
        Assert.Contains("Asn1Codecs.Null", generated);
        Assert.Contains("Asn1Codecs.Int32", generated);
        Assert.Contains("DecodeEmptyEntry, Asn1Codecs.Null.Encode", generated);
        Assert.Contains("DecodeDataEntry, Asn1Codecs.Int32.Encode", generated);
        Assert.DoesNotContain("private static Asn1Any EncodeEmptyEntry", generated);
        Assert.DoesNotContain("private static Asn1Any EncodeDataEntry", generated);
        Assert.Contains("TryDecodeMixedParameters<T>(MixedParametersBinding<T> binding, out T value)", generated);
        Assert.Contains("public void SetMixedParametersEmptyEntry()", generated);
        Assert.DoesNotContain("TryDecodeMixedParameters<T>(out T value)", generated);
        Assert.DoesNotContain("TryDecodeMixedParametersEmptyEntry", generated);
        Assert.DoesNotContain("SetMixedParametersEmptyEntry(Asn1Null value)", generated);
        Assert.DoesNotContain("out Asn1Null value", generated);
        Assert.DoesNotContain("TryGetMixedEmptyEntry", generated);
        Assert.DoesNotContain("TryGetEmptyEmptyEntry", generated);
        Assert.Contains("TryGetMixed<T>(this Holder source, MixedParametersBinding<T> binding, out T value)", generated);
        Assert.Contains("TryGetEmpty<T>", generated);
        Assert.Equal(42, Run(@"
          var raw = AlgorithmInfo.Decode(new Asn1Reader(Convert.FromHexString(""300606022A030500"")));
          var holder = new Holder {Mixed = raw, Empty = raw};
          if (!holder.TryDecodeMixedParameters(MixedParametersBindings.EmptyEntry, out _) ||
              !holder.TryDecodeEmptyParameters(EmptyParametersBindings.EmptyEntry, out _)) throw new Exception();
          holder.SetMixedParametersEmptyEntry();
          var writer = new Asn1Writer(); raw.Encode(writer);
          if (Convert.ToHexString(writer.Encode()) != ""300606022A030500"") throw new Exception();
          holder.Mixed = AlgorithmInfo.Decode(new Asn1Reader(Convert.FromHexString(""300406022A03"")));
          if (holder.TryDecodeMixedParameters(MixedParametersBindings.EmptyEntry, out _) ||
              holder.TryGetMixed(MixedParametersBindings.DataEntry, out _)) throw new Exception();
          holder.Mixed = raw;
          raw.Parameters = new Asn1Any(Convert.FromHexString(""02012A""));
          try { holder.TryDecodeMixedParameters(MixedParametersBindings.EmptyEntry, out _); throw new Exception(); }
          catch (Asn1Exception) { }
          holder.Mixed = new AlgorithmInfo();
          holder.Mixed.SetParametersDataEntry(42);
          if (!holder.TryGetMixed(MixedParametersBindings.DataEntry, out var typed)) throw new Exception();
          return typed;
        ", document => document.Modules[0].Types.Single(t => t.Name == "AlgorithmIdentifier").Options =
            IrOptions.SetCSharp(null, "typeName", "AlgorithmInfo"), source));
    }

    [Fact]
    public void InstanceDescriptorsSupportCustomIntegerKeys()
    {
        const string source = @"M DEFINITIONS ::= BEGIN
          C ::= CLASS { &id INTEGER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          emptyEntry C ::= { NULL IDENTIFIED BY 3 }
          dataEntry C ::= { INTEGER (0..100) IDENTIFIED BY 4 }
          Mixed C ::= {emptyEntry | dataEntry}
          Empty C ::= {emptyEntry}
          AlgorithmIdentifier{C:S} ::= SEQUENCE {
            algorithm C.&id({S}), parameters C.&T({S}{@algorithm}) OPTIONAL }
          Holder ::= SEQUENCE {
            mixed AlgorithmIdentifier{Mixed}, empty AlgorithmIdentifier{Empty} }
          END";
        Assert.Equal(42, Run(@"
          var key = new System.Numerics.BigInteger(9);
          var binding = MixedParametersBindings.Create<int>(key,
            static source => {
              var reader = new Asn1Reader(source.Parameters!.Value.EncodedMemory);
              var decoded = reader.ReadInt32(Asn1Tag.Integer);
              reader.ThrowIfNotEmpty();
              return decoded;
            },
            static value => {
              var writer = new Asn1Writer();
              writer.WriteInteger(Asn1Tag.Integer, value);
              return new Asn1Any(writer.Encode());
            });
          var holder = new Holder {Mixed = new AlgorithmIdentifier {
            Algorithm = Asn1Integer.FromInt32(9),
            Parameters = new Asn1Any(Convert.FromHexString(""020107"")) }};
          if (!holder.TryDecodeMixedParameters(binding, out var initial) || initial != 7)
            throw new Exception();
          holder.SetMixedParameters(binding, 42);
          if (!holder.TryDecodeMixedParameters(binding, out var updated)) throw new Exception();
          return updated;
        ", source: source));
    }

    [Theory]
    [InlineData("lazy")]
    [InlineData("retainEncoded")]
    public void InstanceDescriptorsUpdateWrappedValueTypeContainers(string option)
    {
        const string source = @"M DEFINITIONS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          emptyEntry C ::= { NULL IDENTIFIED BY {1 2 3} }
          dataEntry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 4} }
          Mixed C ::= {dataEntry}
          Empty C ::= {emptyEntry}
          AlgorithmIdentifier{C:S} ::= SEQUENCE {
            algorithm C.&id({S}), parameters C.&T({S}{@algorithm}) OPTIONAL }
          Holder ::= SEQUENCE {
            mixed AlgorithmIdentifier{Mixed}, empty AlgorithmIdentifier{Empty} }
          END";
        var assignment = option == "lazy"
            ? "Asn1Lazy<AlgorithmInfo>.FromValue(info)"
            : "info";
        Assert.Equal(42, Run(@"
          var descriptor = MixedParametersBindings.DataEntry;
          var info = new AlgorithmInfo {Algorithm = descriptor.Oid};
          var holder = new Holder {Mixed = " + assignment + @"};
          holder.SetMixedParametersDataEntry(42);
          if (!holder.TryDecodeMixedParameters(descriptor, out var value)) throw new Exception();
          return value;
        ", document =>
        {
            var algorithm = document.Modules[0].Types.Single(t => t.Name == "AlgorithmIdentifier");
            algorithm.Options = IrOptions.SetCSharpValueType(
                IrOptions.SetCSharp(null, "typeName", "AlgorithmInfo"), true);
            IrOptionsPatch.ApplyJson(document,
                "{\"fields\":{\"M.Holder.mixed\":{\"" + option + "\":true}}}");
        }, source));
    }

    [Fact]
    public void NullBindingsAreAvailableForContainingAndOfContainers()
    {
        var source = Source.Replace("booleanEntry C ::= { BOOLEAN",
            "NoData ::= NULL booleanEntry C ::= { NoData");
        var document = new Asn1Compiler().CompileText(source);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var files = new CSharpBackend().Generate(document);
        var generated = Assert.Single(files).Contents;
        Assert.Contains("FlagsPayloadBinding<Asn1Null> BooleanEntry", generated);
        Assert.Contains("FlagsBinding<Asn1Null[]> BooleanEntry", generated);
        Assert.Contains("TryGetOther<T>", generated);
        Assert.Contains("TryGetUnsignedAttrs<T>", generated);
        Assert.DoesNotContain("out Asn1Null value", generated);
        Assert.DoesNotContain("TryGetOtherBooleanEntry", generated);
        Assert.DoesNotContain("public sealed class BooleanEntryExtension", generated);
        Assert.DoesNotContain("public sealed class IntegerEntryExtension", generated);
        Assert.Equal("300806022A0504020500", Run(@"
          var raw = Extension.Decode(new Asn1Reader(Convert.FromHexString(""300806022A0504020500"")));
          var attribute = Attribute.Decode(new Asn1Reader(Convert.FromHexString(""300A06022A05310405000500"")));
          if (attribute.Values.Length != 2) throw new Exception();
          var attributesWriter = new Asn1Writer(); attribute.Encode(attributesWriter);
          if (Convert.ToHexString(attributesWriter.Encode()) != ""300A06022A05310405000500"") throw new Exception();
          var writer = new Asn1Writer(); raw.Encode(writer);
          return Convert.ToHexString(writer.Encode());
        ", source: source));
    }

    [Fact]
    public void BindingPreservesRawMetadataAndProducesCanonicalDer()
    {
        var hex = Run(@"
          var raw = new Extension { Critical = true };
          raw.SetPayload(NumbersPayloadBindings.IntegerEntry, 42);
          var writer = new Asn1Writer(); raw.Encode(writer);
          var bytes = writer.Encode();
          raw = Extension.Decode(new Asn1Reader(bytes));
          if (!raw.TryDecodePayload(NumbersPayloadBindings.IntegerEntry, out var decoded) ||
              decoded != 42 || !raw.Critical) throw new Exception(""Conversion failed."");
          var before = raw.Payload.Contents;
          raw.SetPayload(NumbersPayloadBindings.IntegerEntry, 7);
          if (!raw.TryDecodePayload(NumbersPayloadBindings.IntegerEntry, out var changed) || changed != 7)
              throw new Exception(""Updated data was not encoded."");
          if (before.Span.SequenceEqual(raw.Payload.Contents.Span)) throw new Exception(""Source was not updated."");
          if (!raw.Critical) throw new Exception(""Metadata was not preserved."");
          return Convert.ToHexString(bytes);
        ");
        Assert.Equal("300C06022A030101FF040302012A", hex);
    }

    [Fact]
    public void BindingReusesForeignOidCatalogWithoutADirectValueImport()
    {
        Assert.Equal(true, Run(@"
          return NumbersPayloadBindings.IntegerEntry.Oid.Memory.Equals(Catalog.Known.KeysOids.IdInteger.Memory);
        ", document =>
        {
            document.Modules.Add(new IrModule
            {
                Name = "Keys", Options = IrOptions.SetCSharp(null, "namespace", "Catalog.Known"),
                Values = new List<IrValueDef> { new()
                {
                    Name = "id-integer", Type = new OidType(), Value = new IrOidValue {Value = "1.2.3"}
                }}
            });
        }));
    }

    [Fact]
    public void BindingPrefersLocalOidsAndSkipsSuppressedForeignValues()
    {
        var document = new Asn1Compiler().CompileText(Source);
        var local = new IrValueDef {Name = "id-local", Type = new OidType(),
            Value = new IrOidValue {Value = "1.2.3"}};
        document.Modules[0].Values.Add(local);
        document.Modules.Insert(0, new IrModule {Name = "Keys", Values = new List<IrValueDef> {new()
        {
            Name = "id-foreign", Type = new OidType(), Value = new IrOidValue {Value = "1.2.3"}
        }}});
        var backend = new CSharpBackend();
        var source = backend.Generate(document).Single(f => f.RelativePath == "M.g.cs").Contents;
        Assert.Contains("new(MOids.IdLocal, DecodeIntegerEntry, EncodeIntegerEntry)", source);
        local.Options = new System.Text.Json.Nodes.JsonObject {["generate"] = false};
        document.Modules[0].Values[0].Options = new System.Text.Json.Nodes.JsonObject {["generate"] = false};
        var files = backend.Generate(document);
        source = files.Single(f => f.RelativePath == "M.g.cs").Contents;
        Assert.Contains("Asn1Oid.Parse(\"1.2.3\")", source);
        Assert.DoesNotContain("MOids.IdLocal", source);
        Assert.DoesNotContain("KeysOids", source);
        _ = ModernAsn1Tests.CompileGenerated(files.Select(f => f.Contents).ToArray());
    }

    [Fact]
    public void SourcesRemainIndependentAndDifferentOidsHaveDifferentBindings()
    {
        Assert.Equal(true, Run(@"
          var first = new Extension(); first.SetPayloadIntegerEntry(42);
          var second = new Extension(); second.SetPayloadAnotherEntry(7);
          var signed = new Attribute(); signed.SetValuesIntegerEntry(new[] {7, 42});
          var unsigned = new Attribute(); unsigned.SetValuesBooleanEntry(new[] {true});
          var holder = new Holder { Extensions = new[] { first, second },
            SignedAttrs = new[] {signed}, UnsignedAttrs = new[] {unsigned} };
          if (!holder.TryGetExtensions(NumbersPayloadBindings.IntegerEntry, out var a) || a != 42 ||
              !holder.TryGetExtensionsAnotherEntry(out var b) || b != 7 ||
              !holder.TryGetSignedAttrs(NumbersBindings.IntegerEntry, out var c) || c.Length != 2 ||
              !holder.TryGetUnsignedAttrsBooleanEntry(out var d) || !d[0]) return false;
          if (holder.TryGetOther(FlagsPayloadBindings.BooleanEntry, out _) ||
              first.TryDecodePayload(NumbersPayloadBindings.AnotherEntry, out _)) return false;
          signed.SetValuesIntegerEntry(Array.Empty<int>());
          return holder.TryGetSignedAttrsIntegerEntry(out var empty) && empty.Length == 0;
        "));
    }

    [Fact]
    public void SourceGettersUseTableScopedDescriptorsWithoutTypeSwitches()
    {
        var document = new Asn1Compiler().CompileText(Source);
        var generated = Assert.Single(new CSharpBackend().Generate(document)).Contents;
        Assert.Contains("public sealed record NumbersPayloadBinding<T>", generated);
        Assert.Contains("public static NumbersPayloadBinding<int> IntegerEntry", generated);
        Assert.Contains("TryGetExtensions<T>(this Holder source, NumbersPayloadBinding<T> binding, out T value)", generated);
        Assert.Contains("TryGetExtensionsIntegerEntry(this Holder source, out int value)", generated);
        Assert.Contains("out T value, out M.Extension raw", generated);
        Assert.DoesNotContain("TryGetExtensions<T>(this Holder source, out T value)", generated);
        Assert.Contains("TryDecodePayload<T>(this M.Extension source, NumbersPayloadBinding<T> binding, out T value)", generated);
        Assert.Contains("SetPayload<T>(this M.Extension source, NumbersPayloadBinding<T> binding, T value)", generated);
        Assert.Contains("TryDecodePayloadIntegerEntry(this M.Extension source, out int value)", generated);
        Assert.DoesNotContain("TryDecodePayload<T>(this M.Extension source, out T value)", generated);
        Assert.DoesNotContain("typeof(T)", generated);

        var start = generated.IndexOf(
            "TryGetExtensions<T>(this Holder source, NumbersPayloadBinding<T> binding, out T value, out M.Extension raw)",
            StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = generated.IndexOf("TryGetExtensionsIntegerEntry", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var getter = generated[start..end];
        Assert.DoesNotContain("typeof(T)", getter);
        Assert.DoesNotContain("TryFromExtension(match", getter);
        Assert.Contains("value = binding.Decoder(match);", getter);
    }

    [Fact]
    public void ContextualDescriptorsDecodeBuiltInAndCustomBindings()
    {
        Assert.Equal(99, Run(@"
          var raw = new Extension();
          raw.SetPayloadIntegerEntry(42);
          if (!raw.TryDecodePayloadIntegerEntry(out var known) || known != 42)
              throw new Exception();
          var oid = Asn1Oid.Parse(""1.2.99"");
          raw.Oid = oid;
          raw.Critical = true;
          var custom = NumbersPayloadBindings.Create<int>(oid,
              static source => source.Critical ? 99 : 0);
          if (!raw.TryDecodePayload(custom, out var value)) throw new Exception();
          return value;
        "));
    }

    [Fact]
    public void ContextualDescriptorsSupportCustomIntegerKeys()
    {
        Assert.Equal(99, Run(@"
          var raw = new Extension {Oid = Asn1Integer.FromInt32(99), Critical = true};
          var custom = NumbersPayloadBindings.Create<int>(
              new System.Numerics.BigInteger(99), static source => source.Critical ? 99 : 0);
          if (!raw.TryDecodePayload(custom, out var value)) throw new Exception();
          return value;
        ", source: Source.Replace("&id OBJECT IDENTIFIER UNIQUE", "&id INTEGER UNIQUE")
            .Replace("{1 2 3}", "3").Replace("{1 2 4}", "4").Replace("{1 2 5}", "5")));
    }

    [Fact]
    public void ContextualDescriptorTypesPreventBorrowingFromAnotherTable()
    {
        // Container TryDecodePayload overloads accept each table's Binding<T>; owner TryGet
        // stays table-scoped and rejects a foreign descriptor type.
        var files = new CSharpBackend().Generate(new Asn1Compiler().CompileText(Source));
        const string probe = @"namespace M;
          public static class Probe { public static void Run(Holder holder) {
            holder.TryGetExtensions(FlagsPayloadBindings.BooleanEntry, out _);
          } }";
        var error = Assert.Throws<InvalidOperationException>(() =>
            ModernAsn1Tests.CompileGenerated(files.Select(f => f.Contents).Append(probe).ToArray()));
        Assert.Contains("CS0411", error.Message);
        Assert.Contains("NumbersPayloadBinding", error.Message);
    }

    [Fact]
    public void CustomDescriptorsDecodeUnknownOidAndRunAfterDuplicateCheck()
    {
        Assert.Equal(1, Run(@"
          var oid = Asn1Oid.Parse(""1.2.99"");
          var raw = new Extension {Critical = true};
          var calls = 0;
          var binding = NumbersPayloadBindings.Create<int>(oid, source =>
          {
            calls++;
            var reader = new Asn1Reader(source.Payload.Value.EncodedMemory);
            var decoded = reader.ReadInt32(Asn1Tag.Integer);
            reader.ThrowIfNotEmpty();
            return decoded;
          }, static value =>
          {
            var writer = new Asn1Writer();
            writer.WriteInteger(Asn1Tag.Integer, value);
            return Asn1Contained<Asn1Any>.FromValue(new Asn1Any(writer.Encode()));
          });
          raw.SetPayload(binding, 7);
          var holder = new Holder {Extensions = new[] {raw}};
          if (!holder.TryGetExtensions(binding, out var value, out var matched) || value != 7 ||
              !matched.Critical || matched.Oid != oid) throw new Exception();
          holder.Extensions = new[] {raw, raw};
          try { holder.TryGetExtensions(binding, out _); throw new Exception(); }
          catch (Asn1Exception) { }
          return calls;
        "));
    }

    [Fact]
    public void DescriptorTypesPreventBorrowingFromAnotherTable()
    {
        var files = new CSharpBackend().Generate(new Asn1Compiler().CompileText(Source));
        const string probe = @"namespace M;
          public static class Probe { public static void Run() {
            new Holder().TryGetUnsignedAttrs(NumbersBindings.IntegerEntry, out _);
          } }";
        var error = Assert.Throws<InvalidOperationException>(() =>
            ModernAsn1Tests.CompileGenerated(files.Select(f => f.Contents).Append(probe).ToArray()));
        Assert.Contains("CS0411", error.Message);
        Assert.Contains("FlagsBinding", error.Message);
    }

    [Theory]
    [InlineData("0201", true)]
    [InlineData("0101FF", true)]
    [InlineData("02012A00", true)]
    [InlineData("02012A", false)]
    public void KnownContentsAreValidatedButUnknownOidsStayOpaque(string contents, bool fails)
    {
        var body = @"
          var raw = new Extension();
          raw.SetPayloadIntegerEntry(42);
          raw.Payload = Asn1Contained<Asn1Any>.FromEncoded(Convert.FromHexString(""" + contents + @"""));
          return raw.TryDecodePayload(NumbersPayloadBindings.IntegerEntry, out _);";
        if (fails) Assert.Throws<Asn1Exception>(() => Run(body));
        else Assert.Equal(true, Run(body));
        Assert.Equal(false, Run(body.Replace("return raw.TryDecode", "raw.Oid = Asn1Oid.Parse(\"1.2.99\"); return raw.TryDecode")));
    }

    [Fact]
    public void DuplicateSearchFailsBeforeDecodingAndAbsenceReturnsFalse()
    {
        var error = Assert.Throws<Asn1Exception>(() => Run(@"
          var raw = new Extension();
          raw.SetPayloadIntegerEntry(42);
          raw.Payload = Asn1Contained<Asn1Any>.FromEncoded(new byte[] {0xFF});
          var holder = new Holder { Extensions = new[] {raw, raw} };
          return holder.TryGetExtensions(NumbersPayloadBindings.IntegerEntry, out _);
        "));
        Assert.Contains("Multiple values", error.Message);
        Assert.Equal(false, Run("return new Holder().TryGetExtensionsIntegerEntry(out _);"));
    }

    [Fact]
    public void ConstructedBerContainingAndRenamedRetainedSourcesWork()
    {
        Assert.Equal(42, Run(@"
          var raw = Extension.Decode(new Asn1Reader(Convert.FromHexString(
              ""301006022A030101FF2480040302012A0000""), Asn1Encoding.Ber));
          var holder = new Holder { Items = new[] {raw} };
          if (!holder.TryGetItems(NumbersPayloadBindings.IntegerEntry, out var value)) throw new Exception();
          return value;
        ", document => IrOptionsPatch.Apply(document, System.Text.Json.Nodes.JsonNode.Parse(@"{
          ""fields"": {""M.Holder.extensions"": {""retainEncoded"": true,
            ""csharp"": {""propertyName"": ""Items""}}}
        }")!.AsObject())));
    }

    [Fact]
    public void ValueTypeContainersAndLazySourcesWork()
    {
        Assert.Equal(42, Run(@"
          var raw = new Extension(); raw.SetPayloadIntegerEntry(42);
          var writer = new Asn1Writer(); raw.Encode(writer);
          var holder = new Holder { Extensions = new[] {Extension.Decode(new Asn1Reader(writer.Encode()))} };
          if (!holder.TryGetExtensionsIntegerEntry(out var value)) throw new Exception();
          return value;
        ", document =>
        {
            var extension = document.Modules[0].Types.Single(t => t.Name == "Extension");
            extension.Options = IrOptions.SetCSharpValueType(extension.Options, true);
        }));
        Assert.Equal(7, Run(@"
          var original = new Holder {Extensions = Asn1Lazy<Extension[]>.FromValue(
              new[] {new Extension()})};
          original.Extensions.Value[0].SetPayloadIntegerEntry(7);
          var writer = new Asn1Writer(); original.Encode(writer);
          var holder = Holder.Decode(new Asn1Reader(writer.Encode()));
          if (!holder.TryGetExtensions(NumbersPayloadBindings.IntegerEntry, out var value)) throw new Exception();
          return value;
        ", document => IrOptionsPatch.ApplyJson(document,
            "{\"fields\":{\"M.Holder.extensions\":{\"lazy\":true}}}")));
    }

    [Fact]
    public void WrappedOfPayloadsDecodeAndEncodeThroughTheirValues()
    {
        foreach (var option in new[] { "retainEncoded", "lazy" })
            Assert.Equal(42, Run(@"
              var raw = new Attribute();
              raw.SetValuesIntegerEntry(new[] {7, 42});
              var writer = new Asn1Writer(); raw.Encode(writer);
              raw = Attribute.Decode(new Asn1Reader(writer.Encode()));
              if (!raw.TryDecodeValuesIntegerEntry(out var typed)) throw new Exception();
              return typed[1];
            ", document => IrOptionsPatch.ApplyJson(document,
                "{\"fields\":{\"M.Attribute.values\":{\"" + option + "\":true}}}")));
    }

    [Fact]
    public void AncestorSelectorsRequireAnExplicitContextWithoutChangingIt()
    {
        Assert.Equal(42, Run(@"
          var holder = new Holder { SourceOid = NumbersPayloadBindings.IntegerEntry.Oid };
          var raw = new Extension {Oid = Asn1Oid.Parse(""1.2.99"")};
          raw.SetPayload(holder, NumbersPayloadBindings.IntegerEntry, 42);
          holder.Extensions = new[] {raw};
          if (!holder.TryGetExtensionsIntegerEntry(out var value) ||
              !raw.TryDecodePayloadIntegerEntry(holder, out var payload) || payload != 42 ||
              raw.Oid.ToString() != ""1.2.99"") throw new Exception();
          return value;
        ", document =>
        {
            var types = document.Modules[0].Types;
            var holder = (SequenceType)types.Single(t => t.Name == "Holder").Type;
            holder.Components.Insert(0, new IrComponent {Name = "sourceOid", Type = new OidType()});
            var extension = (SequenceType)types.Single(t => t.Name == "Extension").Type;
            var any = (AnyType)((OctetStringType)extension.Components[2].Type).Containing!;
            any.Selector = new IrOpenTypeSelector {Levels = 1, Path = new List<string> {"sourceOid"}};
        }));
    }

    [Fact]
    public void ImportedContainerMetadataUsesItsDefiningNamespace()
    {
        Assert.Equal(7, Run(@"
          var raw = new Extension {Label = new Label {Number = 7}};
          Use.UseOpenTypeExtensions.SetPayload(raw,
              Use.NumbersPayloadBindings.IntegerEntry, 42);
          var holder = new Use.Holder {Extensions = new[] {raw}};
          if (!Use.HolderOpenTypeExtensions.TryGetExtensions(holder,
                  Use.NumbersPayloadBindings.IntegerEntry, out var value, out var matched))
              throw new Exception();
          if (value != 42) throw new Exception();
          return matched.Label.Number;
        ", document =>
        {
            var library = document.Modules[0];
            var holder = library.Types.Single(t => t.Name == "Holder");
            library.Types.Remove(holder);
            document.Modules.Add(new IrModule {Name = "Use", Types = new List<IrTypeDef> {holder}});
            library.Types.Add(new IrTypeDef {Name = "Label", Type = new SequenceType
            {
                Components = new List<IrComponent> {new() {Name = "number",
                    Type = new IntegerType {Constraint = new IrConstraint {Value = new IrBound {Min = 0, Max = 100}}}}}
            }});
            var extension = (SequenceType)library.Types.Single(t => t.Name == "Extension").Type;
            extension.Components.Add(new IrComponent {Name = "label", Type = new RefType {Name = "Label", Module = "M"}});
        }));
    }

    [Fact]
    public void InlineBindingTypesHaveGeneratedCodecs()
    {
        Assert.Equal(42, Run(@"
          var raw = new Extension();
          raw.SetPayloadIntegerEntry(new IntegerEntryExtension_Value {Number = 42});
          if (!raw.TryDecodePayloadIntegerEntry(out var value)) throw new Exception();
          return value.Number;
        ", source: Source.Replace("integerEntry C ::= { INTEGER (0..100)",
            "integerEntry C ::= { SEQUENCE {number INTEGER (0..100)}")));
    }

    [Fact]
    public void NestedOfPayloadsKeepTheirArrayShape()
    {
        Assert.Equal(42, Run(@"
          var raw = new Attribute();
          raw.SetValuesIntegerEntry(new[] {new[] {7, 42}, Array.Empty<int>()});
          if (!raw.TryDecodeValuesIntegerEntry(out var value) || value[1].Length != 0)
              throw new Exception();
          return value[0][1];
        ", source: Source.Replace("values SET OF C.&T", "values SET OF SET OF C.&T")));
    }

    [Fact]
    public void IntegerSelectorsHaveFixedKeysAndTypedConversions()
    {
        Assert.Equal(42, Run(@"
          var raw = new Extension(); raw.SetPayloadIntegerEntry(42);
          var holder = new Holder {Extensions = new[] {raw}};
          if (!holder.TryGetExtensions(NumbersPayloadBindings.IntegerEntry, out var value) ||
              NumbersPayloadBindings.IntegerEntry.Key != 3) throw new Exception();
          return value;
        ", source: Source.Replace("&id OBJECT IDENTIFIER UNIQUE", "&id INTEGER UNIQUE")
            .Replace("{1 2 3}", "3").Replace("{1 2 4}", "4").Replace("{1 2 5}", "5")));
    }

    [Fact]
    public void IntegerSelectorCatalogCreatesCustomDescriptors()
    {
        Assert.Equal(99, Run(@"
          var raw = new Extension {Oid = Asn1Integer.FromInt32(99), Critical = true};
          var holder = new Holder {Extensions = new[] {raw}};
          var binding = NumbersPayloadBindings.Create<int>(new System.Numerics.BigInteger(99),
            static source => source.Critical ? 99 : 0, static _ => default);
          if (!holder.TryGetExtensions(binding, out var value)) throw new Exception();
          return value;
        ", source: Source.Replace("&id OBJECT IDENTIFIER UNIQUE", "&id INTEGER UNIQUE")
            .Replace("{1 2 3}", "3").Replace("{1 2 4}", "4").Replace("{1 2 5}", "5")));
    }

    [Fact]
    public void TypedBitStringContainingRequiresOctetAlignment()
    {
        Assert.Throws<Asn1Exception>(() => Run(@"
          var raw = new Extension(); raw.SetPayloadIntegerEntry(42);
          raw.Payload = Asn1Contained<Asn1Any>.FromEncoded(Convert.FromHexString(""02012A""), 1);
          return raw.TryDecodePayload(NumbersPayloadBindings.IntegerEntry, out _);
        ", source: Source.Replace("OCTET STRING (CONTAINING", "BIT STRING (CONTAINING")));
    }

    [Fact]
    public void NestedSelectorsCreateAndCopyTheirKeyOwners()
    {
        Assert.Equal("preserved", Run(@"
          var original = new Header {Oid = NumbersPayloadBindings.AnotherEntry.Oid, Label = ""preserved""};
          var raw = new Extension {Header = original};
          raw.SetPayloadIntegerEntry(42);
          if (raw.Header.Oid != NumbersPayloadBindings.IntegerEntry.Oid ||
              original.Oid != NumbersPayloadBindings.AnotherEntry.Oid ||
              !raw.TryDecodePayloadIntegerEntry(out var value) || value != 42)
              throw new Exception(""Key owner was changed."");
          return raw.Header.Label;
        ", document =>
        {
            var types = document.Modules[0].Types;
            types.Add(new IrTypeDef {Name = "Header", Type = new SequenceType {Components = new List<IrComponent>
            {
                new() {Name = "oid", Type = new OidType()},
                new() {Name = "label", Type = new StringType {Form = "utf8"}}
            }}});
            var extension = (SequenceType)types.Single(t => t.Name == "Extension").Type;
            extension.Components[0] = new IrComponent {Name = "header", Type = new RefType {Name = "Header", Module = "M"}};
            var any = (AnyType)((OctetStringType)extension.Components[2].Type).Containing!;
            any.Selector = new IrOpenTypeSelector {Path = new List<string> {"header", "oid"}};
        }));
    }

    [Theory]
    [InlineData("lazy")]
    [InlineData("retainEncoded")]
    public void WrappedLocalSelectorOwnersAreCopiedAndRewrapped(string option)
    {
        var header = option == "lazy"
            ? "Asn1Lazy<Header>.FromValue(new Header {Label = \"preserved\"})"
            : "new Asn1Value<Header>(new Header {Label = \"preserved\"})";
        Assert.Equal("preserved", Run(@"
          var raw = new Extension {Header = " + header + @"};
          raw.SetPayloadIntegerEntry(42);
          if (!raw.Header.Value.Oid.Equals(NumbersPayloadBindings.IntegerEntry.Oid) ||
              !raw.TryDecodePayloadIntegerEntry(out _)) throw new Exception();
          return raw.Header.Value.Label;
        ", document =>
        {
            var types = document.Modules[0].Types;
            types.Add(new IrTypeDef {Name = "Header", Type = new SequenceType {Components = new List<IrComponent>
            {
                new() {Name = "oid", Type = new OidType()},
                new() {Name = "label", Type = new StringType {Form = "utf8"}}
            }}});
            var extension = (SequenceType)types.Single(t => t.Name == "Extension").Type;
            extension.Components[0] = new IrComponent
            {
                Name = "header",
                Type = new RefType {Name = "Header", Module = "M"},
                Options = IrOptions.Set(null, option, System.Text.Json.Nodes.JsonValue.Create(true)!)
            };
            var any = (AnyType)((OctetStringType)extension.Components[2].Type).Containing!;
            any.Selector = new IrOpenTypeSelector {Path = new List<string> {"header", "oid"}};
        }));
    }

    [Fact]
    public void MixedChoiceStringGroupUsesStaticDecodeStringChoiceForms()
    {
        const string source = @"M DEFINITIONS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          Mixed ::= CHOICE { utf8 UTF8String, printable PrintableString, number INTEGER }
          mixedEntry C ::= { Mixed IDENTIFIED BY {1 2 3} }
          intEntry C ::= { INTEGER IDENTIFIED BY {1 2 4} }
          Numbers C ::= { mixedEntry | intEntry }
          Other C ::= { intEntry }
          AlgorithmIdentifier{C:S} ::= SEQUENCE {
            algorithm C.&id({S}), parameters C.&T({S}{@algorithm}) OPTIONAL }
          Holder ::= SEQUENCE {
            mixed AlgorithmIdentifier{Numbers},
            other AlgorithmIdentifier{Other} }
          END";
        var document = new Asn1Compiler().CompileText(source);
        document.Modules[0].Types.Single(t => t.Name == "AlgorithmIdentifier").Options =
            IrOptions.SetCSharp(null, "typeName", "AlgorithmInfo");
        var generated = Assert.Single(new CSharpBackend().Generate(document)).Contents;
        Assert.Contains("DecodeMixedEntryStringValueForms", generated);
        Assert.Contains("Asn1Codecs.DecodeStringChoice(raw, DecodeMixedEntryStringValueForms)", generated);
        Assert.DoesNotContain("DecodeStringChoice(raw, new[]", generated);
        Assert.Equal("A", Run(@"
          var raw = new AlgorithmInfo();
          raw.SetParameters(NumbersParametersBindings.MixedEntry, Mixed.FromUtf8(""A""));
          if (!raw.TryDecodeParameters(NumbersParametersBindings.MixedEntryStringValue, out var text) ||
              text != ""A"") throw new Exception();
          return text;
        ", document => document.Modules[0].Types.Single(t => t.Name == "AlgorithmIdentifier").Options =
            IrOptions.SetCSharp(null, "typeName", "AlgorithmInfo"), source));
    }

    [Fact]
    public void NamedObjectSetBecomesOpenTypeUseTableAndCatalogStem()
    {
        var document = new Asn1Compiler().CompileText(Source);
        var holder = (SequenceType)document.Modules[0].Types.Single(t => t.Name == "Holder").Type;
        var extensions = Assert.IsType<SequenceOfType>(holder.Components.Single(c => c.Name == "extensions").Type);
        var extensionRef = Assert.IsType<RefType>(extensions.Element);
        var use = Assert.Single(extensionRef.OpenTypes!);
        Assert.Equal("Numbers", use.Table);

        var generated = Assert.Single(new CSharpBackend().Generate(document)).Contents;
        Assert.Contains("public static class NumbersPayloadBindings", generated);
        Assert.Contains("TryGet<T>(this M.Extension[] source, NumbersPayloadBinding<T> binding, out T value)", generated);
        Assert.Equal(42, Run(@"
          var item = new Extension();
          item.SetPayloadIntegerEntry(42);
          var items = new[] { item };
          if (!items.TryGet(NumbersPayloadBindings.IntegerEntry, out var value)) throw new Exception();
          return value;
        "));
    }

    [Fact]
    public void SameTableTryGetSitesReuseOneBindingsCatalog()
    {
        // Same IOC table on two owners must share one Bindings catalog (Name subject/issuer case).
        var source = Source.Replace(
            "unsignedAttrs SET OF Attribute{Flags} OPTIONAL }",
            @"unsignedAttrs SET OF Attribute{Flags} OPTIONAL }
      OtherHolder ::= SEQUENCE { extensions SEQUENCE OF Extension{Numbers} OPTIONAL }");
        var document = new Asn1Compiler().CompileText(source);
        var generated = Assert.Single(new CSharpBackend().Generate(document)).Contents;
        Assert.Contains("public static class NumbersPayloadBindings", generated);
        Assert.DoesNotContain("public static class OtherNumbersPayloadBindings", generated);
        Assert.Contains(
            "TryGetExtensions<T>(this Holder source, NumbersPayloadBinding<T> binding, out T value)",
            generated);
        Assert.Contains(
            "TryGetExtensions<T>(this OtherHolder source, NumbersPayloadBinding<T> binding, out T value)",
            generated);
        Assert.Equal(42, Run(@"
          var item = new Extension();
          item.SetPayload(NumbersPayloadBindings.IntegerEntry, 42);
          var holder = new Holder { Extensions = new[] { item } };
          var other = new OtherHolder { Extensions = new[] { item } };
          if (!holder.TryGetExtensions(NumbersPayloadBindings.IntegerEntry, out var a) || a != 42)
              throw new Exception();
          if (!other.TryGetExtensionsIntegerEntry(out var b) || b != 42) throw new Exception();
          return a;
        ", source: source));
    }
}

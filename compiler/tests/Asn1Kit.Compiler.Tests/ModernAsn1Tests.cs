using System.Reflection;
using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class ModernAsn1Tests
{
    internal const string Example = @"
Modern DEFINITIONS EXPLICIT TAGS ::= BEGIN
ENTRY ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &Type } WITH SYNTAX { &Type IDENTIFIED BY &id }
one ENTRY ::= { INTEGER (0..100) IDENTIFIED BY {1 2 3} }
Entries ENTRY ::= {one, ...}
Record{ENTRY:Table} ::= SEQUENCE {
 id ENTRY.&id({Table}),
 values SET OF ENTRY.&Type({Table}{@id}),
 payload OCTET STRING (CONTAINING ENTRY.&Type({Table}{@id})) OPTIONAL
}
R ::= Record{Entries}
Defaults ::= SEQUENCE { pair Pair DEFAULT {number 7, text ""abc""} }
Pair ::= SEQUENCE { number INTEGER (0..100), text UTF8String }
END";

    [Fact]
    public void ResolvesTemplatesAndObjectTablesIntoValidatedIr()
    {
        var document = new Asn1Compiler().CompileText(Example);
        var module = Assert.Single(document.Modules);
        Assert.DoesNotContain(module.Types, t => t.Name == "ENTRY" || t.Name == "Record");
        var specialization = module.Types.Single(t => t.Name == "R");
        var sequence = Assert.IsType<SequenceType>(specialization.Type);
        var open = Assert.IsType<AnyType>(Assert.IsType<SetOfType>(sequence.Components[1].Type).Element);
        Assert.Equal("1.2.3", Assert.Single(open.Bindings!).Key);
        Assert.Equal("id", Assert.Single(open.Selector!.Path));
        Assert.True(open.TableExtensible);
        Assert.IsType<AnyType>(Assert.IsType<OctetStringType>(sequence.Components[2].Type).Containing);
        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        Assert.Equal(json, IrSerializer.ToJson(IrSerializer.FromJson(json)));
    }

    [Theory]
    [InlineData("one ENTRY ::= { &id {1 2 3} }", "Required")]
    [InlineData("one ENTRY ::= { INTEGER IDENTIFIED BY {1 2 3} } two ENTRY ::= { BOOLEAN IDENTIFIED BY {1 2 3} } Entries ENTRY ::= {one | two}", "UNIQUE")]
    [InlineData("T{ENTRY:S} ::= INTEGER R ::= T{}", "arguments")]
    public void RejectsInvalidInformationObjects(string body, string expected)
    {
        var source = "M DEFINITIONS ::= BEGIN ENTRY ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &Type } WITH SYNTAX { &Type IDENTIFIED BY &id } " + body + " END";
        var error = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText(source));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(error.Line > 0);
    }

    [Fact]
    public void GeneratedCollectionsContainingAndFreshDefaultsRoundTrip()
    {
        var document = new Asn1Compiler().CompileText(Example);
        var assembly = CompileGenerated(new CSharpBackend().Generate(document).Select(f => f.Contents).ToArray());
        var record = assembly.GetType("Modern.R")!;
        var bytes = Convert.FromHexString("300E06022A03310302012A040302012A");
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var value = record.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {reader})!;
        var writer = new Asn1Writer();
        record.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal(bytes, writer.Encode());
        reader.ThrowIfNotEmpty();
        var defaults = assembly.GetTypes().Single(t => t.Name == "Defaults");
        var first = Activator.CreateInstance(defaults)!;
        var second = Activator.CreateInstance(defaults)!;
        Assert.NotSame(defaults.GetProperty("Pair")!.GetValue(first), defaults.GetProperty("Pair")!.GetValue(second));
        writer.Reset();
        defaults.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(first, new object[] {writer});
        Assert.Equal("3000", Convert.ToHexString(writer.Encode()));
    }

    internal static Assembly CompileGenerated(params string[] sources) =>
        GeneratedCompilation.CompileSources(sources);

    [Fact]
    public void ChoiceAndSetOfDefaultsCompareStructurallyAndAreIndependent()
    {
        var source = @"M DEFINITIONS ::= BEGIN
          P ::= SEQUENCE { n INTEGER (0..100), flag BOOLEAN DEFAULT FALSE }
          C ::= CHOICE { n INTEGER (0..100), text UTF8String }
          D ::= SEQUENCE { list SET OF P DEFAULT {{n 1}, {n 2, flag FALSE}, {n 1}}, pick C DEFAULT n:7 }
          END";
        var assembly = CompileGenerated(new CSharpBackend().Generate(new Asn1Compiler().CompileText(source)).Single().Contents);
        var type = assembly.GetType("M.D")!;
        object Create() => Activator.CreateInstance(type)!;
        string Encode(object value)
        {
            var writer = new Asn1Writer();
            type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
            return Convert.ToHexString(writer.Encode());
        }
        var first = Create(); var second = Create();
        var property = type.GetProperty("List")!;
        var list = (Array)property.GetValue(first)!;
        var other = (Array)property.GetValue(second)!;
        Assert.NotSame(list, other); Assert.NotSame(list.GetValue(0), other.GetValue(0));
        var original = list.GetValue(0); list.SetValue(list.GetValue(1), 0); list.SetValue(original, 1);
        Assert.Equal("3000", Encode(first));
        list.GetValue(2)!.GetType().GetProperty("N")!.SetValue(list.GetValue(2), 3);
        Assert.Equal("3011310F300302010130030201023003020103", Encode(first));
        Assert.Equal("3000", Encode(second));
        var decoded = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(Convert.FromHexString("3000"))})!;
        Assert.NotSame(other, property.GetValue(decoded));
        Assert.Equal("3000", Encode(decoded));
    }

    [Fact]
    public void DeepSelectorUsesAnAncestorAndNestedPath()
    {
        var source = @"M DEFINITIONS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          entry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 3} } Items C ::= {entry}
          Header ::= SEQUENCE { id OBJECT IDENTIFIER }
          Outer ::= SEQUENCE { header Header, middle SEQUENCE { leaves SET OF SEQUENCE { values SET OF C.&T({Items}{@header.id}) } } }
          END";
        var document = new Asn1Compiler().CompileText(source);
        var assembly = CompileGenerated(new CSharpBackend().Generate(document).Single().Contents);
        var type = assembly.GetType("M.Outer")!;
        var bytes = Convert.FromHexString("3014300406022A03300C310A3008310602010702012A");
        var value = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(bytes)})!;
        var writer = new Asn1Writer(); type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal(bytes, writer.Encode());
    }

    [Fact]
    public void ExtensionBoundaryAllowsAbsentOptionalTrailingRoot()
    {
        var document = new Asn1Compiler().CompileText(@"M DEFINITIONS IMPLICIT TAGS ::= BEGIN
          E ::= SEQUENCE { root INTEGER, ..., a [0] BOOLEAN, ..., tail [1] BOOLEAN OPTIONAL, last [2] INTEGER }
          END");
        var assembly = CompileGenerated(new CSharpBackend().Generate(document).Single().Contents);
        var type = assembly.GetType("M.E")!;
        var value = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(Convert.FromHexString("300902010189010082012A"))})!;
        Assert.Null(type.GetProperty("A")!.GetValue(value));
        Assert.Null(type.GetProperty("Tail")!.GetValue(value));
        Assert.Equal(42, ((Asn1Integer)type.GetProperty("Last")!.GetValue(value)!).GetInt32());
        var writer = new Asn1Writer(); type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal("300602010182012A", Convert.ToHexString(writer.Encode()));
    }

    [Theory]
    [InlineData("[[-1: a [0] INTEGER]]")]
    [InlineData("[[2: a [0] INTEGER]], [[2: b [1] INTEGER]]")]
    [InlineData("[[]]")]
    public void InvalidExtensionGroupsHaveCompilerDiagnostics(string additions)
    {
        var error = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText("M DEFINITIONS ::= BEGIN E ::= SEQUENCE { root INTEGER, ..., " + additions + " } END"));
        Assert.True(error.Line > 0); Assert.Contains("extension group", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkipsUnknownExtensionsAndValidatesGroups()
    {
        var document = new Asn1Compiler().CompileText(@"M DEFINITIONS IMPLICIT TAGS ::= BEGIN
          E ::= SEQUENCE { root INTEGER (0..100), ..., [[2: a [0] INTEGER (0..100), b [1] BOOLEAN OPTIONAL]], c [2] NULL }
          C ::= CHOICE { known INTEGER, ... }
          END");
        var assembly = CompileGenerated(new CSharpBackend().Generate(document).Single().Contents);
        var type = assembly.GetType("M.E")!;
        Assert.Null(type.GetProperty("UnknownExtensions"));
        object Decode(string hex) => type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(Convert.FromHexString(hex), Asn1Encoding.Der)})!;
        var rootOnly = Decode("3003020101");
        Assert.Null(type.GetProperty("A")!.GetValue(rootOnly));
        var value = Decode("300C0201018001078901FF8101FF");
        Assert.Equal(7, type.GetProperty("A")!.GetValue(value));
        Assert.True((bool)type.GetProperty("B")!.GetValue(value)!);
        var writer = new Asn1Writer();
        type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal("30090201018001078101FF", Convert.ToHexString(writer.Encode()));
        var error = Assert.Throws<TargetInvocationException>(() => Decode("30060201018101FF"));
        Assert.IsType<Asn1Exception>(error.InnerException);
        var choice = assembly.GetType("M.C")!;
        var unknown = choice.GetMethod("Decode")!.Invoke(null, new object[] {new Asn1Reader(Convert.FromHexString("850100"))});
        writer.Reset();
        choice.GetMethod("Encode")!.Invoke(unknown, new object[] {writer});
        Assert.Equal("850100", Convert.ToHexString(writer.Encode()));
    }

    [Fact]
    public void SelectsFromAnAncestorThroughCollections()
    {
        var source = @"M DEFINITIONS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          item C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 3} }
          Items C ::= {item, ...}
          Outer ::= SEQUENCE { id OBJECT IDENTIFIER, nested SEQUENCE OF SEQUENCE {
            value C.&T({Items}{@id}) } }
          END";
        var document = new Asn1Compiler().CompileText(source);
        var assembly = CompileGenerated(new CSharpBackend().Generate(document).Single().Contents);
        var type = assembly.GetType("M.Outer")!;
        var bytes = Convert.FromHexString("300B06022A033005300302012A");
        var value = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(bytes, Asn1Encoding.Der)});
        var writer = new Asn1Writer();
        type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal(bytes, writer.Encode());
    }

    [Fact]
    public void SpecializationsReuseStableNamesAcrossModuleOrderAndQualifiedImports()
    {
        const string library = @"Lib DEFINITIONS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          R{C:S} ::= SEQUENCE { id C.&id({S}), value C.&T({S}{@id}) }
          END";
        const string first = @"First DEFINITIONS ::= BEGIN IMPORTS C FROM Lib;
          entry C ::= { INTEGER IDENTIFIED BY {1 2 3} } Items C ::= {entry} END";
        const string second = @"Second DEFINITIONS ::= BEGIN IMPORTS C FROM Lib;
          entry C ::= { BOOLEAN IDENTIFIED BY {1 2 4} } Items C ::= {entry} END";
        const string consumer = @"Use DEFINITIONS ::= BEGIN IMPORTS R{}, C FROM Lib Items FROM First Items FROM Second;
          A ::= R{First.Items} B ::= R{Second.Items} Again ::= R{First.Items} END";
        var inputs = new[] {library, first, second, consumer};
        IrDocument Compile(IEnumerable<string> source) => new Asn1Compiler().CompileTexts(source.Select(s => (s, (string?)null)));
        var forward = Compile(inputs);
        var reverse = Compile(inputs.Reverse());
        var templates = forward.Modules.Single(m => m.Name == "Lib").Types.Where(t => t.Name == "R").ToArray();
        Assert.Single(templates);
        var use = forward.Modules.Single(m => m.Name == "Use");
        Assert.Equal(Assert.IsType<RefType>(use.Types[0].Type).Name, Assert.IsType<RefType>(use.Types[2].Type).Name);
        Assert.Equal(templates.Select(t => t.Name).OrderBy(n => n), reverse.Modules.Single(m => m.Name == "Lib").Types.Select(t => t.Name).OrderBy(n => n));
        foreach (var template in templates)
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(template.Type, IrSerializer.JsonOptions),
                System.Text.Json.JsonSerializer.Serialize(reverse.Modules.Single(m => m.Name == "Lib").Types.Single(t => t.Name == template.Name).Type, IrSerializer.JsonOptions));
        Assert.Throws<CompileException>(() => Compile(new[] {library, first, second, consumer.Replace("First.Items", "Items")}));
        const string another = "Another DEFINITIONS ::= BEGIN IMPORTS R{} FROM Lib Items FROM First; Same ::= R{Items} END";
        var shared = Compile(inputs.Append(another));
        Assert.Single(shared.Modules.Single(m => m.Name == "Lib").Types.Where(t => t.Name == "R"));
        Assert.Equal(Assert.IsType<RefType>(shared.Modules.Single(m => m.Name == "Use").Types[0].Type).Name,
            Assert.IsType<RefType>(shared.Modules.Single(m => m.Name == "Another").Types[0].Type).Name);
    }

    [Fact]
    public void TableOnlySpecializationsShareTypeAndDecodeAtTheirUseSites()
    {
        const string source = @"M DEFINITIONS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          integerEntry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 3} }
          booleanEntry C ::= { BOOLEAN IDENTIFIED BY {1 2 4} }
          Integers C ::= {integerEntry} Booleans C ::= {booleanEntry}
          Box{C:S} ::= SEQUENCE { id C.&id({S}), value C.&T({S}{@id}) }
          A ::= SEQUENCE { item Box{Integers} }
          B ::= SET { item Box{Booleans} }
          END";
        var document = new Asn1Compiler().CompileText(source);
        var module = Assert.Single(document.Modules);
        Assert.Single(module.Types.Where(t => t.Name == "Box"));
        Assert.DoesNotContain(module.Types, t => t.Name.Contains('-', StringComparison.Ordinal));
        var a = Assert.IsType<SequenceType>(module.Types.Single(t => t.Name == "A").Type);
        var b = Assert.IsType<SetType>(module.Types.Single(t => t.Name == "B").Type);
        Assert.Equal("1.2.3", Assert.Single(Assert.Single(Assert.IsType<RefType>(a.Components[0].Type).OpenTypes!).Bindings).Key);
        Assert.Equal("1.2.4", Assert.Single(Assert.Single(Assert.IsType<RefType>(b.Components[0].Type).OpenTypes!).Bindings).Key);
        var json = IrSerializer.ToJson(document);
        IrSerializer.ValidateSchema(json);
        Assert.Equal(json, IrSerializer.ToJson(IrSerializer.FromJson(json)));
        var usePath = Assert.Single(Assert.IsType<RefType>(a.Components[0].Type).OpenTypes!).Path;
        usePath[0] = "missing";
        Assert.Throws<IrException>(() => IrValidator.Validate(document));
        usePath[0] = "value";

        var code = new CSharpBackend().Generate(document).Single().Contents;
        Assert.DoesNotContain("Box-", code, StringComparison.Ordinal);
        var assembly = CompileGenerated(code);
        var aType = assembly.GetType("M.A")!;
        var bType = assembly.GetType("M.B")!;
        object Decode(Type type, string hex) => type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!
            .Invoke(null, new object[] {new Asn1Reader(Convert.FromHexString(hex))})!;
        var aValue = Decode(aType, "3009300706022A0302012A");
        var bValue = Decode(bType, "3109300706022A040101FF");
        var aBinding = assembly.GetType("M.IntegersValueBindings")!.GetProperty("IntegerEntry")!.GetValue(null)!;
        var bBinding = assembly.GetType("M.BooleansValueBindings")!.GetProperty("BooleanEntry")!.GetValue(null)!;
        var extensions = assembly.GetType("M.MOpenTypeExtensions")!;
        var aItem = aType.GetProperty("Item")!.GetValue(aValue)!;
        var bItem = bType.GetProperty("Item")!.GetValue(bValue)!;
        MethodInfo DecodeMethod(Type resultType) => extensions.GetMethods()
            .Where(m => m.Name == "TryDecodeValue" && m.IsGenericMethodDefinition)
            .Select(m => new { Method = m, Parameters = m.GetParameters() })
            .Single(m => m.Parameters.Length == 3 &&
                         m.Parameters[0].ParameterType.Name == "Box" &&
                         m.Parameters[1].ParameterType.Name.StartsWith("ValueBinding", StringComparison.Ordinal) &&
                         m.Parameters[2].Name == "value")
            .Method.MakeGenericMethod(resultType);
        var aDecode = DecodeMethod(typeof(int));
        var bDecode = DecodeMethod(typeof(bool));
        object?[] aArgs = { aItem, aBinding, null };
        object?[] bArgs = { bItem, bBinding, null };
        Assert.True((bool)aDecode.Invoke(null, aArgs)!);
        Assert.Equal(42, aArgs[2]);
        Assert.True((bool)bDecode.Invoke(null, bArgs)!);
        Assert.Equal(true, bArgs[2]);
        // Shared ValueBinding shape: a Booleans key on an Integers carrier simply does not match.
        Assert.False((bool)bDecode.Invoke(null, new[] {aItem, bBinding, null})!);

        var unknown = Decode(aType, "3009300706022A0502012A");
        var unknownItem = aType.GetProperty("Item")!.GetValue(unknown)!;
        Assert.False((bool)aDecode.Invoke(null, new[] {unknownItem, aBinding, null})!);
        var malformed = Decode(aType, "3009300706022A030101FF");
        var malformedItem = aType.GetProperty("Item")!.GetValue(malformed)!;
        var error = Assert.Throws<TargetInvocationException>(() =>
            aDecode.Invoke(null, new[] {malformedItem, aBinding, null}));
        Assert.IsType<Asn1Exception>(error.InnerException);

        extensions.GetMethods()
            .Where(m => m.Name == "SetValue" && m.IsGenericMethodDefinition)
            .Select(m => new { Method = m, Parameters = m.GetParameters() })
            .Single(m => m.Parameters.Length == 3 &&
                         m.Parameters[0].ParameterType.Name == "Box" &&
                         m.Parameters[1].ParameterType.Name.StartsWith("ValueBinding", StringComparison.Ordinal))
            .Method.MakeGenericMethod(typeof(int))
            .Invoke(null, new[] {aItem, aBinding, (object)42});
        var writer = new Asn1Writer();
        aType.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(aValue, new object[] { writer });
        Assert.Equal("3009300706022A0302012A", Convert.ToHexString(writer.Encode()));
    }

    [Fact]
    public void EnumImplicitNumbersSkipAllExplicitRootValues()
    {
        var document = new Asn1Compiler().CompileText("M DEFINITIONS ::= BEGIN E ::= ENUMERATED {a, b(0), c(5), d, ..., e, f(9), g} END");
        var values = Assert.IsType<EnumeratedType>(Assert.Single(document.Modules[0].Types).Type).Values;
        Assert.Equal(new long[] {1, 0, 5, 2, 3, 9, 10}, values!.Select(v => v.Value));
        Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText("M DEFINITIONS ::= BEGIN E ::= ENUMERATED {a(1), b(1)} END"));
    }

    [Fact]
    public void EmptyExtensibleTableRemainsEmptyAndUnknownOptionsSurvive()
    {
        var document = new Asn1Compiler().CompileText(Example.Replace("R ::= Record{Entries}", "R ::= Record{{...}}"));
        var type = Assert.IsType<SequenceType>(document.Modules[0].Types.Single(t => t.Name == "R").Type);
        Assert.Empty(Assert.IsType<AnyType>(Assert.IsType<SetOfType>(type.Components[1].Type).Element).Bindings!);
        document.Options = new System.Text.Json.Nodes.JsonObject { ["future"] = new System.Text.Json.Nodes.JsonObject { ["enabled"] = true } };
        var json = IrSerializer.ToJson(document);
        Assert.Equal(json, IrSerializer.ToJson(IrSerializer.FromJson(json)));
    }

    [Fact]
    public void SetDefersValuesUntilItsDiscriminatorHasBeenRead()
    {
        var source = Example.Replace("Record{ENTRY:Table} ::= SEQUENCE", "Record{ENTRY:Table} ::= SET")
            .Replace("id ENTRY.&id", "id [1] IMPLICIT ENTRY.&id").Replace("values SET OF", "values [0] IMPLICIT SET OF");
        var assembly = CompileGenerated(new CSharpBackend().Generate(new Asn1Compiler().CompileText(source)).Single().Contents);
        var type = assembly.GetType("Modern.R")!;
        var bytes = Convert.FromHexString("3109A00302012A81022A03");
        var value = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(bytes, Asn1Encoding.Der)});
        var items = (Array)type.GetProperty("Values")!.GetValue(value)!;
        Assert.Equal(42, items.GetValue(0)!.GetType().GetProperty("One")!.GetValue(items.GetValue(0)));
        var writer = new Asn1Writer();
        type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal(bytes, writer.Encode());
    }

    [Fact]
    public void AutomaticTagsWrapOpenValuesAndUnknownOidDoesNotGuess()
    {
        var source = @"M DEFINITIONS AUTOMATIC TAGS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          one C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 3} } Items C ::= {one, ...}
          R ::= SEQUENCE { id C.&id({Items}), value C.&T({Items}{@id}) } END";
        var assembly = CompileGenerated(new CSharpBackend().Generate(new Asn1Compiler().CompileText(source)).Single().Contents);
        var type = assembly.GetType("M.R")!;
        object Decode(string hex) => type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(Convert.FromHexString(hex), Asn1Encoding.Der)})!;
        var value = Decode("300980022A03A10302012A");
        var writer = new Asn1Writer();
        type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal("300980022A03A10302012A", Convert.ToHexString(writer.Encode()));
        var unknown = type.GetProperty("Value")!.GetValue(Decode("300980022A04A10302012A"))!;
        Assert.NotNull(unknown.GetType().GetProperty("Unknown")!.GetValue(unknown));
        Assert.Null(unknown.GetType().GetProperty("One")!.GetValue(unknown));
        var error = Assert.Throws<TargetInvocationException>(() => Decode("300980022A03A1030101FF"));
        Assert.IsType<Asn1Exception>(error.InnerException);
    }

    [Fact]
    public void ForwardObjectsOptionalSyntaxAndDefaultsResolveWithoutPublicClasses()
    {
        var source = @"M DEFINITIONS ::= BEGIN
          Picked ::= entry.&T
          selected OBJECT IDENTIFIER ::= entry.&id
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T DEFAULT INTEGER (0..100), &flag BOOLEAN DEFAULT FALSE }
            WITH SYNTAX { [TYPE &T] IDENTIFIED BY &id [FLAG &flag] }
          Entries C ::= {entry | second, ...}
          entry C ::= { IDENTIFIED BY {1 2 3} }
          second C ::= { TYPE BOOLEAN IDENTIFIED BY {1 2 4} FLAG TRUE }
          P{INTEGER:n} ::= SEQUENCE { v INTEGER (0..100) DEFAULT n }
          Fixed ::= P{7}
          END";
        var document = new Asn1Compiler().CompileText(source);
        Assert.IsType<IntegerType>(document.Modules[0].Types.Single(t => t.Name == "Picked").Type);
        Assert.Equal("1.2.3", Assert.IsType<IrOidValue>(document.Modules[0].Values.Single().Value).Value);
        CompileGenerated(new CSharpBackend().Generate(document).Single().Contents);
    }

    [Theory]
    [InlineData("C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &n INTEGER } entry C ::= { &id {1 2 3}, &n TRUE }", "governing")]
    [InlineData("C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &n INTEGER } entry C ::= { &id {1 2 3}, &n missing }", "governing")]
    [InlineData("C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE } D ::= CLASS { &id OBJECT IDENTIFIER UNIQUE } entry D ::= { &id {1 2 3} } Entries C ::= {entry}", "class")]
    [InlineData("P{INTEGER:n} ::= INTEGER T ::= P{TRUE}", "governing")]
    [InlineData("P{T} ::= SEQUENCE { value T } R ::= P", "arguments")]
    [InlineData("P{T} ::= INTEGER R ::= P{TRUE}", "type")]
    [InlineData("value INTEGER ::= 7 P{T} ::= INTEGER R ::= P{value}", "type")]
    [InlineData("P{T, T} ::= T", "Duplicate")]
    [InlineData("C{T} ::= CLASS { &value T }", "profile")]
    [InlineData("C ::= CHOICE { n INTEGER } wrong C ::= unknown:1", "alternative")]
    [InlineData("C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } entry C ::= { &id {1 2 3}, &T INTEGER } Items C ::= {entry} R ::= SEQUENCE { id BOOLEAN, value C.&T({Items}{@id}) }", "selector")]
    public void InvalidGovernorsParametersAndSelectorsHavePositions(string declarations, string expected)
    {
        var error = Assert.Throws<CompileException>(() => new Asn1Compiler().CompileText("M DEFINITIONS ::= BEGIN " + declarations + " END", "invalid-modern.asn"));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(error.Line > 0); Assert.True(error.Column > 0);
    }

    [Fact]
    public void RecursiveTemplateReusesSpecializationAndExpandingRecursionFails()
    {
        var compiler = new Asn1Compiler();
        var document = compiler.CompileText("M DEFINITIONS ::= BEGIN Node{T} ::= SEQUENCE { value T, next Node{T} OPTIONAL } Root ::= Node{INTEGER} END");
        Assert.Single(document.Modules[0].Types.Where(t => t.Name == "Root"));
        CompileGenerated(new CSharpBackend().Generate(document).Single().Contents);
        var error = Assert.Throws<CompileException>(() => compiler.CompileText("M DEFINITIONS ::= BEGIN P{T} ::= SEQUENCE { next P{SEQUENCE OF T} OPTIONAL } R ::= P{INTEGER} END"));
        Assert.Contains("limit", error.Message);
    }

    [Fact]
    public void TypedValueIrValidatesItsEmbeddedType()
    {
        var document = new Asn1Compiler().CompileText("M DEFINITIONS ::= BEGIN P ::= SEQUENCE { value ANY DEFAULT INTEGER:7 } END");
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        var typed = Assert.IsType<IrTypedValue>(Assert.IsType<SequenceType>(document.Modules[0].Types[0].Type).Components[0].Default);
        typed.Type = new RefType { Name = "Missing" };
        Assert.Throws<IrException>(() => IrValidator.Validate(document));
    }

    [Fact]
    public void IntegerTablePreservesUnknownAlternativeOfKnownExtensibleChoice()
    {
        var source = @"M DEFINITIONS ::= BEGIN
          C ::= CLASS { &id INTEGER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          entry C ::= { CHOICE { number INTEGER (0..100), ... } IDENTIFIED BY 1 }
          Items C ::= {entry, ...}
          R ::= SEQUENCE { id INTEGER (0..10), value C.&T({Items}{@id}) }
          END";
        var assembly = CompileGenerated(new CSharpBackend().Generate(new Asn1Compiler().CompileText(source)).Single().Contents);
        var type = assembly.GetType("M.R")!;
        var bytes = Convert.FromHexString("3006020101850100");
        var value = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(bytes)})!;
        var open = type.GetProperty("Value")!.GetValue(value)!;
        Assert.NotNull(open.GetType().GetProperty("Entry")!.GetValue(open));
        var writer = new Asn1Writer(); type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal(bytes, writer.Encode());
    }

    [Fact]
    public void SetDefersNestedFieldsThatUseAnAncestor()
    {
        var source = @"M DEFINITIONS IMPLICIT TAGS ::= BEGIN
          C ::= CLASS { &id OBJECT IDENTIFIER UNIQUE, &T } WITH SYNTAX { &T IDENTIFIED BY &id }
          entry C ::= { INTEGER (0..100) IDENTIFIED BY {1 2 3} } Items C ::= {entry}
          R ::= SET { nested [0] SEQUENCE { value C.&T({Items}{@id}) }, id [1] OBJECT IDENTIFIER }
          END";
        var assembly = CompileGenerated(new CSharpBackend().Generate(new Asn1Compiler().CompileText(source)).Single().Contents);
        var type = assembly.GetType("M.R")!;
        var bytes = Convert.FromHexString("3109A00302012A81022A03");
        var value = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(bytes)})!;
        var nested = type.GetProperty("Nested")!.GetValue(value)!;
        var open = nested.GetType().GetProperty("Value")!.GetValue(nested)!;
        Assert.Equal(42, open.GetType().GetProperty("Entry")!.GetValue(open));
        var writer = new Asn1Writer(); type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal(bytes, writer.Encode());
    }

    [Theory]
    [InlineData("300F06022A0331002480040302012A0000", true, "02012A")]
    [InlineData("300A06022A0431000402FFFF", false, "FFFF")]
    public void GeneratedContainingReadsBerAndKeepsUnknownContents(string hex, bool known, string contents)
    {
        var assembly = CompileGenerated(new CSharpBackend().Generate(new Asn1Compiler().CompileText(Example)).Single().Contents);
        var type = assembly.GetType("Modern.R")!;
        var value = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(Convert.FromHexString(hex), Asn1Encoding.Ber)})!;
        var payload = type.GetProperty("Payload")!.GetValue(value)!;
        Assert.Equal(known, payload.GetType().GetProperty("HasValue")!.GetValue(payload));
        Assert.Equal(contents, Convert.ToHexString(((ReadOnlyMemory<byte>)payload.GetType().GetProperty("Contents")!.GetValue(payload)!).Span));
    }

    [Fact]
    public void GeneratedContainingRejectsDamagedKnownContents()
    {
        var assembly = CompileGenerated(new CSharpBackend().Generate(new Asn1Compiler().CompileText(Example)).Single().Contents);
        var type = assembly.GetType("Modern.R")!;
        var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null,
            new object[] {new Asn1Reader(Convert.FromHexString("300A06022A03310004020201"))}));
        Assert.IsType<Asn1Exception>(error.InnerException);
    }

    [Fact]
    public void AnonymousTypeArgumentsKeepTheirModulesTagDefaults()
    {
        var source = new[]
        {
            "Lib DEFINITIONS EXPLICIT TAGS ::= BEGIN Wrap{T} ::= SEQUENCE { value T } END",
            "Auto DEFINITIONS AUTOMATIC TAGS ::= BEGIN IMPORTS Wrap{} FROM Lib; Number ::= INTEGER (0..100) R ::= Wrap{SEQUENCE { v Number }} END",
            "Explicit DEFINITIONS EXPLICIT TAGS ::= BEGIN IMPORTS Wrap{} FROM Lib; Number ::= INTEGER (0..100) R ::= Wrap{SEQUENCE { v Number }} END"
        };
        var document = new Asn1Compiler().CompileTexts(source.Select(s => (s, (string?)null)));
        var library = document.Modules.Single(m => m.Name == "Lib");
        Assert.Empty(library.Types);
        var assembly = CompileGenerated(new CSharpBackend().Generate(document).Select(f => f.Contents).ToArray());
        foreach (var (moduleName, hex) in new[] { ("Auto", "3005300380012A"), ("Explicit", "3005300302012A") })
        {
            Assert.IsType<SequenceType>(document.Modules.Single(m => m.Name == moduleName).Types.Single(t => t.Name == "R").Type);
            var type = assembly.GetType(moduleName + ".R")!;
            var bytes = Convert.FromHexString(hex);
            var value = type.GetMethod("Decode", new[] {typeof(Asn1Reader)})!.Invoke(null, new object[] {new Asn1Reader(bytes)})!;
            var writer = new Asn1Writer(); type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
            Assert.Equal(bytes, writer.Encode());
        }
    }

    [Fact]
    public void StructuredDefaultUsesTheRenamedMemberWhenItMatchesItsTypeName()
    {
        var document = new Asn1Compiler().CompileText("M DEFINITIONS ::= BEGIN P ::= SEQUENCE { p INTEGER (0..100) } D ::= SEQUENCE { value P DEFAULT {p 7} } END");
        var assembly = CompileGenerated(new CSharpBackend().Generate(document).Single().Contents);
        var type = assembly.GetType("M.D")!;
        var value = Activator.CreateInstance(type)!;
        var writer = new Asn1Writer();
        type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal("3000", Convert.ToHexString(writer.Encode()));
        var pair = type.GetProperty("Value")!.GetValue(value)!;
        pair.GetType().GetProperty("PValue")!.SetValue(pair, 42);
        writer.Reset(); type.GetMethod("Encode", new[] {typeof(Asn1Writer)})!.Invoke(value, new object[] {writer});
        Assert.Equal("3005300302012A", Convert.ToHexString(writer.Encode()));
    }
}

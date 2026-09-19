using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Asn1Kit.Ir;

public sealed class IrDocument
{
    public int IrVersion { get; set; } = 1;

    public List<string>? SourceFiles { get; set; }

    public JsonObject? Options { get; set; }

    public List<IrModule> Modules { get; set; } = new();
}

public sealed class IrModule
{
    public string Name { get; set; } = "";

    public string? Oid { get; set; }

    public string TagDefault { get; set; } = TagDefaults.Explicit;

    public List<IrImport> Imports { get; set; } = new();

    public JsonObject? Options { get; set; }

    public List<IrTypeDef> Types { get; set; } = new();

    public List<IrValueDef> Values { get; set; } = new();
}

public sealed class IrImport
{
    public string Module { get; set; } = "";

    public List<string> Types { get; set; } = new();

    public List<string>? Values { get; set; }
}

public sealed class IrTypeDef
{
    public string Name { get; set; } = "";

    public TypeExpr Type { get; set; } = null!;

    public JsonObject? Options { get; set; }
}

public sealed class IrValueDef
{
    public string Name { get; set; } = "";

    public TypeExpr Type { get; set; } = null!;

    public IrValue Value { get; set; } = null!;

    public JsonObject? Options { get; set; }
}

public sealed class IrComponent
{
    public string Name { get; set; } = "";

    public TypeExpr Type { get; set; } = null!;

    public bool Optional { get; set; }

    public IrValue? Default { get; set; }

    public JsonObject? Options { get; set; }
}

public sealed class IrTag
{
    public string Class { get; set; } = TagClasses.Context;

    public int Number { get; set; }

    public string Mode { get; set; } = TagModes.Implicit;
}

public sealed class IrNamedNumber
{
    public string Name { get; set; } = "";

    public long Value { get; set; }
}

public sealed class IrConstraint
{
    public IrBound? Size { get; set; }

    public IrBound? Value { get; set; }

    public string? Unsupported { get; set; }
}

public sealed class IrBound
{
    public long Min { get; set; }

    public long? Max { get; set; }
}

[JsonConverter(typeof(IrValueConverter))]
public abstract class IrValue
{
    [JsonIgnore]
    public abstract string Kind { get; }
}

public sealed class IrIntegerValue : IrValue
{
    public override string Kind => ValueKinds.Integer;

    public long Value { get; set; }
}

public sealed class IrBooleanValue : IrValue
{
    public override string Kind => ValueKinds.Boolean;

    public bool Value { get; set; }
}

public sealed class IrNullValue : IrValue
{
    public override string Kind => ValueKinds.Null;
}

public sealed class IrOidValue : IrValue
{
    public override string Kind => ValueKinds.Oid;

    public string Value { get; set; } = "";
}

public sealed class IrStringValue : IrValue
{
    public override string Kind => ValueKinds.String;

    public string Value { get; set; } = "";
}

public sealed class IrBitStringValue : IrValue
{
    public override string Kind => ValueKinds.BitString;

    public string? Bits { get; set; }

    public string? Hex { get; set; }
}

public sealed class IrValueRef : IrValue
{
    public override string Kind => ValueKinds.Ref;

    public string Name { get; set; } = "";

    public string? Module { get; set; }
}

[JsonConverter(typeof(TypeExprConverter))]
public abstract class TypeExpr
{
    public IrTag? Tag { get; set; }

    public IrConstraint? Constraint { get; set; }

    public JsonObject? Options { get; set; }

    [JsonIgnore]
    public abstract string Kind { get; }
}

public sealed class BooleanType : TypeExpr
{
    public override string Kind => TypeKinds.Boolean;
}

public sealed class NullType : TypeExpr
{
    public override string Kind => TypeKinds.Null;
}

public sealed class OctetStringType : TypeExpr
{
    public override string Kind => TypeKinds.OctetString;
}

public sealed class OidType : TypeExpr
{
    public override string Kind => TypeKinds.Oid;
}

public sealed class IntegerType : TypeExpr
{
    public override string Kind => TypeKinds.Integer;

    public List<IrNamedNumber>? NamedValues { get; set; }
}

public sealed class EnumeratedType : TypeExpr
{
    public override string Kind => TypeKinds.Enumerated;

    public List<IrNamedNumber> Values { get; set; } = new();
}

public sealed class BitStringType : TypeExpr
{
    public override string Kind => TypeKinds.BitString;

    public List<IrNamedNumber>? NamedBits { get; set; }
}

public sealed class StringType : TypeExpr
{
    public override string Kind => TypeKinds.String;

    [JsonPropertyName("stringType")]
    public string Form { get; set; } = "";
}

public sealed class TimeType : TypeExpr
{
    public override string Kind => TypeKinds.Time;

    [JsonPropertyName("timeType")]
    public string Form { get; set; } = "";
}

public sealed class AnyType : TypeExpr
{
    public override string Kind => TypeKinds.Any;

    public string? DefinedBy { get; set; }
}

public sealed class SequenceType : TypeExpr
{
    public override string Kind => TypeKinds.Sequence;

    public List<IrComponent> Components { get; set; } = new();

    public bool Extensible { get; set; }
}

public sealed class SetType : TypeExpr
{
    public override string Kind => TypeKinds.Set;

    public List<IrComponent> Components { get; set; } = new();

    public bool Extensible { get; set; }
}

public sealed class ChoiceType : TypeExpr
{
    public override string Kind => TypeKinds.Choice;

    public List<IrComponent> Components { get; set; } = new();

    public bool Extensible { get; set; }
}

public sealed class SequenceOfType : TypeExpr
{
    public override string Kind => TypeKinds.SequenceOf;

    public TypeExpr Element { get; set; } = null!;
}

public sealed class SetOfType : TypeExpr
{
    public override string Kind => TypeKinds.SetOf;

    public TypeExpr Element { get; set; } = null!;
}

public sealed class RefType : TypeExpr
{
    public override string Kind => TypeKinds.Ref;

    public string Name { get; set; } = "";

    public string? Module { get; set; }
}

public static class TypeKinds
{
    public const string Boolean = "boolean";
    public const string Null = "null";
    public const string OctetString = "octetString";
    public const string Oid = "oid";
    public const string Integer = "integer";
    public const string Enumerated = "enumerated";
    public const string BitString = "bitString";
    public const string String = "string";
    public const string Time = "time";
    public const string Any = "any";
    public const string Sequence = "sequence";
    public const string Set = "set";
    public const string Choice = "choice";
    public const string SequenceOf = "sequenceOf";
    public const string SetOf = "setOf";
    public const string Ref = "ref";
}

public static class ValueKinds
{
    public const string Integer = "integer";
    public const string Boolean = "boolean";
    public const string Null = "null";
    public const string Oid = "oid";
    public const string String = "string";
    public const string BitString = "bitString";
    public const string Ref = "ref";
}

public static class StringTypes
{
    public const string Utf8 = "utf8";
    public const string Printable = "printable";
    public const string Teletex = "teletex";
    public const string T61 = "t61";
    public const string Ia5 = "ia5";
    public const string Numeric = "numeric";
    public const string Visible = "visible";
    public const string Bmp = "bmp";
    public const string Universal = "universal";
    public const string General = "general";
    public const string Graphic = "graphic";
    public const string Videotex = "videotex";
}

public static class TimeTypes
{
    public const string Utc = "utc";
    public const string Generalized = "generalized";
}

public static class TagDefaults
{
    public const string Explicit = "explicit";
    public const string Implicit = "implicit";
    public const string Automatic = "automatic";
}

public static class TagClasses
{
    public const string Universal = "universal";
    public const string Application = "application";
    public const string Context = "context";
    public const string Private = "private";
}

public static class TagModes
{
    public const string Implicit = "implicit";
    public const string Explicit = "explicit";
}

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

    public List<int>? Oid { get; set; }

    public string TagDefault { get; set; } = TagDefaults.Explicit;

    public List<IrImport> Imports { get; set; } = new();

    public JsonObject? Options { get; set; }

    public List<IrTypeDef> Types { get; set; } = new();
}

public sealed class IrImport
{
    public string Module { get; set; } = "";

    public List<string> Types { get; set; } = new();
}

public sealed class IrTypeDef
{
    public string Name { get; set; } = "";

    public TypeExpr Type { get; set; } = null!;

    public JsonObject? Options { get; set; }
}

public sealed class IrComponent
{
    public string Name { get; set; } = "";

    public TypeExpr Type { get; set; } = null!;

    public bool Optional { get; set; }

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

[JsonConverter(typeof(TypeExprConverter))]
public abstract class TypeExpr
{
    public IrTag? Tag { get; set; }

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

public sealed class SequenceType : TypeExpr
{
    public override string Kind => TypeKinds.Sequence;

    public List<IrComponent> Components { get; set; } = new();
}

public sealed class ChoiceType : TypeExpr
{
    public override string Kind => TypeKinds.Choice;

    public List<IrComponent> Components { get; set; } = new();
}

public sealed class SequenceOfType : TypeExpr
{
    public override string Kind => TypeKinds.SequenceOf;

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
    public const string Sequence = "sequence";
    public const string Choice = "choice";
    public const string SequenceOf = "sequenceOf";
    public const string Ref = "ref";
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

namespace Asn1Kit.Ir;

public sealed class IrModule
{
    public int IrVersion { get; set; } = 1;

    public string Module { get; set; } = "";

    public string? Oid { get; set; }

    public string? Source { get; set; }

    public string TagDefault { get; set; } = TagDefaults.Explicit;

    public List<IrImport> Imports { get; set; } = new();

    public Dictionary<string, object?> Options { get; set; } = new(StringComparer.Ordinal);

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

    /// <summary>sequence, choice, sequence-of, alias</summary>
    public string Kind { get; set; } = "";

    /// <summary>For alias: referenced or builtin type name.</summary>
    public string? Type { get; set; }

    /// <summary>For sequence-of: element type name.</summary>
    public string? ElementType { get; set; }

    public IrTag? Tag { get; set; }

    public List<IrNamedNumber>? NamedNumbers { get; set; }

    public List<IrField>? Fields { get; set; }

    public Dictionary<string, object?> Options { get; set; } = new(StringComparer.Ordinal);
}

public sealed class IrField
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    public bool Optional { get; set; }

    public IrTag? Tag { get; set; }

    public Dictionary<string, object?> Options { get; set; } = new(StringComparer.Ordinal);
}

public sealed class IrTag
{
    /// <summary>universal, application, context, private</summary>
    public string Class { get; set; } = TagClasses.Context;

    public int Number { get; set; }

    /// <summary>implicit or explicit</summary>
    public string Mode { get; set; } = TagModes.Implicit;
}

public sealed class IrNamedNumber
{
    public string Name { get; set; } = "";

    public long Value { get; set; }
}

public static class TypeKinds
{
    public const string Sequence = "sequence";
    public const string Choice = "choice";
    public const string SequenceOf = "sequence-of";
    public const string Alias = "alias";
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

public static class BuiltinTypes
{
    public const string Boolean = "BOOLEAN";
    public const string Integer = "INTEGER";
    public const string OctetString = "OCTET STRING";
    public const string Null = "NULL";
    public const string ObjectIdentifier = "OBJECT IDENTIFIER";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Boolean, Integer, OctetString, Null, ObjectIdentifier
    };

    public static bool IsBuiltin(string name) => All.Contains(name);
}

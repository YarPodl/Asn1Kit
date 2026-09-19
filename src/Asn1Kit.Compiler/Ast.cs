namespace Asn1Kit.Compiler;

internal enum TagDefaultKind
{
    Explicit,
    Implicit,
    Automatic
}

internal abstract class AstNode
{
    public int Line { get; set; } = 1;
    public int Column { get; set; } = 1;
}

internal sealed class ModuleAst : AstNode
{
    public string Name { get; init; } = "";
    public List<int>? Oid { get; init; }
    public string? Source { get; init; }
    public TagDefaultKind TagDefault { get; init; } = TagDefaultKind.Explicit;
    public List<ImportAst> Imports { get; } = new();
    public List<TypeAssignmentAst> TypeAssignments { get; } = new();
    public List<ValueAssignmentAst> ValueAssignments { get; } = new();
}

internal sealed class ImportAst : AstNode
{
    public string Module { get; init; } = "";
    public List<string> Types { get; } = new();
    public List<string> Values { get; } = new();
}

internal sealed class TypeAssignmentAst : AstNode
{
    public string Name { get; init; } = "";
    public TypeAst Type { get; init; } = null!;
}

internal sealed class ValueAssignmentAst : AstNode
{
    public string Name { get; init; } = "";
    public TypeAst Type { get; init; } = null!;
    public ValueAst Value { get; init; } = null!;
}

internal abstract class TypeAst : AstNode
{
    public ConstraintAst? Constraint { get; set; }
}

internal sealed class EnumeratedTypeAst : TypeAst
{
    public EnumeratedTypeAst(List<NamedNumberAst> values) => Values = values;

    public List<NamedNumberAst> Values { get; }
}

internal sealed class BuiltinTypeAst : TypeAst
{
    public BuiltinTypeAst(string name, List<NamedNumberAst>? namedNumbers = null)
    {
        Name = name;
        NamedNumbers = namedNumbers;
    }

    public string Name { get; }
    public List<NamedNumberAst>? NamedNumbers { get; }
}

internal sealed class BitStringTypeAst : TypeAst
{
    public BitStringTypeAst(List<NamedNumberAst>? namedBits = null) => NamedBits = namedBits;

    public List<NamedNumberAst>? NamedBits { get; }
}

internal sealed class StringTypeAst : TypeAst
{
    public StringTypeAst(string stringType) => StringType = stringType;

    public string StringType { get; }
}

internal sealed class TimeTypeAst : TypeAst
{
    public TimeTypeAst(string timeType) => TimeType = timeType;

    public string TimeType { get; }
}

internal sealed class AnyTypeAst : TypeAst
{
    public AnyTypeAst(string? definedBy = null) => DefinedBy = definedBy;

    public string? DefinedBy { get; }
}

internal sealed class NamedNumberAst : AstNode
{
    public string Name { get; init; } = "";
    public long Value { get; init; }
}

internal sealed class TypeReferenceAst : TypeAst
{
    public TypeReferenceAst(string name, string? module = null)
    {
        Name = name;
        Module = module;
    }

    public string Name { get; }
    public string? Module { get; }
}

internal sealed class SequenceTypeAst : TypeAst
{
    public List<FieldAst> Fields { get; } = new();
    public bool Extensible { get; set; }
}

internal sealed class SetTypeAst : TypeAst
{
    public List<FieldAst> Fields { get; } = new();
    public bool Extensible { get; set; }
}

internal sealed class SequenceOfTypeAst : TypeAst
{
    public SequenceOfTypeAst(TypeAst element) => Element = element;

    public TypeAst Element { get; }
}

internal sealed class SetOfTypeAst : TypeAst
{
    public SetOfTypeAst(TypeAst element) => Element = element;

    public TypeAst Element { get; }
}

internal sealed class ChoiceTypeAst : TypeAst
{
    public List<FieldAst> Fields { get; } = new();
    public bool Extensible { get; set; }
}

internal sealed class TaggedTypeAst : TypeAst
{
    public TaggedTypeAst(TagAst tag, TypeAst inner)
    {
        Tag = tag;
        Inner = inner;
    }

    public TagAst Tag { get; }
    public TypeAst Inner { get; }
}

internal sealed class TagAst : AstNode
{
    public string Class { get; init; } = "context";
    public int Number { get; init; }
    public string? Mode { get; init; }
}

internal sealed class FieldAst : AstNode
{
    public string Name { get; init; } = "";
    public TypeAst Type { get; init; } = null!;
    public bool Optional { get; set; }
    public ValueAst? Default { get; set; }
}

internal sealed class ConstraintAst : AstNode
{
    public BoundAst? SizeMin { get; set; }
    public BoundAst? SizeMax { get; set; }
    public bool HasSize { get; set; }
    public BoundAst? ValueMin { get; set; }
    public BoundAst? ValueMax { get; set; }
    public bool HasValue { get; set; }
    public string? Unsupported { get; set; }
}

internal sealed class BoundAst : AstNode
{
    public bool IsMin { get; init; }
    public bool IsMax { get; init; }
    public long? Number { get; init; }
    public string? Reference { get; init; }
}

internal abstract class ValueAst : AstNode
{
}

internal sealed class IntegerValueAst : ValueAst
{
    public IntegerValueAst(long value) => Value = value;

    public long Value { get; }
}

internal sealed class BooleanValueAst : ValueAst
{
    public BooleanValueAst(bool value) => Value = value;

    public bool Value { get; }
}

internal sealed class NullValueAst : ValueAst
{
}

internal sealed class OidValueAst : ValueAst
{
    public OidValueAst(List<OidArcAst> arcs) => Arcs = arcs;

    public List<OidArcAst> Arcs { get; }
}

internal sealed class OidArcAst : AstNode
{
    public string? Name { get; init; }
    public int? Number { get; init; }
}

internal sealed class CStringValueAst : ValueAst
{
    public CStringValueAst(string value) => Value = value;

    public string Value { get; }
}

internal sealed class BStringValueAst : ValueAst
{
    public BStringValueAst(string bits) => Bits = bits;

    public string Bits { get; }
}

internal sealed class HStringValueAst : ValueAst
{
    public HStringValueAst(string hex) => Hex = hex;

    public string Hex { get; }
}

internal sealed class ValueReferenceAst : ValueAst
{
    public ValueReferenceAst(string name, string? module = null)
    {
        Name = name;
        Module = module;
    }

    public string Name { get; }
    public string? Module { get; }
}

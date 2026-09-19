namespace Asn1Kit.Compiler;

internal enum TagDefaultKind
{
    Explicit,
    Implicit,
    Automatic
}

internal sealed class ModuleAst
{
    public string Name { get; init; } = "";
    public List<int>? Oid { get; init; }
    public string? Source { get; init; }
    public TagDefaultKind TagDefault { get; init; } = TagDefaultKind.Explicit;
    public List<ImportAst> Imports { get; } = new();
    public List<AssignmentAst> Assignments { get; } = new();
}

internal sealed class ImportAst
{
    public string Module { get; init; } = "";
    public List<string> Types { get; } = new();
}

internal sealed class AssignmentAst
{
    public string Name { get; init; } = "";
    public TypeAst Type { get; init; } = null!;
}

internal abstract class TypeAst
{
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

internal sealed class NamedNumberAst
{
    public string Name { get; init; } = "";
    public long Value { get; init; }
}

internal sealed class TypeReferenceAst : TypeAst
{
    public TypeReferenceAst(string name) => Name = name;

    public string Name { get; }
}

internal sealed class SequenceTypeAst : TypeAst
{
    public List<FieldAst> Fields { get; } = new();
}

internal sealed class SequenceOfTypeAst : TypeAst
{
    public SequenceOfTypeAst(TypeAst element) => Element = element;

    public TypeAst Element { get; }
}

internal sealed class ChoiceTypeAst : TypeAst
{
    public List<FieldAst> Fields { get; } = new();
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

internal sealed class TagAst
{
    public string Class { get; init; } = "context";
    public int Number { get; init; }
    public string? Mode { get; init; }
}

internal sealed class FieldAst
{
    public string Name { get; init; } = "";
    public TypeAst Type { get; init; } = null!;
    public bool Optional { get; init; }
}

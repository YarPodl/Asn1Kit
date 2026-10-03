namespace Asn1Kit.Compiler;

internal sealed class InstanceOfTypeAst : TypeAst
{
    public TypeAst Class { get; init; } = null!;
}

internal sealed class FormalParameterAst : AstNode
{
    public string Name { get; init; } = "";
    public IReadOnlyList<Token>? Governor { get; init; }
}

internal sealed class InformationClassAst : TypeAst
{
    public List<InformationFieldAst> Fields { get; } = new();
    public List<SyntaxElementAst>? Syntax { get; set; }
}

internal sealed class InformationFieldAst : AstNode
{
    public string Name { get; init; } = "";
    public TypeAst? Governor { get; init; }
    public bool Unique { get; set; }
    public bool Optional { get; set; }
    public ValueAst? Default { get; set; }
}

internal sealed class SyntaxElementAst : AstNode
{
    public string? Literal { get; init; }
    public string? Field { get; init; }
    public List<SyntaxElementAst>? OptionalGroup { get; init; }
}

internal sealed class ObjectFieldTypeAst : TypeAst
{
    public bool SelectorOutermost { get; set; }
    public string Owner { get; init; } = "";
    public string? Module { get; init; }
    public List<string> Fields { get; } = new();
    public IReadOnlyList<Token>? Table { get; set; }
    public Asn1Kit.Ir.IrOpenTypeSelector? Selector { get; set; }
}

internal sealed class RawValueAst : ValueAst
{
    public IReadOnlyList<Token> Tokens { get; init; } = Array.Empty<Token>();
}

internal sealed class ObjectFieldValueAst : ValueAst
{
    public string Owner { get; init; } = "";
    public string? Module { get; init; }
    public List<string> Fields { get; } = new();
}

internal sealed class StructuredValueAst : ValueAst
{
    public Dictionary<string, ValueAst> Fields { get; } = new(StringComparer.Ordinal);
}

internal sealed class CollectionValueAst : ValueAst
{
    public List<ValueAst> Items { get; } = new();
}

internal sealed class ChoiceValueAst : ValueAst
{
    public string Alternative { get; init; } = "";
    public ValueAst Value { get; init; } = null!;
}

internal sealed class TypedValueAst : ValueAst
{
    public TypeAst Type { get; init; } = null!;
    public ValueAst Value { get; init; } = null!;
}

internal sealed class ContainingTypeAst : TypeAst
{
    public TypeAst Outer { get; init; } = null!;
    public TypeAst Inner { get; init; } = null!;
}

internal sealed class OpenTypeBindingAst
{
    public string? Module { get; init; }
    public string Key { get; init; } = "";
    public string? Name { get; init; }
    public TypeAst Type { get; init; } = null!;
}

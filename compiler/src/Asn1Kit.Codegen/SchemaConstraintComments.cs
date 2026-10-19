using Asn1Kit.Ir;

namespace Asn1Kit.Codegen;

/// <summary>
/// Plain-English schema-constraint and object-set comments shared by C# and C++ backends.
/// Descriptive only — runtime does not enforce these metadata.
/// </summary>
public static class SchemaConstraintComments
{
    public static string? TryFormatConstraint(IrConstraint? constraint)
    {
        var facts = ConstraintFacts(constraint);
        if (facts.Count == 0)
        {
            return null;
        }

        return string.Join(". ", facts) + ". Not enforced by runtime.";
    }

    public static string? TryFormatObjectSet(string? table, bool? tableExtensible)
    {
        var fact = ObjectSetFact(table, tableExtensible);
        if (fact is null)
        {
            return null;
        }

        return fact + ". Membership not enforced by runtime.";
    }

    /// <summary>
    /// Collects constraint and object-set comment text for a field/type expression.
    /// Follows collapsible typedef aliases and CONTAINING payloads; does not enter named SEQUENCE/SET/CHOICE bodies.
    /// </summary>
    public static string? CollectForTypeExpr(TypeExpr type, Func<RefType, IrTypeDef?>? resolve = null)
    {
        var constraintFacts = new List<string>();
        var objectSetFacts = new List<string>();
        Visit(type, resolve, constraintFacts, objectSetFacts, depth: 0);
        return Combine(constraintFacts, objectSetFacts);
    }

    public static string? Combine(IReadOnlyList<string> constraintFacts, IReadOnlyList<string> objectSetFacts)
    {
        if (constraintFacts.Count == 0 && objectSetFacts.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (constraintFacts.Count > 0)
        {
            parts.Add(string.Join(". ", constraintFacts) + ". Not enforced by runtime.");
        }

        if (objectSetFacts.Count > 0)
        {
            parts.Add(string.Join(". ", objectSetFacts) + ". Membership not enforced by runtime.");
        }

        return string.Join(" ", parts);
    }

    private static void Visit(
        TypeExpr type,
        Func<RefType, IrTypeDef?>? resolve,
        List<string> constraintFacts,
        List<string> objectSetFacts,
        int depth)
    {
        if (depth > 32)
        {
            return;
        }

        AppendUnique(constraintFacts, ConstraintFacts(type.Constraint));

        switch (type)
        {
            case AnyType any:
                AppendUnique(objectSetFacts, ObjectSetFact(any.Table, any.TableExtensible));
                break;
            case RefType reference:
                if (reference.OpenTypes is { Count: > 0 })
                {
                    foreach (var use in reference.OpenTypes)
                    {
                        AppendUnique(objectSetFacts, ObjectSetFact(use.Table, use.TableExtensible));
                    }
                }

                if (resolve?.Invoke(reference) is { } def && IsCollapsibleAliasRhs(def.Type))
                {
                    Visit(def.Type, resolve, constraintFacts, objectSetFacts, depth + 1);
                }

                break;
            case OctetStringType { Containing: { } containing }:
                Visit(containing, resolve, constraintFacts, objectSetFacts, depth + 1);
                break;
            case BitStringType { Containing: { } containing }:
                Visit(containing, resolve, constraintFacts, objectSetFacts, depth + 1);
                break;
        }
    }

    /// <summary>
    /// RHS types that backends collapse to the underlying shape (no dedicated named shell).
    /// Matches C# <c>IsCollapsibleAlias</c> / C++ alias unwrap policy.
    /// </summary>
    internal static bool IsCollapsibleAliasRhs(TypeExpr type) => type switch
    {
        SequenceType or SetType => false,
        ChoiceType choice when choice.Components.Count > 1 => false,
        BitStringType bit when bit.NamedBits is { Count: > 0 } => false,
        EnumeratedType => false,
        IntegerType integer when integer.NamedValues is { Count: > 0 } => false,
        _ => true
    };

    private static List<string> ConstraintFacts(IrConstraint? constraint)
    {
        var facts = new List<string>();
        if (constraint is null)
        {
            return facts;
        }

        if (constraint.Size is { } size)
        {
            facts.Add($"ASN.1 SIZE ({FormatBound(size)})");
        }

        if (constraint.Value is { } value)
        {
            facts.Add($"ASN.1 value range ({FormatBound(value)})");
        }

        if (!string.IsNullOrEmpty(constraint.Unsupported))
        {
            facts.Add($"ASN.1 constraint unsupported: {constraint.Unsupported}");
        }

        return facts;
    }

    private static string? ObjectSetFact(string? table, bool? tableExtensible)
    {
        if (tableExtensible is null)
        {
            return null;
        }

        var openness = tableExtensible.Value ? "extensible" : "closed";
        return string.IsNullOrEmpty(table)
            ? $"ASN.1 object set ({openness})"
            : $"ASN.1 object set {table} ({openness})";
    }

    private static string FormatBound(IrBound bound) =>
        bound.Max is null
            ? $"{bound.Min}..MAX"
            : bound.Max.Value == bound.Min
                ? bound.Min.ToString()
                : $"{bound.Min}..{bound.Max.Value}";

    private static void AppendUnique(List<string> target, IEnumerable<string> facts)
    {
        foreach (var fact in facts)
        {
            AppendUnique(target, fact);
        }
    }

    private static void AppendUnique(List<string> target, string? fact)
    {
        if (fact is null || target.Contains(fact, StringComparer.Ordinal))
        {
            return;
        }

        target.Add(fact);
    }
}

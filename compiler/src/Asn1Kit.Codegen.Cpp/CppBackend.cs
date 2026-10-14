using System.Text;
using Asn1Kit.Codegen;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.Cpp;

public sealed partial class CppBackend : ILanguageBackend
{
    public string LanguageId => "cpp";

    private static string ModuleNamespace(IrModule module) =>
        IrOptions.CppNamespace(module.Options) ?? CppNames.SanitizeTypeName(module.Name);

    private static string OidCatalogName(IrModule module) =>
        CppNames.SanitizeTypeName(module.Name) + "Oids";

    private static List<(string Name, string Dotted)> CollectOidValues(IrModule module)
    {
        var result = new List<(string Name, string Dotted)>();
        foreach (var definition in module.Values)
        {
            if (!IrOptions.ShouldGenerate(definition.Options))
            {
                continue;
            }

            if (definition.Value is IrOidValue oid)
            {
                result.Add((CppNames.SanitizeTypeName(definition.Name), oid.Value));
            }
        }

        return result;
    }

    private static void EmitOidCatalog(
        StringBuilder sb,
        string catalogName,
        IReadOnlyList<(string Name, string Dotted)> values)
    {
        sb.AppendLine($"struct {catalogName} {{");
        foreach (var (name, dotted) in values)
        {
            sb.AppendLine(
                $"    static constexpr auto {name} = asn1kit::oid_literal<\"{CppNames.EscapeString(dotted)}\">();");
        }

        sb.AppendLine("};");
    }

    /// <summary>
    /// Emit nested named types in layout-dep order (CHOICE/OF nested structs before owners).
    /// </summary>
    private List<(string Name, TypeExpr Type)> OrderNestedForEmit(
        IrDocument document,
        IrModule module,
        IReadOnlyList<(string Name, TypeExpr Type)> nested)
    {
        var items = nested.ToList();
        var keyOf = new Dictionary<(string Module, string Name), int>();
        for (var index = 0; index < items.Count; index++)
        {
            keyOf[(module.Name, items[index].Name)] = index;
        }

        var deps = items.Select(_ => new HashSet<int>()).ToArray();
        for (var index = 0; index < items.Count; index++)
        {
            CollectNamedTypeDeps(
                document, module, items[index].Type, items[index].Name, keyOf, deps[index]);
        }

        var remaining = new HashSet<int>(Enumerable.Range(0, items.Count));
        var ordered = new List<(string Name, TypeExpr Type)>();
        while (remaining.Count > 0)
        {
            var next = remaining
                .Where(i => deps[i].All(d => !remaining.Contains(d)))
                .OrderBy(i => items[i].Name, StringComparer.Ordinal)
                .DefaultIfEmpty(-1)
                .First();
            if (next < 0)
            {
                next = remaining
                    .OrderByDescending(i => remaining.Count(j => deps[j].Contains(i)))
                    .ThenBy(i => deps[i].Count(d => remaining.Contains(d)))
                    .ThenBy(i => items[i].Name, StringComparer.Ordinal)
                    .First();
            }

            remaining.Remove(next);
            ordered.Add(items[next]);
        }

        return ordered;
    }

    private void CollectNested(
        IrDocument document,
        IrModule module,
        string owner,
        TypeExpr type,
        Queue<(string Name, TypeExpr Type)> queue)
    {
        switch (type)
        {
            case OctetStringType { Containing: { } content }:
                OfferNested(document, module, owner + "_Content", content, queue);
                break;
            case BitStringType { Containing: { } bitContent }:
                OfferNested(document, module, owner + "_Content", bitContent, queue);
                break;
            case SequenceType sequence:
                foreach (var component in sequence.Components)
                {
                    OfferNested(
                        document,
                        module,
                        owner + "_" + CppNames.SanitizeTypeName(component.Name),
                        component.Type,
                        queue);
                }

                break;
            case SetType set:
                foreach (var component in set.Components)
                {
                    OfferNested(
                        document,
                        module,
                        owner + "_" + CppNames.SanitizeTypeName(component.Name),
                        component.Type,
                        queue);
                }

                break;
            case ChoiceType choice:
                if (IsSingleAlternativeChoice(choice))
                {
                    OfferNested(document, module, owner, choice.Components[0].Type, queue);
                }
                else
                {
                    foreach (var component in choice.Components)
                    {
                        OfferNested(
                            document,
                            module,
                            owner + "_" + CppNames.SanitizeTypeName(component.Name),
                            component.Type,
                            queue);
                    }
                }

                break;
            case SequenceOfType sequenceOf:
                OfferNested(document, module, owner + "_Item", sequenceOf.Element, queue);
                break;
            case SetOfType setOf:
                OfferNested(document, module, owner + "_Item", setOf.Element, queue);
                break;
        }
    }

    private void OfferNested(
        IrDocument document,
        IrModule module,
        string hint,
        TypeExpr type,
        Queue<(string Name, TypeExpr Type)> queue)
    {
        if (NeedsNamedType(type) || IsEnumerated(type) || IsNamedInteger(type) || IsNamedBitString(type))
        {
            queue.Enqueue((hint, type));
            if (NeedsNamedType(type))
            {
                CollectNested(document, module, hint, type, queue);
            }
        }
        else if (type is RefType)
        {
            return;
        }
        else if (ContainedType(type) is { } content)
        {
            OfferNested(document, module, hint + "_Content", content, queue);
        }
        else if (type is SequenceOfType or SetOfType or SequenceType or SetType or ChoiceType)
        {
            CollectNested(document, module, hint, type, queue);
        }
    }

    private void EmitType(StringBuilder sb, IrDocument document, IrModule module, string typeName, TypeExpr type,
        bool deferChoiceCodecs = false)
    {
        EnsureBackendSupport(document, module, type);
        switch (type)
        {
            case SequenceType sequence:
                EmitSequence(sb, document, module, typeName, sequence);
                break;
            case SetType set:
                EmitSet(sb, document, module, typeName, set);
                break;
            case ChoiceType choice:
                EmitChoice(sb, document, module, typeName, choice, deferChoiceCodecs);
                break;
            case BitStringType bitString when IsNamedBitString(bitString):
                EmitNamedBitString(sb, typeName, bitString);
                break;
            case EnumeratedType enumerated:
                EmitEnumerated(sb, typeName, enumerated);
                break;
            case IntegerType integer when IsNamedInteger(integer):
                EmitNamedIntegerConstants(sb, typeName, integer);
                break;
            case SequenceOfType:
            case SetOfType:
                throw new NotSupportedException(
                    $"C++ backend does not emit a named type for '{type.Kind}'; use std::vector via field encode/decode.");
            default:
                throw new InvalidOperationException(
                    $"C++ backend refused to emit collapsible alias '{typeName}' (kind '{type.Kind}').");
        }
    }
}

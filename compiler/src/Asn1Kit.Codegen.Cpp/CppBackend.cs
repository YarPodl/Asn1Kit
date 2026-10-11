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
    /// Emit nested named types so OF item structs appear before owners that embed them by value.
    /// </summary>
    private List<(string Name, TypeExpr Type)> OrderNestedForEmit(
        IrDocument document,
        IrModule module,
        IReadOnlyList<(string Name, TypeExpr Type)> nested)
    {
        var byName = nested.ToDictionary(static item => item.Name, StringComparer.Ordinal);
        var deps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (name, _) in nested)
            deps[name] = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, type) in nested)
        {
            if (OpenContainerFields(type) is not { } fields) continue;
            foreach (var field in fields)
            {
                var fieldType = UnwrapAliases(document, module, field.Type);
                if (fieldType is RefType reference)
                {
                    var found = FindWithModule(document, module, reference);
                    if (found is null || found.Value.Module != module) continue;
                    var dep = IrOptions.CppTypeName(found.Value.Def.Options)
                        ?? CppNames.SanitizeTypeName(found.Value.Def.Name);
                    if (byName.ContainsKey(dep) && dep != name)
                        deps[name].Add(dep);
                    continue;
                }

                if (fieldType is SequenceOfType or SetOfType)
                {
                    // CollectNested(owner) → OfferNested(owner_Field, OF) → owner_Field_Item.
                    var itemName = name + "_" + CppNames.SanitizeTypeName(field.Name) + "_Item";
                    if (byName.ContainsKey(itemName))
                        deps[name].Add(itemName);
                }
            }
        }

        var remaining = new HashSet<string>(byName.Keys, StringComparer.Ordinal);
        var ordered = new List<(string Name, TypeExpr Type)>();
        while (remaining.Count > 0)
        {
            var next = remaining
                .Where(n => deps[n].All(d => !remaining.Contains(d)))
                .OrderBy(static n => n, StringComparer.Ordinal)
                .FirstOrDefault();
            next ??= remaining.OrderBy(static n => n, StringComparer.Ordinal).First();
            remaining.Remove(next);
            ordered.Add(byName[next]);
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

    private void EmitType(StringBuilder sb, IrDocument document, IrModule module, string typeName, TypeExpr type)
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
                EmitChoice(sb, document, module, typeName, choice);
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

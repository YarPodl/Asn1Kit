using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

/// <summary>
/// Replaces inline SEQUENCE/SET/multi-CHOICE that match an existing typedef with a ref,
/// moving IOC tables onto <c>ref.openTypes</c>. Fingerprint is wire-shape plus
/// <c>options</c>; IOC place-specific fields (<c>selector</c>/<c>bindings</c>/<c>table</c>/<c>openTypes</c>)
/// are ignored for matching.
/// </summary>
internal static class InlineCompositeDeduper
{
    private static readonly Regex TableConstraintNoise = new(
        @"table\(\s*\{[^}]*\}\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly record struct Candidate(IrModule Module, IrTypeDef Definition, string Fingerprint);

    public static void Dedup(IrDocument document)
    {
        bool changed;
        do
        {
            changed = false;
            var catalog = BuildCatalog(document);
            foreach (var module in document.Modules)
            {
                foreach (var definition in module.Types)
                {
                    if (Rewrite(document, module, definition.Type, definition.Name, catalog))
                        changed = true;
                }

                foreach (var definition in module.Values)
                {
                    if (Rewrite(document, module, definition.Type, definingName: null, catalog))
                        changed = true;
                }
            }
        } while (changed);
    }

    private static List<Candidate> BuildCatalog(IrDocument document)
    {
        var catalog = new List<Candidate>();
        foreach (var module in document.Modules)
        {
            foreach (var definition in module.Types)
            {
                if (!IsComposite(definition.Type)) continue;
                catalog.Add(new Candidate(module, definition, Fingerprint(definition.Type)));
            }
        }

        return catalog;
    }

    private static bool Rewrite(
        IrDocument document,
        IrModule owner,
        TypeExpr type,
        string? definingName,
        IReadOnlyList<Candidate> catalog)
    {
        return type switch
        {
            SequenceType sequence => RewriteFields(document, owner, sequence.Components, definingName, catalog),
            SetType set => RewriteFields(document, owner, set.Components, definingName, catalog),
            ChoiceType choice => RewriteFields(document, owner, choice.Components, definingName, catalog),
            SequenceOfType sequenceOf => RewriteSlot(
                document, owner, () => sequenceOf.Element, value => sequenceOf.Element = value, definingName, catalog),
            SetOfType setOf => RewriteSlot(
                document, owner, () => setOf.Element, value => setOf.Element = value, definingName, catalog),
            OctetStringType { Containing: { } } octets => RewriteSlot(
                document, owner, () => octets.Containing!, value => octets.Containing = value, definingName, catalog),
            BitStringType { Containing: { } } bits => RewriteSlot(
                document, owner, () => bits.Containing!, value => bits.Containing = value, definingName, catalog),
            AnyType { Bindings: { } bindings } => RewriteBindings(document, owner, bindings, definingName, catalog),
            RefType { OpenTypes: { } uses } => RewriteOpenUses(document, owner, uses, definingName, catalog),
            _ => false
        };
    }

    private static bool RewriteFields(
        IrDocument document,
        IrModule owner,
        IList<IrComponent> fields,
        string? definingName,
        IReadOnlyList<Candidate> catalog)
    {
        var changed = false;
        foreach (var field in fields)
        {
            changed |= Rewrite(document, owner, field.Type, definingName, catalog);
            if (!IsComposite(field.Type)) continue;
            if (TryCollapse(owner, field.Type, definingName, catalog) is not { } collapsed) continue;
            field.Type = collapsed;
            changed = true;
        }

        return changed;
    }

    private static bool RewriteSlot(
        IrDocument document,
        IrModule owner,
        Func<TypeExpr> get,
        Action<TypeExpr> set,
        string? definingName,
        IReadOnlyList<Candidate> catalog)
    {
        var current = get();
        var changed = Rewrite(document, owner, current, definingName, catalog);
        current = get();
        if (!IsComposite(current)) return changed;
        if (TryCollapse(owner, current, definingName, catalog) is not { } collapsed) return changed;
        set(collapsed);
        return true;
    }

    private static bool RewriteBindings(
        IrDocument document,
        IrModule owner,
        IList<IrOpenTypeBinding> bindings,
        string? definingName,
        IReadOnlyList<Candidate> catalog)
    {
        var changed = false;
        foreach (var binding in bindings)
        {
            changed |= Rewrite(document, owner, binding.Type, definingName, catalog);
            if (!IsComposite(binding.Type)) continue;
            if (TryCollapse(owner, binding.Type, definingName, catalog) is not { } collapsed) continue;
            binding.Type = collapsed;
            changed = true;
        }

        return changed;
    }

    private static bool RewriteOpenUses(
        IrDocument document,
        IrModule owner,
        IList<IrOpenTypeUse> uses,
        string? definingName,
        IReadOnlyList<Candidate> catalog)
    {
        var changed = false;
        foreach (var use in uses)
            changed |= RewriteBindings(document, owner, use.Bindings, definingName, catalog);
        return changed;
    }

    private static RefType? TryCollapse(
        IrModule owner,
        TypeExpr inline,
        string? definingName,
        IReadOnlyList<Candidate> catalog)
    {
        var fingerprint = Fingerprint(inline);
        var matches = catalog
            .Where(c => c.Fingerprint == fingerprint)
            .Where(c => definingName is null ||
                        !(c.Module.Name == owner.Name && c.Definition.Name == definingName))
            .Where(c => HasCompatibleOpenSelectors(c.Definition.Type, inline))
            .ToList();
        if (matches.Count == 0) return null;

        var chosen = matches
            .OrderBy(c => Preference(owner, c), Comparer<int>.Default)
            .ThenBy(c => c.Module.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Definition.Name, StringComparer.Ordinal)
            .First();

        var tables = CollectTables(inline);
        return new RefType
        {
            Name = chosen.Definition.Name,
            Module = chosen.Module.Name == owner.Name ? null : chosen.Module.Name,
            OpenTypes = tables.Count > 0 ? tables : null
        };
    }

    private static int Preference(IrModule owner, Candidate candidate)
    {
        if (candidate.Module.Name == owner.Name) return 0;
        if (owner.Imports.Any(i =>
                i.Module == candidate.Module.Name &&
                i.Types.Contains(candidate.Definition.Name, StringComparer.Ordinal)))
            return 1;
        return 2;
    }

    private static bool HasCompatibleOpenSelectors(TypeExpr typedefBody, TypeExpr inline)
    {
        var tables = CollectTables(inline);
        if (tables.Count == 0) return true;
        foreach (var use in tables)
        {
            if (use.Path.Count == 0) return false;
            if (FindAnyAtPath(typedefBody, use.Path) is not { Selector: { Levels: 0 } })
                return false;
        }

        return true;
    }

    private static AnyType? FindAnyAtPath(TypeExpr type, IReadOnlyList<string> path)
    {
        var current = type;
        for (var index = 0; index < path.Count; index++)
        {
            var part = path[index];
            switch (part)
            {
                case "[]":
                    current = current switch
                    {
                        SequenceOfType sequenceOf => sequenceOf.Element,
                        SetOfType setOf => setOf.Element,
                        _ => null
                    };
                    break;
                case "containing":
                    current = ContainedType(current);
                    break;
                default:
                    var fields = current switch
                    {
                        SequenceType sequence => sequence.Components,
                        SetType set => set.Components,
                        ChoiceType choice => choice.Components,
                        _ => null
                    };
                    if (fields is null) return null;
                    var field = fields.FirstOrDefault(f => f.Name == part);
                    if (field is null) return null;
                    current = field.Type;
                    break;
            }

            if (current is null) return null;
        }

        return current as AnyType;
    }

    private static TypeExpr? ContainedType(TypeExpr type) => type switch
    {
        OctetStringType { Containing: { } containing } => containing,
        BitStringType { Containing: { } containing } => containing,
        _ => null
    };

    private static bool IsComposite(TypeExpr type) => type switch
    {
        SequenceType => true,
        SetType => true,
        ChoiceType { Components.Count: > 1 } => true,
        _ => false
    };

    private static string Fingerprint(TypeExpr type)
    {
        var node = JsonSerializer.SerializeToNode(type, IrSerializer.JsonOptions)!;
        Normalize(node);
        return node.ToJsonString();
    }

    private static void Normalize(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("bindings");
            obj.Remove("table");
            obj.Remove("tableExtensible");
            obj.Remove("openTypes");
            obj.Remove("selector");
            if (obj.TryGetPropertyValue("unsupported", out var unsupported) &&
                unsupported is JsonValue value &&
                value.TryGetValue<string>(out var text) &&
                text is not null)
            {
                obj["unsupported"] = TableConstraintNoise.Replace(text, "table({})");
            }

            foreach (var property in obj.ToArray())
            {
                if (property.Value is not null) Normalize(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null) Normalize(item);
            }
        }
    }

    private static List<IrOpenTypeUse> CollectTables(TypeExpr type)
    {
        var result = new List<IrOpenTypeUse>();
        Visit(type, new List<string>());
        return result;

        void Visit(TypeExpr expression, List<string> path)
        {
            switch (expression)
            {
                case AnyType any when any.Bindings is not null || any.TableExtensible.HasValue:
                    result.Add(new IrOpenTypeUse
                    {
                        Path = path.ToList(),
                        Bindings = CloneBindings(any.Bindings ?? new()),
                        Table = any.Table,
                        TableExtensible = any.TableExtensible
                    });
                    break;
                case RefType reference when reference.OpenTypes is not null:
                    foreach (var use in reference.OpenTypes)
                    {
                        result.Add(new IrOpenTypeUse
                        {
                            Path = path.Concat(use.Path).ToList(),
                            Bindings = CloneBindings(use.Bindings),
                            Table = use.Table,
                            TableExtensible = use.TableExtensible
                        });
                    }

                    break;
                case SequenceType sequence:
                    foreach (var field in sequence.Components)
                        Visit(field.Type, path.Append(field.Name).ToList());
                    break;
                case SetType set:
                    foreach (var field in set.Components)
                        Visit(field.Type, path.Append(field.Name).ToList());
                    break;
                case ChoiceType choice:
                    foreach (var field in choice.Components)
                        Visit(field.Type, path.Append(field.Name).ToList());
                    break;
                case SequenceOfType of:
                    Visit(of.Element, path.Append("[]").ToList());
                    break;
                case SetOfType of:
                    Visit(of.Element, path.Append("[]").ToList());
                    break;
                case OctetStringType { Containing: { } containing }:
                    Visit(containing, path.Append("containing").ToList());
                    break;
                case BitStringType { Containing: { } containing }:
                    Visit(containing, path.Append("containing").ToList());
                    break;
            }
        }
    }

    private static List<IrOpenTypeBinding> CloneBindings(IReadOnlyList<IrOpenTypeBinding> bindings) =>
        bindings.Select(binding => JsonSerializer.Deserialize<IrOpenTypeBinding>(
            JsonSerializer.Serialize(binding, IrSerializer.JsonOptions), IrSerializer.JsonOptions)!).ToList();
}

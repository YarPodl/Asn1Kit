using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

/// <summary>Moves IOC tables from concrete template bodies to their uses and removes redundant bodies.</summary>
internal static class SpecializationCompactor
{
    private static readonly Regex SpecializedName = new("^(?<name>.+)-[0-9A-F]{16}$", RegexOptions.Compiled);

    public static void Compact(IrDocument document)
    {
        // A parent specialization can become equivalent only after its nested specializations
        // have been folded, so repeat until no structural family changes.
        bool changed;
        do
        {
            changed = false;
            foreach (var module in document.Modules)
            {
                var families = module.Types.Where(t => SpecializedName.IsMatch(t.Name))
                    .GroupBy(t => SpecializedName.Match(t.Name).Groups["name"].Value, StringComparer.Ordinal).ToArray();
                foreach (var family in families)
                {
                    var groups = family.GroupBy(t => Fingerprint(t.Type), StringComparer.Ordinal).ToArray();
                    foreach (var group in groups.Where(g => g.Count() > 1))
                    {
                        var members = group.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
                        if (members.Any(t => CollectTables(t.Type).Any(use => use.Path.Count == 0))) continue;
                        var canonical = members[0];
                        var targetName = groups.Length == 1 && module.Types.All(t => t.Name != family.Key)
                            ? family.Key : canonical.Name;
                        var overlays = members.ToDictionary(t => t.Name, t => CollectTables(t.Type), StringComparer.Ordinal);
                        foreach (var member in members)
                            RewriteReferences(document, module.Name, member.Name, targetName, overlays[member.Name]);
                        StripTables(canonical.Type);
                        canonical.Name = targetName;
                        ResetGeneratedName(canonical);
                        foreach (var duplicate in members.Skip(1)) module.Types.Remove(duplicate);
                        changed = true;
                    }
                }
            }
        } while (changed);

        // A structurally distinct specialization is a real type. Prefer its ASN.1 typedef
        // as its public name, and use a readable deterministic name for anonymous uses.
        foreach (var module in document.Modules)
        {
            foreach (var definition in module.Types.Where(t => SpecializedName.IsMatch(t.Name))
                .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray())
            {
                var baseName = SpecializedName.Match(definition.Name).Groups["name"].Value;
                var origin = new IrSpecializationOrigin { Module = module.Name, Name = baseName };
                var alias = document.Modules.SelectMany(m => m.Types.Select(t => (Module: m, Type: t)))
                    .Where(x => x.Type.Type is RefType r && r.Name == definition.Name &&
                        (r.Module ?? x.Module.Name) == module.Name && !SpecializedName.IsMatch(x.Type.Name))
                    .OrderBy(x => x.Module.Name == module.Name ? 0 : 1)
                    .ThenBy(x => x.Type.Name, StringComparer.Ordinal).FirstOrDefault();
                if (alias.Type is not null)
                {
                    var oldName = definition.Name;
                    alias.Type.Type = definition.Type;
                    alias.Type.Specialization = origin;
                    module.Types.Remove(definition);
                    RewriteReferences(document, module.Name, oldName, alias.Type.Name, Array.Empty<IrOpenTypeUse>(), alias.Module.Name);
                    continue;
                }

                var candidate = module.Types.All(t => t == definition || t.Name != baseName)
                    ? baseName : baseName + "Variant";
                var suffix = 2;
                while (module.Types.Any(t => t != definition && t.Name == candidate))
                    candidate = baseName + "Variant" + suffix++;
                RewriteReferences(document, module.Name, definition.Name, candidate, Array.Empty<IrOpenTypeUse>());
                definition.Name = candidate;
                definition.Specialization = origin;
                ResetGeneratedName(definition);
            }
        }
    }

    private static void ResetGeneratedName(IrTypeDef definition)
    {
        if (definition.Options?["csharp"] is not JsonObject csharp) return;
        csharp.Remove("typeName");
        if (csharp.Count == 0) definition.Options.Remove("csharp");
        if (definition.Options.Count == 0) definition.Options = null;
    }

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
            foreach (var value in obj.ToArray())
                if (value.Value is not null) Normalize(value.Value);
        }
        else if (node is JsonArray array)
            foreach (var value in array)
                if (value is not null) Normalize(value);
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
                        Path = path.ToList(), Bindings = any.Bindings ?? new(), Table = any.Table,
                        TableExtensible = any.TableExtensible
                    });
                    break;
                case RefType reference when reference.OpenTypes is not null:
                    foreach (var use in reference.OpenTypes)
                        result.Add(new IrOpenTypeUse
                        {
                            Path = path.Concat(use.Path).ToList(), Bindings = use.Bindings, Table = use.Table,
                            TableExtensible = use.TableExtensible
                        });
                    break;
                case SequenceType sequence:
                    foreach (var field in sequence.Components) Visit(field.Type, path.Append(field.Name).ToList());
                    break;
                case SetType set:
                    foreach (var field in set.Components) Visit(field.Type, path.Append(field.Name).ToList());
                    break;
                case ChoiceType choice:
                    foreach (var field in choice.Components) Visit(field.Type, path.Append(field.Name).ToList());
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

    private static void StripTables(TypeExpr type) => Walk(type, _ => { }, any =>
    {
        any.Bindings = null;
        any.Table = null;
        any.TableExtensible = null;
    }, stripReferences: true);

    private static void RewriteReferences(IrDocument document, string moduleName, string oldName, string newName,
        IReadOnlyList<IrOpenTypeUse> uses, string? targetModule = null)
    {
        foreach (var module in document.Modules)
        {
            void RewriteNameOnly(RefType reference)
            {
                if (reference.Name != oldName || (reference.Module ?? module.Name) != moduleName) return;
                reference.Name = newName;
                if (targetModule is not null) reference.Module = targetModule;
            }
            void Rewrite(RefType reference)
            {
                if (reference.Name != oldName || (reference.Module ?? module.Name) != moduleName) return;
                RewriteNameOnly(reference);
                if (uses.Count > 0)
                {
                    reference.OpenTypes ??= new();
                    foreach (var use in uses)
                    {
                        var copy = JsonSerializer.Deserialize<IrOpenTypeUse>(
                            JsonSerializer.Serialize(use, IrSerializer.JsonOptions), IrSerializer.JsonOptions)!;
                        foreach (var binding in copy.Bindings)
                            Walk(binding.Type, RewriteNameOnly, _ => { });
                        reference.OpenTypes.Add(copy);
                    }
                }
            }
            foreach (var definition in module.Types) Walk(definition.Type, Rewrite, _ => { });
            foreach (var definition in module.Values) Walk(definition.Type, Rewrite, _ => { });
        }
    }

    private static void Walk(TypeExpr type, Action<RefType> onReference, Action<AnyType> onAny, bool stripReferences = false)
    {
        switch (type)
        {
            case RefType reference:
                var nestedUses = reference.OpenTypes?.ToArray();
                onReference(reference);
                if (nestedUses is not null)
                    foreach (var use in nestedUses)
                        foreach (var binding in use.Bindings)
                            Walk(binding.Type, onReference, onAny, stripReferences);
                if (stripReferences) reference.OpenTypes = null;
                break;
            case AnyType any:
                onAny(any);
                if (any.Bindings is not null)
                    foreach (var binding in any.Bindings) Walk(binding.Type, onReference, onAny, stripReferences);
                break;
            case SequenceType sequence:
                foreach (var field in sequence.Components) Walk(field.Type, onReference, onAny, stripReferences);
                break;
            case SetType set:
                foreach (var field in set.Components) Walk(field.Type, onReference, onAny, stripReferences);
                break;
            case ChoiceType choice:
                foreach (var field in choice.Components) Walk(field.Type, onReference, onAny, stripReferences);
                break;
            case SequenceOfType of:
                Walk(of.Element, onReference, onAny, stripReferences);
                break;
            case SetOfType of:
                Walk(of.Element, onReference, onAny, stripReferences);
                break;
            case OctetStringType { Containing: { } containing }:
                Walk(containing, onReference, onAny, stripReferences);
                break;
            case BitStringType { Containing: { } containing }:
                Walk(containing, onReference, onAny, stripReferences);
                break;
        }
    }
}

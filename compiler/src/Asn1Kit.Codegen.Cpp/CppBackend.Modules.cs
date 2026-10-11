using System.IO;
using System.Text;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.Cpp;

public sealed partial class CppBackend
{
    private sealed record ModulePlan(
        IrModule Module,
        List<(string Name, TypeExpr Type)> Nested,
        OpenPlan Open,
        List<(string Name, string Dotted)> Oids,
        HashSet<string> EmittedNames);

    public IReadOnlyList<GeneratedFile> Generate(IrDocument document)
    {
        OpenTypeBindings.NormalizeLegacy(document);
        var modules = document.Modules
            .Where(module => module.Types.Any(static type => IrOptions.ShouldGenerate(type.Options)))
            .ToList();
        var plans = modules.Select(module => PlanModule(document, module)).ToList();
        var peerMap = plans.ToDictionary(
            static p => p.Module.Name,
            p => CollectPeerModuleNames(document, p),
            StringComparer.Ordinal);

        var files = new List<GeneratedFile>(plans.Count + 1);
        foreach (var plan in plans)
        {
            files.Add(EmitModuleFile(document, plan, peerMap[plan.Module.Name]));
        }

        if (plans.Count > 1 && ModuleDependencyGraphHasCycle(peerMap))
        {
            files.Add(EmitAmalgamatedFile(document, plans, "all.hpp"));
            // Certificate round-trip: type-level closure from Certificate (not whole peer modules).
            var certificateTypes = SelectTypeClosure(
                document, plans, "PKIX1Explicit-2009", "Certificate");
            if (certificateTypes.Count > 0)
            {
                files.Add(EmitAmalgamatedFile(
                    document, plans, "certificate.hpp", certificateTypes, emitOpenTypes: false));
            }
        }

        return files;
    }

    /// <summary>
    /// Transitive named-type closure rooted at <paramref name="rootType"/> in
    /// <paramref name="rootModule"/> (for a minimal amalgam compile surface).
    /// </summary>
    private HashSet<(string Module, string Name)> SelectTypeClosure(
        IrDocument document,
        IReadOnlyList<ModulePlan> plans,
        string rootModule,
        string rootType)
    {
        var keyOf = new Dictionary<(string Module, string Name), (IrModule Module, string Name, TypeExpr Type)>();
        foreach (var plan in plans)
        {
            foreach (var (name, type) in plan.Nested)
                keyOf[(plan.Module.Name, name)] = (plan.Module, name, type);
        }

        var rootKey = (rootModule, CppNames.SanitizeTypeName(rootType));
        if (!keyOf.ContainsKey(rootKey))
        {
            // Prefer options.cpp.typeName if present on the root definition.
            var match = keyOf.Keys.FirstOrDefault(k =>
                k.Module == rootModule &&
                string.Equals(k.Name, rootType, StringComparison.Ordinal));
            if (match.Module is null) return new HashSet<(string, string)>();
            rootKey = match;
        }

        var allKeys = keyOf.Keys.ToList();
        var keyIndex = allKeys
            .Select((k, i) => (k, i))
            .ToDictionary(static x => x.k, static x => x.i);

        var selected = new HashSet<(string Module, string Name)> { rootKey };
        var queue = new Queue<(string Module, string Name)>();
        queue.Enqueue(rootKey);
        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            if (!keyOf.TryGetValue(key, out var item)) continue;
            var depIndexes = new HashSet<int>();
            CollectNamedTypeDeps(
                document, item.Module, item.Type, item.Name, keyIndex, depIndexes);
            foreach (var depIndex in depIndexes)
            {
                var depKey = allKeys[depIndex];
                if (selected.Add(depKey))
                    queue.Enqueue(depKey);
            }
        }

        return selected;
    }

    private ModulePlan PlanModule(IrDocument document, IrModule module)
    {
        ResetOpenTypeState();
        var queue = new Queue<(string Name, TypeExpr Type)>();
        foreach (var type in module.Types)
        {
            if (!IrOptions.ShouldGenerate(type.Options))
            {
                continue;
            }

            RejectUnsupportedOptions(type.Options, type.Type);
            EnsureBackendSupport(document, module, type.Type);

            if (IrOptions.CppAliasOf(type.Options) is not null)
            {
                continue;
            }

            var typeName = IrOptions.CppTypeName(type.Options) ?? CppNames.SanitizeTypeName(type.Name);
            if (IsCollapsibleAlias(type.Type))
            {
                CollectNested(document, module, typeName, type.Type, queue);
                continue;
            }

            queue.Enqueue((typeName, type.Type));
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var nested = new List<(string Name, TypeExpr Type)>();
        while (queue.Count > 0)
        {
            var (name, type) = queue.Dequeue();
            if (!emitted.Add(name))
            {
                continue;
            }

            CollectNested(document, module, name, type, queue);
            nested.Add((name, type));
        }

        var openPlan = PlanDirectOpenTypes(document, module, nested, emitted);
        CollectOpenTypeNested(document, module, openPlan, queue);
        while (queue.Count > 0)
        {
            var (name, type) = queue.Dequeue();
            if (!emitted.Add(name))
            {
                continue;
            }

            CollectNested(document, module, name, type, queue);
            nested.Add((name, type));
        }

        var oidValues = CollectOidValues(module);
        if (oidValues.Count > 0)
        {
            var catalogName = OidCatalogName(module);
            if (emitted.Contains(catalogName))
            {
                throw new NotSupportedException(
                    $"Generated OID catalog '{catalogName}' in module '{module.Name}' conflicts with a generated type.");
            }
        }

        return new ModulePlan(module, nested, openPlan, oidValues, emitted);
    }

    private GeneratedFile EmitModuleFile(
        IrDocument document,
        ModulePlan plan,
        IReadOnlyCollection<string> peers)
    {
        var module = plan.Module;
        var ns = ModuleNamespace(module);
        var builder = new StringBuilder();
        var guard = CppNames.IncludeGuard(module.Name);
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine($"#ifndef {guard}");
        builder.AppendLine($"#define {guard}");
        builder.AppendLine();
        EmitRuntimeIncludes(builder);
        foreach (var peer in peers.OrderBy(static name => name, StringComparer.Ordinal))
        {
            builder.AppendLine($"#include \"{CppNames.SanitizeTypeName(peer)}.hpp\"");
        }

        if (peers.Count > 0)
        {
            builder.AppendLine();
        }

        builder.Append(CppNames.NamespaceOpen(ns));
        builder.AppendLine();
        EmitModuleBody(builder, document, plan);
        builder.Append(CppNames.NamespaceClose(ns));
        builder.AppendLine($"#endif // {guard}");
        return new GeneratedFile(CppNames.SanitizeTypeName(module.Name) + ".hpp", builder.ToString());
    }

    private GeneratedFile EmitAmalgamatedFile(
        IrDocument document,
        IReadOnlyList<ModulePlan> plans,
        string fileName,
        HashSet<(string Module, string Name)>? typeFilter = null,
        bool emitOpenTypes = true)
    {
        var builder = new StringBuilder();
        var guard = "ASN1KIT_GENERATED_" +
                    CppNames.SanitizeTypeName(Path.GetFileNameWithoutExtension(fileName))
                        .ToUpperInvariant() + "_HPP";
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("// Amalgamated multi-module header (breaks cyclic peer includes).");
        builder.AppendLine($"#ifndef {guard}");
        builder.AppendLine($"#define {guard}");
        builder.AppendLine();
        EmitRuntimeIncludes(builder);

        var modulesInFilter = typeFilter is null
            ? null
            : new HashSet<string>(typeFilter.Select(static t => t.Module), StringComparer.Ordinal);

        foreach (var plan in plans.OrderBy(static p => p.Module.Name, StringComparer.Ordinal))
        {
            if (modulesInFilter is not null && !modulesInFilter.Contains(plan.Module.Name))
                continue;
            var oidValues = plan.Oids;
            if (oidValues.Count == 0) continue;
            var ns = ModuleNamespace(plan.Module);
            builder.Append(CppNames.NamespaceOpen(ns));
            EmitOidCatalog(builder, OidCatalogName(plan.Module), oidValues);
            builder.AppendLine();
            builder.Append(CppNames.NamespaceClose(ns));
            builder.AppendLine();
        }

        var ordered = OrderAllNestedForEmit(document, plans);
        if (typeFilter is not null)
        {
            ordered = ordered
                .Where(item => typeFilter.Contains((item.Module.Name, item.Name)))
                .ToList();
        }

        string? openNs = null;
        foreach (var (module, name, type) in ordered)
        {
            var ns = ModuleNamespace(module);
            if (!string.Equals(openNs, ns, StringComparison.Ordinal))
            {
                if (openNs is not null)
                {
                    builder.Append(CppNames.NamespaceClose(openNs));
                    builder.AppendLine();
                }

                builder.Append(CppNames.NamespaceOpen(ns));
                builder.AppendLine();
                openNs = ns;
            }

            EmitType(builder, document, module, name, type);
            builder.AppendLine();
        }

        if (openNs is not null)
        {
            builder.Append(CppNames.NamespaceClose(openNs));
            builder.AppendLine();
        }

        if (emitOpenTypes)
        {
            foreach (var plan in plans.OrderBy(static p => p.Module.Name, StringComparer.Ordinal))
            {
                if (plan.Open.Sites.Count == 0) continue;
                if (modulesInFilter is not null && !modulesInFilter.Contains(plan.Module.Name))
                    continue;
                var ns = ModuleNamespace(plan.Module);
                builder.Append(CppNames.NamespaceOpen(ns));
                builder.AppendLine();
                EmitOpenTypes(builder, document, plan.Module, plan.Open);
                builder.Append(CppNames.NamespaceClose(ns));
                builder.AppendLine();
            }
        }

        builder.AppendLine($"#endif // {guard}");
        return new GeneratedFile(fileName, builder.ToString());
    }

    private void EmitModuleBody(StringBuilder builder, IrDocument document, ModulePlan plan)
    {
        var module = plan.Module;
        if (plan.Oids.Count > 0)
        {
            EmitOidCatalog(builder, OidCatalogName(module), plan.Oids);
            builder.AppendLine();
        }

        foreach (var (name, type) in OrderNestedForEmit(document, module, plan.Nested))
        {
            EmitType(builder, document, module, name, type);
            builder.AppendLine();
        }

        EmitOpenTypes(builder, document, module, plan.Open);
    }

    private static void EmitRuntimeIncludes(StringBuilder builder)
    {
        builder.AppendLine("#include \"asn1kit/any.hpp\"");
        builder.AppendLine("#include \"asn1kit/bit_string.hpp\"");
        builder.AppendLine("#include \"asn1kit/bytes.hpp\"");
        builder.AppendLine("#include \"asn1kit/codec.hpp\"");
        builder.AppendLine("#include \"asn1kit/contained.hpp\"");
        builder.AppendLine("#include \"asn1kit/exception.hpp\"");
        builder.AppendLine("#include \"asn1kit/integer.hpp\"");
        builder.AppendLine("#include \"asn1kit/lazy.hpp\"");
        builder.AppendLine("#include \"asn1kit/null.hpp\"");
        builder.AppendLine("#include \"asn1kit/oid.hpp\"");
        builder.AppendLine("#include \"asn1kit/reader.hpp\"");
        builder.AppendLine("#include \"asn1kit/tag.hpp\"");
        builder.AppendLine("#include \"asn1kit/utc_date_time.hpp\"");
        builder.AppendLine("#include \"asn1kit/value.hpp\"");
        builder.AppendLine("#include \"asn1kit/writer.hpp\"");
        builder.AppendLine();
        builder.AppendLine("#include <cstdint>");
        builder.AppendLine("#include <optional>");
        builder.AppendLine("#include <string>");
        builder.AppendLine("#include <utility>");
        builder.AppendLine("#include <variant>");
        builder.AppendLine("#include <vector>");
        builder.AppendLine();
    }

    private HashSet<string> CollectPeerModuleNames(IrDocument document, ModulePlan plan)
    {
        var peers = new HashSet<string>(StringComparer.Ordinal);
        var module = plan.Module;
        foreach (var definition in module.Types)
        {
            if (!IrOptions.ShouldGenerate(definition.Options)) continue;
            CollectPeerRefs(document, module, definition.Type, peers);
        }

        foreach (var binding in plan.Open.Bindings)
            CollectPeerRefs(document, module, binding.Binding.Type, peers);

        peers.Remove(module.Name);
        peers.RemoveWhere(name => document.Modules.All(m => m.Name != name));
        return peers;
    }

    private void CollectPeerRefs(
        IrDocument document, IrModule module, TypeExpr type, HashSet<string> peers)
    {
        // Follow typedef aliases that are themselves refs; never expand named type bodies here
        // (module.Types are walked from the root, and expanding SEQUENCE OF / CHOICE cycles
        // through refs would recurse forever).
        while (type is RefType reference)
        {
            var found = FindWithModule(document, module, reference);
            if (found is not null && found.Value.Module.Name != module.Name)
                peers.Add(found.Value.Module.Name);
            if (reference.OpenTypes is { } uses)
            {
                foreach (var use in uses)
                foreach (var binding in use.Bindings)
                    CollectPeerRefs(document, module, binding.Type, peers);
            }

            if (found?.Def.Type is RefType alias)
            {
                module = found.Value.Module;
                type = alias;
                continue;
            }

            return;
        }

        switch (type)
        {
            case SequenceType sequence:
                foreach (var field in sequence.Components)
                    CollectPeerRefs(document, module, field.Type, peers);
                break;
            case SetType set:
                foreach (var field in set.Components)
                    CollectPeerRefs(document, module, field.Type, peers);
                break;
            case ChoiceType choice:
                foreach (var alt in choice.Components)
                    CollectPeerRefs(document, module, alt.Type, peers);
                break;
            case SequenceOfType of:
                CollectPeerRefs(document, module, of.Element, peers);
                break;
            case SetOfType of:
                CollectPeerRefs(document, module, of.Element, peers);
                break;
            case OctetStringType { Containing: { } content }:
                CollectPeerRefs(document, module, content, peers);
                break;
            case BitStringType { Containing: { } content }:
                CollectPeerRefs(document, module, content, peers);
                break;
            case AnyType { Bindings: { } bindings }:
                foreach (var binding in bindings)
                    CollectPeerRefs(document, module, binding.Type, peers);
                break;
        }
    }

    private static bool ModuleDependencyGraphHasCycle(
        IReadOnlyDictionary<string, HashSet<string>> peers)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in peers.Keys)
        {
            if (Visit(name)) return true;
        }

        return false;

        bool Visit(string name)
        {
            if (visited.Contains(name)) return false;
            if (!visiting.Add(name)) return true;
            if (peers.TryGetValue(name, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (peers.ContainsKey(dep) && Visit(dep)) return true;
                }
            }

            visiting.Remove(name);
            visited.Add(name);
            return false;
        }
    }

    private List<(IrModule Module, string Name, TypeExpr Type)> OrderAllNestedForEmit(
        IrDocument document,
        IReadOnlyList<ModulePlan> plans)
    {
        var items = new List<(IrModule Module, string Name, TypeExpr Type)>();
        var keyOf = new Dictionary<(string Module, string Name), int>();
        foreach (var plan in plans)
        {
            foreach (var (name, type) in plan.Nested)
            {
                keyOf[(plan.Module.Name, name)] = items.Count;
                items.Add((plan.Module, name, type));
            }
        }

        var deps = items.Select(_ => new HashSet<int>()).ToArray();
        for (var index = 0; index < items.Count; index++)
        {
            var (module, name, type) = items[index];
            CollectNamedTypeDeps(document, module, type, name, keyOf, deps[index]);
        }

        var remaining = new HashSet<int>(Enumerable.Range(0, items.Count));
        var ordered = new List<(IrModule Module, string Name, TypeExpr Type)>();
        while (remaining.Count > 0)
        {
            var next = remaining
                .Where(i => deps[i].All(d => !remaining.Contains(d)))
                .OrderBy(i => items[i].Module.Name, StringComparer.Ordinal)
                .ThenBy(i => items[i].Name, StringComparer.Ordinal)
                .DefaultIfEmpty(-1)
                .First();
            if (next < 0)
            {
                next = remaining
                    .OrderBy(i => items[i].Module.Name, StringComparer.Ordinal)
                    .ThenBy(i => items[i].Name, StringComparer.Ordinal)
                    .First();
            }

            remaining.Remove(next);
            ordered.Add(items[next]);
        }

        return ordered;
    }

    private void CollectNamedTypeDeps(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        string owner,
        Dictionary<(string Module, string Name), int> keyOf,
        HashSet<int> deps)
    {
        void AddNamed(string moduleName, string typeName)
        {
            if (keyOf.TryGetValue((moduleName, typeName), out var depIndex))
                deps.Add(depIndex);
        }

        void AddRef(RefType reference)
        {
            var found = FindWithModule(document, module, reference);
            if (found is null) return;
            // Named INTEGER constants structs are not field types; skip as encode deps.
            if (found.Value.Def.Type is IntegerType { NamedValues.Count: > 0 }) return;
            var depName = IrOptions.CppTypeName(found.Value.Def.Options)
                ?? CppNames.SanitizeTypeName(found.Value.Def.Name);
            AddNamed(found.Value.Module.Name, depName);
        }

        void AddNested(string nestedOwner, string hint, TypeExpr expression)
        {
            expression = UnwrapAliases(document, module, expression);
            if (expression is RefType reference)
            {
                AddRef(reference);
                return;
            }

            if (NeedsNamedType(expression) || IsEnumerated(expression) || IsNamedBitString(expression) ||
                IsNamedInteger(expression))
            {
                AddNamed(module.Name, nestedOwner + "_" + CppNames.SanitizeTypeName(hint));
            }
        }

        void Walk(TypeExpr expression, IrModule context, string nestedOwner)
        {
            expression = UnwrapAliases(document, context, expression);
            switch (expression)
            {
                case RefType reference:
                    AddRef(reference);
                    break;
                case SequenceType sequence:
                    foreach (var field in sequence.Components)
                    {
                        AddNested(nestedOwner, field.Name, field.Type);
                        Walk(field.Type, context, nestedOwner + "_" + CppNames.SanitizeTypeName(field.Name));
                    }

                    break;
                case SetType set:
                    foreach (var field in set.Components)
                    {
                        AddNested(nestedOwner, field.Name, field.Type);
                        Walk(field.Type, context, nestedOwner + "_" + CppNames.SanitizeTypeName(field.Name));
                    }

                    break;
                case ChoiceType choice:
                    foreach (var alt in choice.Components)
                    {
                        AddNested(nestedOwner, alt.Name, alt.Type);
                        Walk(alt.Type, context, nestedOwner + "_" + CppNames.SanitizeTypeName(alt.Name));
                    }

                    break;
                case SequenceOfType of:
                    AddNested(nestedOwner, "Item", of.Element);
                    Walk(of.Element, context, nestedOwner + "_Item");
                    break;
                case SetOfType of:
                    AddNested(nestedOwner, "Item", of.Element);
                    Walk(of.Element, context, nestedOwner + "_Item");
                    break;
                case OctetStringType { Containing: { } content }:
                    AddNested(nestedOwner, "Content", content);
                    Walk(content, context, nestedOwner + "_Content");
                    break;
                case BitStringType { Containing: { } content }:
                    AddNested(nestedOwner, "Content", content);
                    Walk(content, context, nestedOwner + "_Content");
                    break;
                case AnyType { Bindings: { } bindings }:
                    foreach (var binding in bindings)
                        Walk(binding.Type, context, nestedOwner);
                    break;
            }
        }

        Walk(type, module, owner);
    }
}

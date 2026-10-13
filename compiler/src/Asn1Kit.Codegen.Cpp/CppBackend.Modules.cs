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

    private const int MaxCycleFileStemLength = 120;

    public IReadOnlyList<GeneratedFile> Generate(IrDocument document)
    {
        OpenTypeBindings.NormalizeLegacy(document);
        var modules = document.Modules
            .Where(module => module.Types.Any(static type => IrOptions.ShouldGenerate(type.Options)))
            .ToList();
        var plans = modules.Select(module => PlanModule(document, module)).ToList();

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

        var adj = items.Select(_ => new HashSet<int>()).ToArray();
        for (var index = 0; index < items.Count; index++)
        {
            var (module, name, type) = items[index];
            CollectNamedTypeDeps(document, module, type, name, keyOf, adj[index]);
        }

        var sccs = ComputeTypeSccs(adj);
        var amalgams = sccs
            .Where(component => IsNontrivialScc(component, adj))
            .Select(component => component
                .Select(i => (Module: items[i].Module.Name, Name: items[i].Name))
                .OrderBy(static t => t.Module, StringComparer.Ordinal)
                .ThenBy(static t => t.Name, StringComparer.Ordinal)
                .ToHashSet())
            .OrderBy(static members => string.Join('\0',
                    members.OrderBy(static t => t.Module, StringComparer.Ordinal)
                        .ThenBy(static t => t.Name, StringComparer.Ordinal)
                        .Select(m => m.Module + '\u0001' + m.Name)),
                StringComparer.Ordinal)
            .ToList();

        var cycleNames = AssignCycleFileNames(amalgams);
        var typeToCycleFile = new Dictionary<(string Module, string Name), string>();
        for (var i = 0; i < amalgams.Count; i++)
        {
            foreach (var key in amalgams[i])
            {
                typeToCycleFile[key] = cycleNames[i];
            }
        }

        var typesInAmalgams = new HashSet<(string Module, string Name)>(typeToCycleFile.Keys);
        // Split Module.types.hpp only when cycle amalgams exist AND this module needs a
        // facade (owned cycle / post types / open-type catalogs). Otherwise keep a single
        // Module.hpp (types + OID together) so OID-only facades are not duplicated.
        var useTypesSplit = amalgams.Count > 0;
        var postTypes = useTypesSplit
            ? ComputePostTypes(items, adj, typesInAmalgams)
            : new HashSet<(string Module, string Name)>();
        var modulesWithTypesFile = new HashSet<string>(StringComparer.Ordinal);
        if (useTypesSplit)
        {
            foreach (var plan in plans)
            {
                var hasCore = plan.Nested.Any(t =>
                    !typesInAmalgams.Contains((plan.Module.Name, t.Name)) &&
                    !postTypes.Contains((plan.Module.Name, t.Name)));
                if (hasCore && ModuleNeedsFacade(plan, typeToCycleFile, postTypes))
                {
                    modulesWithTypesFile.Add(plan.Module.Name);
                }
            }
        }

        var peerModulesByModule = BuildPeerModulesByModule(
            document, plans, typesInAmalgams, postTypes, typeToCycleFile, keyOf);

        var files = new List<GeneratedFile>(plans.Count * 3 + amalgams.Count);

        for (var i = 0; i < amalgams.Count; i++)
        {
            var amalgam = amalgams[i];
            var includes = CollectAmalgamIncludes(
                amalgam, cycleNames[i], typeToCycleFile, modulesWithTypesFile, items, keyOf, adj);
            files.Add(EmitAmalgamatedFile(
                document,
                plans,
                cycleNames[i],
                amalgam,
                emitOpenTypes: false,
                emitOidCatalogs: false,
                includes));
        }

        foreach (var plan in plans)
        {
            if (!useTypesSplit)
            {
                var localNested = plan.Nested
                    .Where(t => !typesInAmalgams.Contains((plan.Module.Name, t.Name)))
                    .ToList();
                var includes = CollectFacadeIncludes(
                    document, plan, localNested, typeToCycleFile, typesInAmalgams, postTypes,
                    modulesWithTypesFile, keyOf, peerTypesFiles: false);
                files.Add(EmitModuleFile(
                    document, plan, localNested, includes, emitOids: true,
                    typeToCycleFile, typesInAmalgams, postTypes, modulesWithTypesFile, keyOf,
                    peerTypesFiles: false, peerModulesByModule));
                continue;
            }

            var coreNested = plan.Nested
                .Where(t =>
                    !typesInAmalgams.Contains((plan.Module.Name, t.Name)) &&
                    !postTypes.Contains((plan.Module.Name, t.Name)))
                .ToList();
            var postNested = plan.Nested
                .Where(t => postTypes.Contains((plan.Module.Name, t.Name)))
                .ToList();
            var emitTypesFile = modulesWithTypesFile.Contains(plan.Module.Name);

            if (emitTypesFile)
            {
                files.Add(EmitTypesFile(
                    document, plan, coreNested, typesInAmalgams, postTypes, modulesWithTypesFile,
                    keyOf, peerModulesByModule));
            }

            if (emitTypesFile || ModuleNeedsFacade(plan, typeToCycleFile, postTypes))
            {
                var facadeNested = emitTypesFile ? postNested : coreNested.Concat(postNested).ToList();
                var facadeIncludes = CollectFacadeIncludes(
                    document, plan, facadeNested, typeToCycleFile, typesInAmalgams, postTypes,
                    modulesWithTypesFile, keyOf, peerTypesFiles: true);
                files.Add(EmitModuleFile(
                    document, plan, facadeNested, facadeIncludes, emitOids: !emitTypesFile,
                    typeToCycleFile, typesInAmalgams, postTypes, modulesWithTypesFile, keyOf,
                    peerTypesFiles: true, peerModulesByModule));
            }
            else
            {
                // No facade content: single Module.hpp with core types + OID catalog.
                var includes = CollectFacadeIncludes(
                    document, plan, coreNested, typeToCycleFile, typesInAmalgams, postTypes,
                    modulesWithTypesFile, keyOf, peerTypesFiles: true);
                files.Add(EmitModuleFile(
                    document, plan, coreNested, includes, emitOids: true,
                    typeToCycleFile, typesInAmalgams, postTypes, modulesWithTypesFile, keyOf,
                    peerTypesFiles: true, peerModulesByModule));
            }
        }

        foreach (var plan in plans)
        {
            if (plan.Nested.Any(t => HasFormatDefinition(t.Type)))
            {
                files.Add(EmitFormatFile(document, plan, items, keyOf));
            }
        }

        return files;
    }

    private static bool ModuleNeedsFacade(
        ModulePlan plan,
        Dictionary<(string Module, string Name), string> typeToCycleFile,
        HashSet<(string Module, string Name)> postTypes)
    {
        foreach (var (name, _) in plan.Nested)
        {
            if (typeToCycleFile.ContainsKey((plan.Module.Name, name)))
            {
                return true;
            }

            if (postTypes.Contains((plan.Module.Name, name)))
            {
                return true;
            }
        }

        return plan.Open.Sites.Count > 0 || plan.Open.Bindings.Count > 0;
    }

    private static string TypesFileName(string moduleName) =>
        CppNames.SanitizeTypeName(moduleName) + ".types.hpp";

    /// <summary>
    /// Non-SCC types that layout-depend on an SCC type (directly or transitively).
    /// Emitted in the module facade after cycle includes, not in Module.types.hpp.
    /// </summary>
    private static HashSet<(string Module, string Name)> ComputePostTypes(
        IReadOnlyList<(IrModule Module, string Name, TypeExpr Type)> items,
        HashSet<int>[] adj,
        HashSet<(string Module, string Name)> typesInAmalgams)
    {
        var n = items.Count;
        var state = new byte[n];
        var reaches = new bool[n];

        void Dfs(int i)
        {
            if (state[i] != 0)
            {
                return;
            }

            state[i] = 1;
            var key = (Module: items[i].Module.Name, Name: items[i].Name);
            if (typesInAmalgams.Contains(key))
            {
                reaches[i] = true;
                state[i] = 2;
                return;
            }

            foreach (var dep in adj[i])
            {
                Dfs(dep);
                if (reaches[dep])
                {
                    reaches[i] = true;
                }
            }

            state[i] = 2;
        }

        for (var i = 0; i < n; i++)
        {
            Dfs(i);
        }

        var post = new HashSet<(string Module, string Name)>();
        for (var i = 0; i < n; i++)
        {
            var key = (Module: items[i].Module.Name, Name: items[i].Name);
            if (!typesInAmalgams.Contains(key) && reaches[i])
            {
                post.Add(key);
            }
        }

        return post;
    }

    private static bool IsNontrivialScc(List<int> component, HashSet<int>[] adj) =>
        component.Count > 1 ||
        (component.Count == 1 && adj[component[0]].Contains(component[0]));

    private static List<List<int>> ComputeTypeSccs(HashSet<int>[] adj)
    {
        var n = adj.Length;
        var index = new int[n];
        Array.Fill(index, -1);
        var lowlink = new int[n];
        var onStack = new bool[n];
        var stack = new Stack<int>();
        var result = new List<List<int>>();
        var current = 0;

        void StrongConnect(int v)
        {
            index[v] = current;
            lowlink[v] = current;
            current++;
            stack.Push(v);
            onStack[v] = true;

            foreach (var succ in adj[v])
            {
                if (index[succ] < 0)
                {
                    StrongConnect(succ);
                    lowlink[v] = Math.Min(lowlink[v], lowlink[succ]);
                }
                else if (onStack[succ])
                {
                    lowlink[v] = Math.Min(lowlink[v], index[succ]);
                }
            }

            if (lowlink[v] != index[v])
            {
                return;
            }

            var component = new List<int>();
            int member;
            do
            {
                member = stack.Pop();
                onStack[member] = false;
                component.Add(member);
            } while (member != v);

            result.Add(component);
        }

        for (var v = 0; v < n; v++)
        {
            if (index[v] < 0)
            {
                StrongConnect(v);
            }
        }

        return result;
    }

    private static List<string> AssignCycleFileNames(
        IReadOnlyList<HashSet<(string Module, string Name)>> amalgams)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>(amalgams.Count);
        foreach (var amalgam in amalgams)
        {
            var members = amalgam
                .OrderBy(static t => t.Module, StringComparer.Ordinal)
                .ThenBy(static t => t.Name, StringComparer.Ordinal)
                .ToList();
            var parts = members
                .Select(static t =>
                    CppNames.SanitizeTypeName(t.Module) + "_" + CppNames.SanitizeTypeName(t.Name))
                .ToList();
            var stem = "cycle_" + string.Join("_", parts);
            if (stem.Length > MaxCycleFileStemLength)
            {
                var keep = Math.Min(2, parts.Count);
                stem = "cycle_" + string.Join("_", parts.Take(keep));
                if (parts.Count > keep)
                {
                    stem += "_and_" + (parts.Count - keep) + "_more";
                }
            }

            var candidate = stem + ".hpp";
            var suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = stem + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            ".hpp";
                suffix++;
            }

            names.Add(candidate);
        }

        return names;
    }

    private static List<string> CollectAmalgamIncludes(
        HashSet<(string Module, string Name)> amalgam,
        string selfFile,
        Dictionary<(string Module, string Name), string> typeToCycleFile,
        HashSet<string> modulesWithTypesFile,
        IReadOnlyList<(IrModule Module, string Name, TypeExpr Type)> items,
        Dictionary<(string Module, string Name), int> keyOf,
        HashSet<int>[] adj)
    {
        var peerIncludes = new HashSet<string>(StringComparer.Ordinal);
        var cycleIncludes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in amalgam)
        {
            if (!keyOf.TryGetValue(key, out var index))
            {
                continue;
            }

            foreach (var dep in adj[index])
            {
                var depKey = (Module: items[dep].Module.Name, Name: items[dep].Name);
                if (amalgam.Contains(depKey))
                {
                    continue;
                }

                if (typeToCycleFile.TryGetValue(depKey, out var cycleFile) &&
                    !string.Equals(cycleFile, selfFile, StringComparison.Ordinal))
                {
                    cycleIncludes.Add(cycleFile);
                    continue;
                }

                peerIncludes.Add(
                    modulesWithTypesFile.Contains(depKey.Module)
                        ? TypesFileName(depKey.Module)
                        : CppNames.SanitizeTypeName(depKey.Module) + ".hpp");
            }
        }

        return peerIncludes
            .OrderBy(static n => n, StringComparer.Ordinal)
            .Concat(cycleIncludes.OrderBy(static n => n, StringComparer.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Peer module names required by a single core type in Module.types.hpp.
    /// Throws if the type layout-depends on cycle/post (invariant of the types split).
    /// </summary>
    private HashSet<string> CollectTypesFilePeerModulesForType(
        IrDocument document,
        IrModule module,
        string typeName,
        TypeExpr type,
        HashSet<(string Module, string Name)> typesInAmalgams,
        HashSet<(string Module, string Name)> postTypes,
        Dictionary<(string Module, string Name), int> keyOf)
    {
        var peers = new HashSet<string>(StringComparer.Ordinal);
        var deps = new HashSet<int>();
        CollectNamedTypeDeps(document, module, type, typeName, keyOf, deps);
        foreach (var pair in keyOf)
        {
            if (!deps.Contains(pair.Value))
            {
                continue;
            }

            var key = pair.Key;
            if (string.Equals(key.Module, module.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (typesInAmalgams.Contains(key) || postTypes.Contains(key))
            {
                throw new InvalidOperationException(
                    $"Core type in module '{module.Name}' depends on cycle/post type '{key.Module}.{key.Name}'.");
            }

            peers.Add(key.Module);
        }

        return peers;
    }

    /// <summary>
    /// Peer module names required by one type when emitting a Module.hpp body (facade or unsplit).
    /// Cycle-amalgam deps are excluded (those headers stay eager at file top).
    /// </summary>
    private HashSet<string> CollectFacadePeerModulesForType(
        IrDocument document,
        IrModule module,
        string typeName,
        TypeExpr type,
        Dictionary<(string Module, string Name), string> typeToCycleFile,
        HashSet<(string Module, string Name)> typesInAmalgams,
        HashSet<(string Module, string Name)> postTypes,
        Dictionary<(string Module, string Name), int> keyOf)
    {
        var peers = new HashSet<string>(StringComparer.Ordinal);
        var deps = new HashSet<int>();
        CollectNamedTypeDeps(document, module, type, typeName, keyOf, deps);
        foreach (var pair in keyOf)
        {
            if (!deps.Contains(pair.Value))
            {
                continue;
            }

            var key = pair.Key;
            if (string.Equals(key.Module, module.Name, StringComparison.Ordinal) &&
                !typesInAmalgams.Contains(key) &&
                !postTypes.Contains(key))
            {
                continue;
            }

            if (typeToCycleFile.ContainsKey(key))
            {
                continue;
            }

            if (string.Equals(key.Module, module.Name, StringComparison.Ordinal))
            {
                continue;
            }

            peers.Add(key.Module);
        }

        return peers;
    }

    private static string PeerHeaderForTypesFile(
        string peerModule,
        HashSet<string> modulesWithTypesFile) =>
        modulesWithTypesFile.Contains(peerModule)
            ? TypesFileName(peerModule)
            : CppNames.SanitizeTypeName(peerModule) + ".hpp";

    /// <summary>
    /// Peer headers required by one type when emitting a Module.hpp body (facade or unsplit).
    /// </summary>
    private HashSet<string> CollectFacadePeerIncludesForType(
        IrDocument document,
        IrModule module,
        string typeName,
        TypeExpr type,
        Dictionary<(string Module, string Name), string> typeToCycleFile,
        HashSet<(string Module, string Name)> typesInAmalgams,
        HashSet<(string Module, string Name)> postTypes,
        HashSet<string> modulesWithTypesFile,
        Dictionary<(string Module, string Name), int> keyOf,
        bool peerTypesFiles)
    {
        var moduleIncludes = new HashSet<string>(StringComparer.Ordinal);
        var deps = new HashSet<int>();
        CollectNamedTypeDeps(document, module, type, typeName, keyOf, deps);
        foreach (var pair in keyOf)
        {
            if (!deps.Contains(pair.Value))
            {
                continue;
            }

            var key = pair.Key;
            if (string.Equals(key.Module, module.Name, StringComparison.Ordinal) &&
                !typesInAmalgams.Contains(key) &&
                !postTypes.Contains(key))
            {
                continue;
            }

            if (typeToCycleFile.ContainsKey(key))
            {
                continue;
            }

            if (string.Equals(key.Module, module.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (!peerTypesFiles || postTypes.Contains(key) ||
                !modulesWithTypesFile.Contains(key.Module))
            {
                moduleIncludes.Add(CppNames.SanitizeTypeName(key.Module) + ".hpp");
                continue;
            }

            moduleIncludes.Add(TypesFileName(key.Module));
        }

        return moduleIncludes;
    }

    /// <summary>
    /// True when following peer-module includes from <paramref name="peer"/> reaches
    /// <paramref name="self"/> (include-only triangle such as CMS→AttrCert→ESS→CMS).
    /// </summary>
    private static bool PeerModuleReenters(
        string self,
        string peer,
        IReadOnlyDictionary<string, HashSet<string>> peerModulesByModule)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(peer);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            if (string.Equals(current, self, StringComparison.Ordinal))
            {
                return true;
            }

            if (!peerModulesByModule.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var moduleName in next)
            {
                stack.Push(moduleName);
            }
        }

        return false;
    }

    private List<(string Name, TypeExpr Type)> OrderNestedPreferringNonReentrantPeers(
        IrDocument document,
        IrModule module,
        IReadOnlyList<(string Name, TypeExpr Type)> nested,
        Func<string, TypeExpr, HashSet<string>> peerModulesForType,
        IReadOnlyDictionary<string, HashSet<string>> peerModulesByModule,
        Dictionary<(string Module, string Name), int> keyOf)
    {
        var ordered = OrderNestedForEmit(document, module, nested);
        var deferred = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ordered)
        {
            var peers = peerModulesForType(item.Name, item.Type);
            if (peers.Any(peer => PeerModuleReenters(module.Name, peer, peerModulesByModule)))
            {
                deferred.Add(item.Name);
            }
        }

        // Keep in-module topo: anything that layout-depends on a deferred type is deferred too
        // (e.g. OriginatorInfo → CertificateChoices → AttrCert).
        var localDeps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var item in ordered)
        {
            var deps = new HashSet<int>();
            CollectNamedTypeDeps(document, module, item.Type, item.Name, keyOf, deps);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in keyOf)
            {
                if (deps.Contains(pair.Value) &&
                    string.Equals(pair.Key.Module, module.Name, StringComparison.Ordinal))
                {
                    names.Add(pair.Key.Name);
                }
            }

            localDeps[item.Name] = names;
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var item in ordered)
            {
                if (deferred.Contains(item.Name))
                {
                    continue;
                }

                if (localDeps[item.Name].Any(deferred.Contains))
                {
                    deferred.Add(item.Name);
                    changed = true;
                }
            }
        }
        while (changed);

        return ordered.Where(item => !deferred.Contains(item.Name))
            .Concat(ordered.Where(item => deferred.Contains(item.Name)))
            .ToList();
    }

    private Dictionary<string, HashSet<string>> BuildPeerModulesByModule(
        IrDocument document,
        IReadOnlyList<ModulePlan> plans,
        HashSet<(string Module, string Name)> typesInAmalgams,
        HashSet<(string Module, string Name)> postTypes,
        Dictionary<(string Module, string Name), string> typeToCycleFile,
        Dictionary<(string Module, string Name), int> keyOf)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            var peers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, type) in plan.Nested)
            {
                if (typesInAmalgams.Contains((plan.Module.Name, name)))
                {
                    continue;
                }

                peers.UnionWith(CollectFacadePeerModulesForType(
                    document, plan.Module, name, type, typeToCycleFile, typesInAmalgams, postTypes,
                    keyOf));
            }

            result[plan.Module.Name] = peers;
        }

        return result;
    }

    private static bool IsEagerFacadeInclude(string include, string moduleName)
    {
        if (include.StartsWith("cycle_", StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(include, TypesFileName(moduleName), StringComparison.Ordinal);
    }

    private static void EnsureNamespaceOpen(StringBuilder builder, string ns, ref bool namespaceOpen)
    {
        if (namespaceOpen)
        {
            return;
        }

        builder.Append(CppNames.NamespaceOpen(ns));
        builder.AppendLine();
        namespaceOpen = true;
    }

    /// <summary>
    /// Emits peer includes at file scope. Closes an open namespace first when it already
    /// has content; never opens an empty namespace just to close it again.
    /// Leaves <paramref name="namespaceOpen"/> false after emitting includes.
    /// </summary>
    private static void EmitDeferredPeerIncludes(
        StringBuilder builder,
        string ns,
        ref bool namespaceOpen,
        IEnumerable<string> includes)
    {
        var ordered = includes
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        if (namespaceOpen)
        {
            builder.Append(CppNames.NamespaceClose(ns));
            builder.AppendLine();
            namespaceOpen = false;
        }

        foreach (var include in ordered)
        {
            builder.AppendLine($"#include \"{include}\"");
        }

        builder.AppendLine();
    }

    private List<string> CollectFacadeIncludes(
        IrDocument document,
        ModulePlan plan,
        IReadOnlyList<(string Name, TypeExpr Type)> postNested,
        Dictionary<(string Module, string Name), string> typeToCycleFile,
        HashSet<(string Module, string Name)> typesInAmalgams,
        HashSet<(string Module, string Name)> postTypes,
        HashSet<string> modulesWithTypesFile,
        Dictionary<(string Module, string Name), int> keyOf,
        bool peerTypesFiles)
    {
        var typesIncludes = new HashSet<string>(StringComparer.Ordinal);
        var cycleIncludes = new HashSet<string>(StringComparer.Ordinal);
        var moduleIncludes = new HashSet<string>(StringComparer.Ordinal);
        var module = plan.Module;

        if (modulesWithTypesFile.Contains(module.Name))
        {
            typesIncludes.Add(TypesFileName(module.Name));
        }

        // Re-export cycle amalgams that absorbed this module's types so
        // `#include "Module.hpp"` still reaches them.
        foreach (var (name, _) in plan.Nested)
        {
            if (typeToCycleFile.TryGetValue((module.Name, name), out var ownedCycle))
            {
                cycleIncludes.Add(ownedCycle);
            }
        }

        void ConsiderKey((string Module, string Name) key)
        {
            if (string.Equals(key.Module, module.Name, StringComparison.Ordinal) &&
                !typesInAmalgams.Contains(key) &&
                !postTypes.Contains(key))
            {
                return;
            }

            if (typeToCycleFile.TryGetValue(key, out var cycleFile))
            {
                cycleIncludes.Add(cycleFile);
                return;
            }

            if (string.Equals(key.Module, module.Name, StringComparison.Ordinal))
            {
                return;
            }

            if (!peerTypesFiles || postTypes.Contains(key) ||
                !modulesWithTypesFile.Contains(key.Module))
            {
                moduleIncludes.Add(CppNames.SanitizeTypeName(key.Module) + ".hpp");
                return;
            }

            moduleIncludes.Add(TypesFileName(key.Module));
        }

        foreach (var (name, type) in postNested)
        {
            var deps = new HashSet<int>();
            CollectNamedTypeDeps(document, module, type, name, keyOf, deps);
            foreach (var pair in keyOf)
            {
                if (deps.Contains(pair.Value))
                {
                    ConsiderKey(pair.Key);
                }
            }
        }

        foreach (var binding in plan.Open.Bindings)
        {
            var deps = new HashSet<int>();
            CollectNamedTypeDeps(document, module, binding.Binding.Type, "_open", keyOf, deps);
            foreach (var pair in keyOf)
            {
                if (deps.Contains(pair.Value))
                {
                    ConsiderKey(pair.Key);
                }
            }
        }

        return typesIncludes
            .OrderBy(static n => n, StringComparer.Ordinal)
            .Concat(cycleIncludes.OrderBy(static n => n, StringComparer.Ordinal))
            .Concat(moduleIncludes.OrderBy(static n => n, StringComparer.Ordinal))
            .ToList();
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

    private GeneratedFile EmitTypesFile(
        IrDocument document,
        ModulePlan plan,
        IReadOnlyList<(string Name, TypeExpr Type)> nested,
        HashSet<(string Module, string Name)> typesInAmalgams,
        HashSet<(string Module, string Name)> postTypes,
        HashSet<string> modulesWithTypesFile,
        Dictionary<(string Module, string Name), int> keyOf,
        IReadOnlyDictionary<string, HashSet<string>> peerModulesByModule)
    {
        var module = plan.Module;
        var ns = ModuleNamespace(module);
        var builder = new StringBuilder();
        var guard = CppNames.IncludeGuard(module.Name + "_types");
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("// Non-cycle named types + OID catalog (safe for cycle_*.hpp to include).");
        builder.AppendLine($"#ifndef {guard}");
        builder.AppendLine($"#define {guard}");
        builder.AppendLine();
        EmitRuntimeIncludes(builder);
        // Peer includes are deferred until the first owning type so include-only
        // triangles (CMS.types → AttrCert → ESS → CMS.types) see local types first.
        var namespaceOpen = false;
        if (plan.Oids.Count > 0)
        {
            EnsureNamespaceOpen(builder, ns, ref namespaceOpen);
            EmitOidCatalog(builder, OidCatalogName(module), plan.Oids);
            builder.AppendLine();
        }

        var emitOrder = OrderNestedPreferringNonReentrantPeers(
            document,
            module,
            nested,
            (name, type) => CollectTypesFilePeerModulesForType(
                document, module, name, type, typesInAmalgams, postTypes, keyOf),
            peerModulesByModule,
            keyOf);

        var emittedIncludes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, type) in emitOrder)
        {
            var peerModules = CollectTypesFilePeerModulesForType(
                document, module, name, type, typesInAmalgams, postTypes, keyOf);
            var headers = peerModules
                .Select(peer => PeerHeaderForTypesFile(peer, modulesWithTypesFile));
            EmitDeferredPeerIncludes(builder, ns, ref namespaceOpen, headers.Where(emittedIncludes.Add));
            EnsureNamespaceOpen(builder, ns, ref namespaceOpen);
            EmitType(builder, document, module, name, type);
            builder.AppendLine();
        }

        if (namespaceOpen)
        {
            builder.Append(CppNames.NamespaceClose(ns));
        }

        builder.AppendLine($"#endif // {guard}");
        return new GeneratedFile(TypesFileName(module.Name), builder.ToString());
    }

    private GeneratedFile EmitModuleFile(
        IrDocument document,
        ModulePlan plan,
        IReadOnlyList<(string Name, TypeExpr Type)> nested,
        IReadOnlyList<string> includes,
        bool emitOids,
        Dictionary<(string Module, string Name), string> typeToCycleFile,
        HashSet<(string Module, string Name)> typesInAmalgams,
        HashSet<(string Module, string Name)> postTypes,
        HashSet<string> modulesWithTypesFile,
        Dictionary<(string Module, string Name), int> keyOf,
        bool peerTypesFiles,
        IReadOnlyDictionary<string, HashSet<string>> peerModulesByModule)
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

        var eager = includes
            .Where(include => IsEagerFacadeInclude(include, module.Name))
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToList();
        foreach (var include in eager)
        {
            builder.AppendLine($"#include \"{include}\"");
        }

        if (eager.Count > 0)
        {
            builder.AppendLine();
        }

        var namespaceOpen = false;
        if (emitOids && plan.Oids.Count > 0)
        {
            EnsureNamespaceOpen(builder, ns, ref namespaceOpen);
            EmitOidCatalog(builder, OidCatalogName(module), plan.Oids);
            builder.AppendLine();
        }

        var emitOrder = OrderNestedPreferringNonReentrantPeers(
            document,
            module,
            nested,
            (name, type) => CollectFacadePeerModulesForType(
                document, module, name, type, typeToCycleFile, typesInAmalgams, postTypes, keyOf),
            peerModulesByModule,
            keyOf);

        var emittedIncludes = new HashSet<string>(eager, StringComparer.Ordinal);
        foreach (var (name, type) in emitOrder)
        {
            var headers = CollectFacadePeerIncludesForType(
                document, module, name, type, typeToCycleFile, typesInAmalgams, postTypes,
                modulesWithTypesFile, keyOf, peerTypesFiles);
            EmitDeferredPeerIncludes(builder, ns, ref namespaceOpen, headers.Where(emittedIncludes.Add));
            EnsureNamespaceOpen(builder, ns, ref namespaceOpen);
            EmitType(builder, document, module, name, type);
            builder.AppendLine();
        }

        // Open-type bindings may need peers not referenced by nested types.
        var remainingPeers = includes
            .Where(include => !IsEagerFacadeInclude(include, module.Name) && emittedIncludes.Add(include))
            .ToList();
        EmitDeferredPeerIncludes(builder, ns, ref namespaceOpen, remainingPeers);

        if (plan.Open.Sites.Count > 0)
        {
            EnsureNamespaceOpen(builder, ns, ref namespaceOpen);
            EmitOpenTypes(builder, document, module, plan.Open);
        }

        if (namespaceOpen)
        {
            builder.Append(CppNames.NamespaceClose(ns));
        }

        builder.AppendLine($"#endif // {guard}");
        return new GeneratedFile(CppNames.SanitizeTypeName(module.Name) + ".hpp", builder.ToString());
    }

    private GeneratedFile EmitAmalgamatedFile(
        IrDocument document,
        IReadOnlyList<ModulePlan> plans,
        string fileName,
        HashSet<(string Module, string Name)> typeFilter,
        bool emitOpenTypes,
        bool emitOidCatalogs,
        IReadOnlyList<string> includes)
    {
        var builder = new StringBuilder();
        var guard = "ASN1KIT_GENERATED_" +
                    CppNames.SanitizeTypeName(Path.GetFileNameWithoutExtension(fileName))
                        .ToUpperInvariant() + "_HPP";
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("// Type-SCC amalgam (breaks cyclic named-type dependencies).");
        builder.AppendLine($"#ifndef {guard}");
        builder.AppendLine($"#define {guard}");
        builder.AppendLine();
        EmitRuntimeIncludes(builder);
        foreach (var include in includes)
        {
            builder.AppendLine($"#include \"{include}\"");
        }

        if (includes.Count > 0)
        {
            builder.AppendLine();
        }

        var modulesInFilter = new HashSet<string>(
            typeFilter.Select(static t => t.Module), StringComparer.Ordinal);

        if (emitOidCatalogs)
        {
            foreach (var plan in plans.OrderBy(static p => p.Module.Name, StringComparer.Ordinal))
            {
                if (!modulesInFilter.Contains(plan.Module.Name))
                {
                    continue;
                }

                if (plan.Oids.Count == 0)
                {
                    continue;
                }

                var oidNs = ModuleNamespace(plan.Module);
                builder.Append(CppNames.NamespaceOpen(oidNs));
                EmitOidCatalog(builder, OidCatalogName(plan.Module), plan.Oids);
                builder.AppendLine();
                builder.Append(CppNames.NamespaceClose(oidNs));
                builder.AppendLine();
            }
        }

        var ordered = OrderAllNestedForEmit(document, plans)
            .Where(item => typeFilter.Contains((item.Module.Name, item.Name)))
            .ToList();

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
                if (plan.Open.Sites.Count == 0 || !modulesInFilter.Contains(plan.Module.Name))
                {
                    continue;
                }

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

    private void EmitModuleBody(
        StringBuilder builder,
        IrDocument document,
        ModulePlan plan,
        IReadOnlyList<(string Name, TypeExpr Type)> nested,
        bool emitOids)
    {
        var module = plan.Module;
        if (emitOids && plan.Oids.Count > 0)
        {
            EmitOidCatalog(builder, OidCatalogName(module), plan.Oids);
            builder.AppendLine();
        }

        foreach (var (name, type) in OrderNestedForEmit(document, module, nested))
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
                // Cycle break: prefer types that other remaining types depend on
                // (payloads before containers), then fewest pending outs.
                next = remaining
                    .OrderByDescending(i => remaining.Count(j => deps[j].Contains(i)))
                    .ThenBy(i => deps[i].Count(d => remaining.Contains(d)))
                    .ThenBy(i => items[i].Module.Name, StringComparer.Ordinal)
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
                // Open-type AnyType.Bindings are not layout deps (payload stays any /
                // contained<any>); binding descriptors are included via OpenPlan.
            }
        }

        Walk(type, module, owner);
    }
}

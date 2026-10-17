using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.Cpp;

public sealed partial class CppBackend
{
    private sealed record OpenContainer(IrModule Module, string Owner, string TypeName,
        IReadOnlyList<IrComponent> Fields);

    private enum OpenPayloadKind
    {
        Direct,
        Contained,
        Many
    }

    private sealed record OpenPayload(TypeExpr Type, OpenPayloadKind Kind);

    private sealed record OpenSite(
        OpenContainer Container,
        IrComponent Field,
        OpenPayload Payload,
        IrOpenTypeSelector Selector,
        IReadOnlyList<OpenContainer> Parents,
        IrModule ContextModule,
        string ContextOwner,
        IReadOnlyList<string> ContextPath,
        bool CrossesInlineBoundary,
        string? Table,
        IReadOnlyList<string> Path,
        List<OpenBinding> Bindings)
    {
        public string BindingStem { get; set; } = "";
        public string CatalogStem { get; set; } = "";
    }

    private sealed record OpenBinding(string MemberName, IrOpenTypeBinding Binding, OpenSite Site);

    private sealed record OpenViewNames(
        string Accessor,
        string? FactoryAccessor,
        string ViewType);

    private sealed record OpenFieldViewNames(string ViewType);

    private sealed class OpenPlan
    {
        public List<OpenSite> Sites { get; } = new();
        public List<OpenBinding> Bindings { get; } = new();
    }

    private readonly Dictionary<(string Module, string Type), List<OpenSite>> _openSitesByType = new();
    private readonly Dictionary<(string Module, string Type, string Usage), (string Table, string Field)>
        _openViewPaths = new();
    private readonly Dictionary<(string Module, string Catalog), string> _openCatalogEntryNames = new();
    private readonly Dictionary<(string Module, string Type, string Table), OpenViewNames>
        _openViewNames = new();
    private readonly Dictionary<(string Module, string Type, string Table, string Field), OpenFieldViewNames>
        _openFieldViewNames = new();

    private void ConfigureOpenTypeEmission(IEnumerable<ModulePlan> plans)
    {
        _openSitesByType.Clear();
        _openViewPaths.Clear();
        _openCatalogEntryNames.Clear();
        _openViewNames.Clear();
        _openFieldViewNames.Clear();
        var materializedPlans = plans.ToList();
        foreach (var plan in materializedPlans)
        foreach (var site in plan.Open.Sites)
        {
            Add(site.Container.Module.Name, site.Container.TypeName, site);
            if (site.ContextOwner != site.Container.TypeName || site.ContextModule != site.Container.Module)
                Add(site.ContextModule.Name, site.ContextOwner, site);
        }

        void Add(string module, string type, OpenSite site)
        {
            var key = (module, type);
            if (!_openSitesByType.TryGetValue(key, out var sites))
                _openSitesByType.Add(key, sites = new List<OpenSite>());
            if (!sites.Contains(site)) sites.Add(site);
        }

        foreach (var plan in materializedPlans)
        {
            var usedNames = new HashSet<string>(plan.EmittedNames, StringComparer.Ordinal)
            {
                OidCatalogName(plan.Module)
            };
            foreach (var site in plan.Open.Sites
                         .Where(site => site.Container.Module.Name == plan.Module.Name)
                         .GroupBy(static site => site.CatalogStem, StringComparer.Ordinal)
                         .Select(static group => group.First())
                         .OrderBy(static site => site.CatalogStem, StringComparer.Ordinal))
            {
                var baseName = site.CatalogStem + "_open_type_entries";
                var name = baseName;
                for (var suffix = 2; !usedNames.Add(name); suffix++)
                    name = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                _openCatalogEntryNames.Add((plan.Module.Name, site.CatalogStem), name);
            }

            foreach (var owner in _openSitesByType.Keys
                         .Where(key => key.Module == plan.Module.Name)
                         .OrderBy(static key => key.Type, StringComparer.Ordinal))
            {
                var sites = _openSitesByType[owner]
                    .Where(site => SupportsOpenView(owner, site))
                    .GroupBy(site => OpenViewUsageIdentity(owner, site), StringComparer.Ordinal)
                    .Select(static group => group.First())
                    .OrderBy(static site => site.Table, StringComparer.Ordinal)
                    .ThenBy(static site => site.CatalogStem, StringComparer.Ordinal)
                    .ThenBy(site => string.Join("\n", site.ContextPath), StringComparer.Ordinal)
                    .ToList();
                if (!plan.EmittedNames.Contains(owner.Type)) continue;

                var preferredPaths = sites.Select(site =>
                {
                    var table = CppNames.SanitizeFieldName(ToSnakeCaseIdentifier(
                        string.IsNullOrEmpty(site.Table) ? "local" : site.Table));
                    var field = CppNames.SanitizeFieldName(
                        ToSnakeCaseIdentifier(OpenStemName(site.Field)));
                    return (Site: site, Table: table, Field: field);
                }).ToList();
                var repeatedPaths = preferredPaths
                    .GroupBy(static item => item.Table + "\n" + item.Field, StringComparer.Ordinal)
                    .Where(static group => group.Count() > 1)
                    .Select(static group => group.Key)
                    .ToHashSet(StringComparer.Ordinal);
                var usedPaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var preferred in preferredPaths)
                {
                    var table = preferred.Table;
                    var field = preferred.Field;
                    if (repeatedPaths.Contains(table + "\n" + field))
                        table = CppNames.SanitizeFieldName(
                            table + "_" + OpenViewRouteStem(preferred.Site));
                    var path = table + "\n" + field;
                    var baseTable = table;
                    for (var suffix = 2; !usedPaths.Add(path); suffix++)
                    {
                        table = CppNames.SanitizeFieldName(
                            baseTable + "_" + suffix.ToString(CultureInfo.InvariantCulture));
                        path = table + "\n" + field;
                    }

                    _openViewPaths.Add(
                        (owner.Module, owner.Type, OpenViewUsageIdentity(owner, preferred.Site)),
                        (table, field));
                }

                var usedMembers = OpenViewReservedMembers(plan.Module, owner.Type, sites);
                foreach (var tableGroup in sites.GroupBy(
                             site => OpenViewPath(owner, site).Table,
                             StringComparer.Ordinal))
                {
                    var accessor = AllocateOpenViewMember(tableGroup.Key, usedMembers);
                    var hasFactory = tableGroup.Any(site => IsDirectOpenOwner(owner, site));
                    var factoryAccessor = hasFactory
                        ? AllocateOpenViewMember("make_" + accessor, usedMembers)
                        : null;
                    var viewType = AllocateOpenViewType(
                        owner.Type + "_" + accessor + "_open_view", usedNames);
                    _openViewNames.Add(
                        (owner.Module, owner.Type, tableGroup.Key),
                        new OpenViewNames(accessor, factoryAccessor, viewType));

                    if (tableGroup.Count() <= 1) continue;
                    foreach (var site in tableGroup)
                    {
                        var path = _openViewPaths[
                            (owner.Module, owner.Type, OpenViewUsageIdentity(owner, site))];
                        var fieldViewType = AllocateOpenViewType(
                            viewType + "_" + path.Field, usedNames);
                        _openFieldViewNames.Add(
                            (owner.Module, owner.Type, tableGroup.Key, path.Field),
                            new OpenFieldViewNames(fieldViewType));
                    }
                }
            }
        }
    }

    private static string AllocateOpenViewMember(string preferred, HashSet<string> used)
    {
        var name = preferred;
        if (used.Add(name)) return name;
        name = preferred + "_view";
        for (var suffix = 2; !used.Add(name); suffix++)
            name = preferred + "_view_" + suffix.ToString(CultureInfo.InvariantCulture);
        return name;
    }

    private static string AllocateOpenViewType(string preferred, HashSet<string> used)
    {
        var name = CppNames.SanitizeTypeName(preferred);
        var baseName = name;
        for (var suffix = 2; !used.Add(name); suffix++)
            name = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
        return name;
    }

    private HashSet<string> OpenViewReservedMembers(
        IrModule module, string owner, IReadOnlyList<OpenSite> sites)
    {
        var used = new HashSet<string>(StringComparer.Ordinal)
        {
            "open_binding", "try_decode", "set", "create",
            "match_tag", "decode", "encode"
        };
        foreach (var site in sites.Where(site => site.Container.TypeName == owner))
        foreach (var field in site.Container.Fields)
            used.Add(FieldName(field));
        var definition = module.Types.FirstOrDefault(def =>
            (IrOptions.CppTypeName(def.Options) ?? CppNames.SanitizeTypeName(def.Name)) == owner);
        if (definition is not null && OpenContainerFields(definition.Type) is { } fields)
        foreach (var field in fields)
            used.Add(FieldName(field));
        return used;
    }

    private static bool IsDirectOpenOwner((string Module, string Type) owner, OpenSite site) =>
        owner.Module == site.Container.Module.Name &&
        owner.Type == site.Container.TypeName &&
        site.Selector.Levels == 0;

    private static bool SupportsOpenView((string Module, string Type) owner, OpenSite site)
    {
        if (IsDirectOpenOwner(owner, site)) return true;
        if (owner.Module != site.ContextModule.Name || owner.Type != site.ContextOwner ||
            site.CrossesInlineBoundary)
            return false;
        var openFieldIndex = -1;
        for (var i = site.ContextPath.Count - 1; i >= 0; i--)
        {
            if (site.ContextPath[i] != site.Field.Name) continue;
            openFieldIndex = i;
            break;
        }
        return openFieldIndex >= 0 && site.ContextPath.Take(openFieldIndex)
            .Count(static part => part is not "[]" and not "containing") == 1;
    }

    private static string OpenViewUsageIdentity(
        (string Module, string Type) owner, OpenSite site) =>
        IsDirectOpenOwner(owner, site)
            ? "direct\n" + site.CatalogStem
            : string.Join("\n", new[]
        {
            site.CatalogStem,
            site.Container.Module.Name,
            site.Container.TypeName,
            site.Field.Name,
            site.ContextModule.Name,
            site.ContextOwner,
            string.Join("/", site.ContextPath),
            site.Selector.Levels.ToString(CultureInfo.InvariantCulture)
        });

    private static string OpenViewRouteStem(OpenSite site)
    {
        var openFieldIndex = -1;
        for (var i = site.ContextPath.Count - 1; i >= 0; i--)
        {
            if (site.ContextPath[i] != site.Field.Name) continue;
            openFieldIndex = i;
            break;
        }

        var parts = (openFieldIndex < 0
                ? site.ContextPath
                : site.ContextPath.Take(openFieldIndex))
            .Where(static part => part is not "[]" and not "containing")
            .Select(ToSnakeCaseIdentifier)
            .ToList();
        if (parts.Count == 0)
            parts.Add(ToSnakeCaseIdentifier(site.Container.TypeName));
        return string.Join("_", parts);
    }

    private (string Table, string Field) OpenViewPath(
        (string Module, string Type) owner, OpenSite site) =>
        _openViewPaths[(owner.Module, owner.Type, OpenViewUsageIdentity(owner, site))];

    private static void ResetOpenTypeState()
    {
    }

    private OpenPlan PlanDirectOpenTypes(
        IrDocument document,
        IrModule module,
        IReadOnlyList<(string Name, TypeExpr Type)> nested,
        HashSet<string> emittedNames)
    {
        var plan = new OpenPlan();
        var shared = new Dictionary<string, OpenBinding>(StringComparer.Ordinal);
        var incomingRefs = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(emittedNames, StringComparer.Ordinal) { OidCatalogName(module) };
        _ = nested;

        foreach (var definition in module.Types.Where(static t => IrOptions.ShouldGenerate(t.Options)))
        {
            if (IrOptions.CppAliasOf(definition.Options) is not null) continue;
            if (IsCollapsibleAlias(definition.Type)) continue;
            var typeName = IrOptions.CppTypeName(definition.Options)
                ?? CppNames.SanitizeTypeName(definition.Name);
            WalkDefinitionScoped(document, module, typeName, definition.Type,
                Array.Empty<OpenContainer>(), typeName, Array.Empty<string>(), plan, shared);
        }

        foreach (var other in document.Modules)
        {
            foreach (var definition in other.Types)
            {
                var owner = IrOptions.CppTypeName(definition.Options)
                    ?? CppNames.SanitizeTypeName(definition.Name);
                ScanIncoming(definition.Type, other, owner, Array.Empty<string>(), false);
            }
            foreach (var definition in other.Values)
                ScanIncoming(definition.Type, other, CppNames.SanitizeTypeName(definition.Name), Array.Empty<string>(), false);
        }

        AssignOpenStems(plan, names);
        return plan;

        void ScanIncoming(TypeExpr expression, IrModule contextModule, string contextOwner,
            IReadOnlyList<string> route, bool crossesInlineBoundary)
        {
            switch (expression)
            {
                case RefType reference:
                    if (reference.OpenTypes is { Count: > 0 })
                    {
                        var found = FindWithModule(document, contextModule, reference);
                        if (found is not null &&
                            found.Value.Module == module &&
                            IrOptions.ShouldGenerate(found.Value.Def.Options))
                        {
                            foreach (var use in reference.OpenTypes)
                                ResolveUse(document, contextModule, found.Value, use, contextOwner,
                                    route.Concat(use.Path).ToList(), crossesInlineBoundary,
                                    plan, shared, names);
                        }
                    }

                    if (reference.OpenTypes is not null)
                    {
                        foreach (var use in reference.OpenTypes)
                        foreach (var binding in use.Bindings)
                            ScanIncoming(binding.Type, contextModule, contextOwner, route, crossesInlineBoundary);
                    }

                    if (FindWithModule(document, contextModule, reference) is { } referenced &&
                        (referenced.Def.Type is RefType or SequenceOfType or SetOfType ||
                         IsSingleAlternativeChoice(referenced.Def.Type)))
                    {
                        var refKey = referenced.Module.Name + "\n" + referenced.Def.Name + "\n" +
                                     contextOwner + "\n" + (route.Count > 0 ? route[0] : "");
                        if (incomingRefs.Add(refKey))
                            ScanIncoming(referenced.Def.Type, referenced.Module, contextOwner, route,
                                crossesInlineBoundary);
                    }

                    break;
                case AnyType { Bindings: { } bodyBindings }:
                    foreach (var binding in bodyBindings)
                        ScanIncoming(binding.Type, contextModule, contextOwner, route, crossesInlineBoundary);
                    break;
                case SequenceType sequence:
                    foreach (var field in sequence.Components)
                        ScanIncoming(field.Type, contextModule, contextOwner,
                            route.Append(field.Name).ToList(),
                            crossesInlineBoundary || CrossesInlineOwnerBoundary(field.Type));
                    break;
                case SetType set:
                    foreach (var field in set.Components)
                        ScanIncoming(field.Type, contextModule, contextOwner,
                            route.Append(field.Name).ToList(),
                            crossesInlineBoundary || CrossesInlineOwnerBoundary(field.Type));
                    break;
                case ChoiceType choice:
                    if (choice.Components.Count == 1)
                    {
                        var alternative = choice.Components[0];
                        ScanIncoming(alternative.Type, contextModule, contextOwner,
                            route, crossesInlineBoundary || alternative.Type is not RefType);
                    }
                    else
                    {
                        foreach (var alternative in choice.Components)
                            ScanIncoming(alternative.Type, contextModule, contextOwner,
                                route.Append(alternative.Name).ToList(),
                                crossesInlineBoundary || alternative.Type is not RefType);
                    }
                    break;
                case SequenceOfType of:
                    ScanIncoming(of.Element, contextModule, contextOwner, route.Append("[]").ToList(),
                        crossesInlineBoundary || of.Element is not RefType);
                    break;
                case SetOfType of:
                    ScanIncoming(of.Element, contextModule, contextOwner, route.Append("[]").ToList(),
                        crossesInlineBoundary || of.Element is not RefType);
                    break;
                case OctetStringType { Containing: { } containing }:
                    ScanIncoming(containing, contextModule, contextOwner, route.Append("containing").ToList(),
                        crossesInlineBoundary || containing is not RefType);
                    break;
                case BitStringType { Containing: { } containing }:
                    ScanIncoming(containing, contextModule, contextOwner, route.Append("containing").ToList(),
                        crossesInlineBoundary || containing is not RefType);
                    break;
            }
        }

        static bool CrossesInlineOwnerBoundary(TypeExpr type) => type switch
        {
            RefType => false,
            SequenceOfType sequence => CrossesInlineOwnerBoundary(sequence.Element),
            SetOfType set => CrossesInlineOwnerBoundary(set.Element),
            SequenceType or SetType or ChoiceType => true,
            _ => false
        };
    }

    private void WalkDefinitionScoped(
        IrDocument document,
        IrModule module,
        string owner,
        TypeExpr type,
        IReadOnlyList<OpenContainer> parents,
        string contextOwner,
        IReadOnlyList<string> contextPath,
        OpenPlan plan,
        Dictionary<string, OpenBinding> shared)
    {
        type = UnwrapAliases(document, module, type);
        if (IsSingleAlternativeChoice(type))
        {
            WalkDefinitionScoped(
                document, module, owner, ((ChoiceType)type).Components[0].Type, parents,
                contextOwner, contextPath, plan, shared);
            return;
        }

        if (type is SequenceOfType or SetOfType)
        {
            var element = type is SetOfType setOf ? setOf.Element : ((SequenceOfType)type).Element;
            WalkDefinitionScoped(document, module, owner + "_Item", element, parents,
                contextOwner, contextPath.Append("[]").ToList(), plan, shared);
            return;
        }

        if (OpenContainerFields(type) is not { } fields) return;

        var container = new OpenContainer(module, owner, owner, fields);
        foreach (var field in fields)
        {
            TryRegisterDefinitionScoped(
                document, module, container, field, parents, contextOwner,
                contextPath.Append(field.Name).ToList(), plan, shared);
            var fieldType = UnwrapAliases(document, module, field.Type);
            if (fieldType is RefType) continue;
            if (fieldType is SequenceOfType or SetOfType or SequenceType or SetType or ChoiceType)
            {
                var childParents = parents.Append(container).ToList();
                var childOwner = owner + "_" + CppNames.SanitizeTypeName(field.Name);
                WalkDefinitionScoped(
                    document, module, childOwner, fieldType, childParents, contextOwner,
                    contextPath.Append(field.Name).ToList(), plan, shared);
            }
        }
    }

    private void TryRegisterDefinitionScoped(
        IrDocument document,
        IrModule module,
        OpenContainer container,
        IrComponent field,
        IReadOnlyList<OpenContainer> outerParents,
        string contextOwner,
        IReadOnlyList<string> contextPath,
        OpenPlan plan,
        Dictionary<string, OpenBinding> shared)
    {
        if (OpenElement(document, module, field.Type) is not AnyType
            {
                Selector: { } selector,
                Bindings.Count: > 0,
                TableExtensible: not null
            } definitionAny)
        {
            return;
        }

        if (selector.Levels > outerParents.Count) return;
        if (selector.Path.Count == 0)
        {
            throw new NotSupportedException(
                "C++ open-type Binding requires a non-empty selector path.");
        }

        var parents = outerParents.Reverse().Take(selector.Levels).ToList();
        var payload = BuildOpenPayload(document, module, field.Type);
        AddSite(document, module, container, field, payload, selector, parents, definitionAny.Bindings!,
            contextOwner, contextPath, false, definitionAny.Table, new[] { field.Name }, module, plan, shared);
    }

    private void ResolveUse(
        IrDocument document,
        IrModule contextModule,
        (IrModule Module, IrTypeDef Def) target,
        IrOpenTypeUse use,
        string contextOwner,
        IReadOnlyList<string> contextPath,
        bool crossesInlineBoundary,
        OpenPlan plan,
        Dictionary<string, OpenBinding> shared,
        HashSet<string> names)
    {
        if (use.Bindings.Count == 0) return;
        var namingOwner = IrOptions.CppTypeName(target.Def.Options)
            ?? CppNames.SanitizeTypeName(target.Def.Name);
        var expression = target.Def.Type;
        var module = target.Module;
        var hint = "";
        OpenContainer? container = null;
        IrComponent? openField = null;
        OpenPayload? payload = null;
        var pathIndex = 0;
        while (true)
        {
            if (expression is RefType reference)
            {
                var found = FindWithModule(document, module, reference);
                if (found is null || !IrOptions.ShouldGenerate(found.Value.Def.Options)) return;
                namingOwner = IrOptions.CppTypeName(found.Value.Def.Options)
                    ?? CppNames.SanitizeTypeName(found.Value.Def.Name);
                module = found.Value.Module;
                expression = found.Value.Def.Type;
                hint = "";
                continue;
            }

            if (IsSingleAlternativeChoice(expression))
            {
                expression = ((ChoiceType)expression).Components[0].Type;
                continue;
            }

            if (pathIndex < use.Path.Count && use.Path[pathIndex] == "[]")
            {
                if (expression is not (SequenceOfType or SetOfType)) return;
                pathIndex++;
                expression = expression is SetOfType setOf ? setOf.Element : ((SequenceOfType)expression).Element;
                if (openField is not null)
                    payload = new OpenPayload(openField.Type, OpenPayloadKind.Many);
                continue;
            }

            if (pathIndex < use.Path.Count && use.Path[pathIndex] == "containing")
            {
                if (ContainedType(expression) is not { } content) return;
                pathIndex++;
                expression = content;
                if (openField is not null)
                    payload = new OpenPayload(openField.Type, OpenPayloadKind.Contained);
                continue;
            }

            if (OpenContainerFields(expression) is { } fields)
            {
                if (pathIndex >= use.Path.Count) return;
                var owner = hint.Length == 0
                    ? namingOwner
                    : namingOwner + "_" + CppNames.SanitizeTypeName(hint);
                container = new OpenContainer(module, owner, owner, fields);
                var part = use.Path[pathIndex++];
                if (part is "[]" or "containing") return;
                openField = fields.Single(f => f.Name == part);
                payload = BuildOpenPayload(document, module, openField.Type);
                expression = openField.Type;
                namingOwner = owner;
                hint = openField.Name;
                continue;
            }

            if (expression is SequenceOfType or SetOfType)
            {
                // Path must explicitly step with "[]".
                return;
            }

            if (ContainedType(expression) is not null)
            {
                // Path must explicitly step with "containing".
                return;
            }

            if (expression is not AnyType any || any.Bindings is { Count: > 0 } ||
                container is null || openField is null || payload is null ||
                pathIndex != use.Path.Count || any.Selector is not { Levels: 0 } selector)
            {
                return;
            }

            AddSite(document, target.Module, container, openField, payload, selector,
                Array.Empty<OpenContainer>(), use.Bindings,
                contextOwner, contextPath, crossesInlineBoundary,
                use.Table, use.Path, contextModule, plan, shared);
            return;
        }
    }

    private void AddSite(
        IrDocument document,
        IrModule emittingModule,
        OpenContainer container,
        IrComponent openField,
        OpenPayload payload,
        IrOpenTypeSelector selector,
        IReadOnlyList<OpenContainer> parents,
        IReadOnlyList<IrOpenTypeBinding> bindings,
        string contextOwner,
        IReadOnlyList<string> contextPath,
        bool crossesInlineBoundary,
        string? table,
        IReadOnlyList<string> path,
        IrModule bindingModule,
        OpenPlan plan,
        Dictionary<string, OpenBinding> shared)
    {
        if (container.Module != emittingModule) return;
        var site = new OpenSite(
            container, openField, payload, selector, parents.ToList(), bindingModule, contextOwner,
            contextPath.ToList(), crossesInlineBoundary, table, path.ToList(),
            new List<OpenBinding>());
        foreach (var binding in bindings)
        {
            var qualified = new IrOpenTypeBinding
            {
                Key = binding.Key,
                Name = binding.Name,
                Type = QualifyOpenTypeReferences(document, bindingModule, emittingModule, binding.Type)
            };
            var identity = container.TypeName + "\n" + openField.Name + "\n" +
                string.Join(";", parents.Select(static p => p.TypeName)) + "\n" +
                JsonSerializer.Serialize(payload.Type, IrSerializer.JsonOptions) + "\n" +
                JsonSerializer.Serialize(qualified.Type, IrSerializer.JsonOptions) + "\n" + binding.Key + "\n" +
                JsonSerializer.Serialize(selector, IrSerializer.JsonOptions);
            if (!shared.TryGetValue(identity, out var wrapper))
            {
                // Member names are unique within a catalog namespace (EmitOpenCatalog), not globally.
                wrapper = new OpenBinding(OpenBindingMemberName(binding), qualified, site);
                shared.Add(identity, wrapper);
                plan.Bindings.Add(wrapper);
            }

            site.Bindings.Add(wrapper);
        }

        if (site.Bindings.Count > 0)
            plan.Sites.Add(site);
    }

    private static void AssignOpenStems(OpenPlan plan, HashSet<string> names)
    {
        var carrierNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in plan.Sites.GroupBy(
                     s => s.Container.Module.Name + "\n" + s.Container.TypeName + "\n" + s.Field.Name + "\n" +
                          JsonSerializer.Serialize(s.Selector, IrSerializer.JsonOptions),
                     StringComparer.Ordinal))
        {
            var site = group.First();
            var stemBase = OpenStemName(site.Field);
            var stem = stemBase;
            var bindingName = stem + "_binding";
            if (!carrierNames.Add(bindingName))
            {
                var disambiguated = CppNames.SanitizeFieldName(site.Container.TypeName) + "_" + stemBase;
                stem = disambiguated;
                bindingName = stem + "_binding";
                for (var suffix = 2; !carrierNames.Add(bindingName); suffix++)
                {
                    stem = disambiguated + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                    bindingName = stem + "_binding";
                }
            }

            foreach (var item in group)
                item.BindingStem = stem;
        }

        var byTable = new Dictionary<string, List<OpenSite>>(StringComparer.Ordinal);
        foreach (var site in plan.Sites)
        {
            var identity = OpenTableIdentity(site);
            if (!byTable.TryGetValue(identity, out var list))
                byTable.Add(identity, list = new List<OpenSite>());
            list.Add(site);
        }

        foreach (var sites in byTable.Values)
        {
            var preferred = sites
                .OrderBy(static s => PreferredOpenTableStem(s).Length)
                .ThenBy(static s => PreferredOpenTableStem(s), StringComparer.Ordinal)
                .First();
            var stemBase = PreferredOpenTableStem(preferred);
            var stem = stemBase;
            var catalogName = stem + "_open_type_entries";
            if (!names.Add(catalogName))
            {
                var disambiguated = stemBase + "_" + CppNames.SanitizeFieldName(preferred.Container.TypeName);
                stem = disambiguated;
                catalogName = stem + "_open_type_entries";
                for (var suffix = 2; !names.Add(catalogName); suffix++)
                {
                    stem = disambiguated + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                    catalogName = stem + "_open_type_entries";
                }
            }

            foreach (var site in sites)
                site.CatalogStem = stem;
        }

    }

    private static string PreferredOpenTableStem(OpenSite site)
    {
        var openField = OpenStemName(site.Field);
        if (!string.IsNullOrEmpty(site.Table))
        {
            var table = CppNames.SanitizeFieldName(site.Table);
            return site.Path.Any(static part => part == "[]") ? table : table + "_" + openField;
        }

        return CppNames.SanitizeFieldName(site.Container.TypeName) + "_" + openField;
    }

    /// <summary>
    /// Stem for binding/catalog names. Field identifiers may end with '_' (C++ keywords);
    /// strip that before appending the generated open-type suffix.
    /// </summary>
    private static string OpenStemName(IrComponent field)
    {
        var name = FieldName(field);
        return name.EndsWith('_') ? name[..^1] : name;
    }

    private static string OpenTableIdentity(OpenSite site)
    {
        var bindings = string.Join("\n", site.Bindings.Select(static binding =>
            binding.Binding.Key + "\t" + (binding.Binding.Name ?? "") + "\t" +
            JsonSerializer.Serialize(binding.Binding.Type, IrSerializer.JsonOptions)));
        return site.Container.TypeName + "\n" + site.Field.Name + "\n" +
               JsonSerializer.Serialize(site.Selector, IrSerializer.JsonOptions) + "\n" +
               JsonSerializer.Serialize(site.Payload.Type, IrSerializer.JsonOptions) + "\n" +
               bindings;
    }

    private static string OpenBindingMemberName(IrOpenTypeBinding binding)
    {
        var semantic = binding.Name;
        if (semantic is null)
        {
            semantic = binding.Type is RefType reference
                ? reference.Name
                : "oid_" + binding.Key.Replace('.', '_');
        }

        foreach (var leading in new[] { "ext-", "aa-", "at-" })
        {
            if (!semantic.StartsWith(leading, StringComparison.Ordinal)) continue;
            semantic = semantic[leading.Length..];
            break;
        }

        return CppNames.SanitizeFieldName(ToSnakeCaseIdentifier(semantic));
    }

    private static string ToSnakeCaseIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return "field";
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '-' or ' ' or '_')
            {
                if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
                continue;
            }

            if (char.IsUpper(c))
            {
                var previous = i > 0 ? name[i - 1] : '\0';
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                var startsWord = char.IsLower(previous) || char.IsDigit(previous) ||
                                 char.IsUpper(previous) && char.IsLower(next);
                if (startsWord && sb.Length > 0 && sb[^1] != '_') sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
                continue;
            }

            sb.Append(c);
        }

        return sb.Length == 0 ? "field" : sb.ToString();
    }

    private static IReadOnlyList<IrComponent>? OpenContainerFields(TypeExpr expression) => expression switch
    {
        SequenceType sequence => sequence.Components,
        SetType set => set.Components,
        _ => null
    };

    private OpenPayload BuildOpenPayload(IrDocument document, IrModule module, TypeExpr original)
    {
        var type = UnwrapAliases(document, module, original);
        if (type is SequenceOfType or SetOfType)
            return new OpenPayload(original, OpenPayloadKind.Many);
        if (ContainedType(type) is not null)
            return new OpenPayload(original, OpenPayloadKind.Contained);
        return new OpenPayload(original, OpenPayloadKind.Direct);
    }

    private TypeExpr? OpenElement(IrDocument document, IrModule module, TypeExpr type)
    {
        type = UnwrapAliases(document, module, type);
        while (true)
        {
            TypeExpr? inner = type switch
            {
                SequenceOfType of => of.Element,
                SetOfType setOf => setOf.Element,
                _ => ContainedType(type)
            };
            if (inner is null)
                return type is AnyType any ? any : null;
            type = UnwrapAliases(document, module, inner);
        }
    }

    /// <summary>
    /// C++ type for an open-type binding value. String/time forms are part of the type
    /// (<c>asn_string&lt;form&gt;</c> / <c>asn_time&lt;form&gt;</c>) so wire rules are compile-time.
    /// </summary>
    private string OpenBindingCppType(
        IrDocument document, IrModule module, string owner, string hint, TypeExpr bindingType)
    {
        var type = UnwrapAliases(document, module, bindingType);
        if (type.Tag is not null)
            return CppType(document, module, owner, hint, bindingType, optional: false);

        return type switch
        {
            StringType text => text.Form switch
            {
                StringTypes.Utf8 => "asn1kit::asn_string<asn1kit::string_form::utf8>",
                StringTypes.Printable => "asn1kit::asn_string<asn1kit::string_form::printable>",
                StringTypes.Teletex or StringTypes.T61 =>
                    "asn1kit::asn_string<asn1kit::string_form::teletex>",
                StringTypes.Ia5 => "asn1kit::asn_string<asn1kit::string_form::ia5>",
                StringTypes.Numeric => "asn1kit::asn_string<asn1kit::string_form::numeric>",
                StringTypes.Visible => "asn1kit::asn_string<asn1kit::string_form::visible>",
                StringTypes.Bmp => "asn1kit::asn_string<asn1kit::string_form::bmp>",
                StringTypes.Universal => "asn1kit::asn_string<asn1kit::string_form::universal>",
                StringTypes.General => "asn1kit::asn_string<asn1kit::string_form::general>",
                StringTypes.Graphic => "asn1kit::asn_string<asn1kit::string_form::graphic>",
                StringTypes.Videotex => "asn1kit::asn_string<asn1kit::string_form::videotex>",
                _ => throw new NotSupportedException($"Unknown stringType '{text.Form}'.")
            },
            TimeType time => time.Form switch
            {
                TimeTypes.Utc => "asn1kit::asn_time<asn1kit::time_form::utc>",
                TimeTypes.Generalized => time.FractionDigits is null or 3
                    ? "asn1kit::asn_time<asn1kit::time_form::generalized>"
                    : $"asn1kit::asn_time<asn1kit::time_form::generalized, {time.FractionDigits.Value.ToString(CultureInfo.InvariantCulture)}>",
                _ => throw new NotSupportedException($"Unknown timeType '{time.Form}'.")
            },
            _ => CppType(document, module, owner, hint, bindingType, optional: false)
        };
    }

    private void EmitOpenTypes(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        OpenPlan plan)
    {
        _ = plan;
        var catalogSites = _openSitesByType
            .Where(pair => pair.Key.Module == module.Name)
            .SelectMany(static pair => pair.Value)
            .Where(site => site.Container.Module.Name == module.Name)
            .GroupBy(static site => site.CatalogStem, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static site => site.CatalogStem, StringComparer.Ordinal)
            .ToList();
        if (catalogSites.Count > 0)
        {
            sb.AppendLine("namespace detail {");
            sb.AppendLine();
            foreach (var site in catalogSites)
                EmitOpenCatalogEntries(sb, document, module, site);
            sb.AppendLine("} // namespace detail");
            sb.AppendLine();
        }

        var viewOwners = _openViewNames.Keys
            .Where(key => key.Module == module.Name)
            .Select(static key => key.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToList();
        if (viewOwners.Count == 0) return;

        sb.AppendLine("#ifdef ASN1KIT_DETAIL_OPEN_VIEW_ENTRY");
        sb.AppendLine("#error \"ASN1KIT_DETAIL_OPEN_VIEW_ENTRY is reserved for generated headers\"");
        sb.AppendLine("#endif");
        sb.AppendLine("#define ASN1KIT_DETAIL_OPEN_VIEW_ENTRY(Name, Binding) \\");
        sb.AppendLine("    template <typename... Args> \\");
        sb.AppendLine("    decltype(auto) Name(Args&&... args) const \\");
        sb.AppendLine("        requires requires(const base& view, Args&&... values) { \\");
        sb.AppendLine("            view.template invoke<open_entries::Binding>(std::forward<Args>(values)...); \\");
        sb.AppendLine("        } \\");
        sb.AppendLine("    { return view_.template invoke<open_entries::Binding>(std::forward<Args>(args)...); }");
        sb.AppendLine();
        foreach (var owner in viewOwners)
            EmitOpenViews(sb, module, owner);
        sb.AppendLine("#undef ASN1KIT_DETAIL_OPEN_VIEW_ENTRY");
        sb.AppendLine();
        foreach (var owner in viewOwners)
            EmitOpenViewAccessorDefinitions(sb, module, owner);
    }

    private IEnumerable<string> OpenCatalogEntryNames(IrModule module, string typeName) =>
        _openSitesByType.TryGetValue((module.Name, typeName), out var sites)
            ? sites.Where(site => site.Container.Module.Name == module.Name &&
                                  site.Container.TypeName == typeName)
                .Select(site => _openCatalogEntryNames[(module.Name, site.CatalogStem)])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    private IEnumerable<(string Table, OpenViewNames Names)> OpenViews(
        IrModule module, string typeName) =>
        _openViewNames
            .Where(pair => pair.Key.Module == module.Name && pair.Key.Type == typeName)
            .OrderBy(static pair => pair.Key.Table, StringComparer.Ordinal)
            .Select(static pair => (pair.Key.Table, pair.Value));

    private void EmitOpenViewForwardDeclarations(
        StringBuilder sb, IrModule module, string typeName)
    {
        foreach (var (_, names) in OpenViews(module, typeName))
        {
            sb.AppendLine("template <typename Target>");
            sb.AppendLine($"struct {names.ViewType};");
        }
    }

    private void EmitOpenViewAccessors(StringBuilder sb, IrModule module, string typeName)
    {
        foreach (var (_, names) in OpenViews(module, typeName))
        {
            sb.AppendLine(
                $"    [[nodiscard]] {names.ViewType}<{typeName}> {names.Accessor}() &;");
            sb.AppendLine(
                $"    [[nodiscard]] {names.ViewType}<const {typeName}> {names.Accessor}() const &;");
            sb.AppendLine(
                $"    [[nodiscard]] {names.ViewType}<asn1kit::open_owned_target<{typeName}>> {names.Accessor}() &&;");
            sb.AppendLine($"    {names.ViewType}<const {typeName}> {names.Accessor}() const && = delete;");
            if (names.FactoryAccessor is not null)
            {
                sb.AppendLine(
                    $"    [[nodiscard]] static {names.ViewType}<asn1kit::open_factory_target<{typeName}>> {names.FactoryAccessor}();");
            }
            sb.AppendLine();
        }
    }

    private void EmitOpenViewAccessorDefinitions(StringBuilder sb, IrModule module, string typeName)
    {
        foreach (var (_, names) in OpenViews(module, typeName))
        {
            sb.AppendLine(
                $"inline {names.ViewType}<{typeName}> {typeName}::{names.Accessor}() & {{");
            sb.AppendLine($"    return {names.ViewType}<{typeName}>{{*this}};");
            sb.AppendLine("}");
            sb.AppendLine(
                $"inline {names.ViewType}<const {typeName}> {typeName}::{names.Accessor}() const & {{");
            sb.AppendLine($"    return {names.ViewType}<const {typeName}>{{*this}};");
            sb.AppendLine("}");
            sb.AppendLine(
                $"inline {names.ViewType}<asn1kit::open_owned_target<{typeName}>> {typeName}::{names.Accessor}() && {{");
            sb.AppendLine(
                $"    return {names.ViewType}<asn1kit::open_owned_target<{typeName}>>{{std::move(*this)}};");
            sb.AppendLine("}");
            if (names.FactoryAccessor is not null)
            {
                sb.AppendLine(
                    $"inline {names.ViewType}<asn1kit::open_factory_target<{typeName}>> {typeName}::{names.FactoryAccessor}() {{");
                sb.AppendLine(
                    $"    return {names.ViewType}<asn1kit::open_factory_target<{typeName}>>{{}};");
                sb.AppendLine("}");
            }
            sb.AppendLine();
        }
    }

    private void EmitOpenOwnerMethods(
        StringBuilder sb, IrDocument document, IrModule module, string typeName)
    {
        if (!_openSitesByType.TryGetValue((module.Name, typeName), out var allSites)) return;

        var carrierSites = allSites
            .Where(site => site.Container.TypeName == typeName && site.Selector.Levels == 0)
            .GroupBy(site => site.Field.Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
        var shortCarrierNames = carrierSites.Count == 1;
        foreach (var site in carrierSites)
            EmitCarrierOpenMethods(sb, document, site, shortCarrierNames);

        foreach (var site in allSites
                     .Where(site => site.ContextOwner == typeName && site.ContextOwner != site.Container.TypeName)
                     .GroupBy(site => site.CatalogStem + "\n" + string.Join("/", site.ContextPath),
                         StringComparer.Ordinal)
                     .Select(static group => group.First()))
        {
            EmitContextualOpenMethod(sb, document, module, site);
        }
    }

    private void EmitCarrierOpenMethods(
        StringBuilder sb, IrDocument document, OpenSite site, bool shortNames)
    {
        var field = FieldName(site.Field);
        var keyExpr = OpenKeyExpression(document, site);
        var keyCppType = OpenKeyCppType(document, site);
        var optional = site.Field.Optional;
        var suffix = shortNames ? "" : "_" + OpenStemName(site.Field);
        var valueType = site.Payload.Kind == OpenPayloadKind.Many ? "std::vector<T>" : "T";
        var bindingType = $"asn1kit::open_binding<Context, {keyCppType}, {valueType}>";
        var defaultBindingType =
            $"asn1kit::open_binding<asn1kit::default_open_context, {keyCppType}, {valueType}>";
        var decodeCall = site.Payload.Kind switch
        {
            OpenPayloadKind.Contained => "asn1kit::decode_contained<T>",
            OpenPayloadKind.Many => "asn1kit::decode_each<T>",
            _ => "asn1kit::decode_open<T>"
        };
        var containedType = site.Payload.Kind == OpenPayloadKind.Contained
            ? UnwrapAliases(document, site.Container.Module, site.Payload.Type)
            : null;
        var encodeCall = site.Payload.Kind switch
        {
            OpenPayloadKind.Contained when containedType is BitStringType =>
                "asn1kit::encode_contained_bits",
            OpenPayloadKind.Contained => "asn1kit::encode_contained_octets",
            OpenPayloadKind.Many => "asn1kit::encode_each",
            _ => "asn1kit::encode_open"
        };

        sb.AppendLine("    template <typename T>");
        sb.AppendLine($"    [[nodiscard]] static {defaultBindingType} open_binding{suffix}({keyCppType} key) {{");
        sb.AppendLine("        return {std::move(key)};");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    template <typename Context, typename T>");
        sb.AppendLine($"    [[nodiscard]] std::optional<{valueType}> try_decode{suffix}(");
        sb.AppendLine($"        const {bindingType}& binding) const {{");
        if (optional)
            sb.AppendLine($"        if (!{field}.has_value()) return std::nullopt;");
        sb.AppendLine($"        if (!({keyExpr.Replace("src.", "this->", StringComparison.Ordinal)} == binding.key)) return std::nullopt;");
        var raw = optional ? $"*{field}" : field;
        sb.AppendLine($"        return {decodeCall}({raw});");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    template <typename Context, typename T>");
        sb.AppendLine($"    void set{suffix}(const {bindingType}& binding, const {valueType}& value) {{");
        sb.AppendLine($"        {OpenKeyAssignmentTarget(document, site).Replace("src.", "this->", StringComparison.Ordinal)} = binding.key;");
        sb.AppendLine($"        {field} = {encodeCall}(value);");
        sb.AppendLine("    }");
        if (site.Payload.Kind == OpenPayloadKind.Many)
        {
            sb.AppendLine();
            sb.AppendLine("    template <typename Context, typename T>");
            sb.AppendLine($"    void set{suffix}(const {bindingType}& binding, const T& value) {{");
            sb.AppendLine($"        set{suffix}(binding, std::vector<T>{{value}});");
            sb.AppendLine("    }");
        }
        sb.AppendLine();
        sb.AppendLine("    template <typename Context, typename T>");
        sb.AppendLine($"    [[nodiscard]] static {site.Container.TypeName} create{suffix}(");
        sb.AppendLine($"        const {bindingType}& binding, const {valueType}& value) {{");
        sb.AppendLine($"        {site.Container.TypeName} result{{}};");
        sb.AppendLine($"        result.set{suffix}(binding, value);");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        if (site.Payload.Kind == OpenPayloadKind.Many)
        {
            sb.AppendLine();
            sb.AppendLine("    template <typename Context, typename T>");
            sb.AppendLine($"    [[nodiscard]] static {site.Container.TypeName} create{suffix}(");
            sb.AppendLine($"        const {bindingType}& binding, const T& value) {{");
            sb.AppendLine($"        return create{suffix}(binding, std::vector<T>{{value}});");
            sb.AppendLine("    }");
        }
        sb.AppendLine();
    }

    private void EmitContextualOpenMethod(
        StringBuilder sb, IrDocument document, IrModule module, OpenSite site)
    {
        if (site.CrossesInlineBoundary) return;
        var openFieldIndex = -1;
        for (var i = site.ContextPath.Count - 1; i >= 0; i--)
        {
            if (site.ContextPath[i] == site.Field.Name)
            {
                openFieldIndex = i;
                break;
            }
        }
        if (openFieldIndex < 0) return;

        var carrierPath = site.ContextPath.Take(openFieldIndex).ToList();
        if (carrierPath.Count == 0) return;
        if (carrierPath.Count(static part => part is not "[]" and not "containing") != 1) return;

        var routeStem = carrierPath.First(static part => part is not "[]" and not "containing");
        var method = "try_decode_" + ToSnakeCaseIdentifier(routeStem);
        var keyType = OpenKeyCppType(document, site);
        var valueType = site.Payload.Kind == OpenPayloadKind.Many ? "std::vector<T>" : "T";
        var contextName = site.Container.Module.Name + "." + site.CatalogStem;
        var bindingType = "asn1kit::open_binding<asn1kit::open_context<\"" +
                          EscapeCppString(contextName) + "\">, " + keyType + ", " + valueType + ">";

        sb.AppendLine("    template <typename T>");
        sb.AppendLine($"    [[nodiscard]] std::optional<{valueType}> {method}(");
        sb.AppendLine($"        const {bindingType}& binding) const {{");
        if (site.Selector.Levels > 0)
        {
            sb.AppendLine($"        std::optional<{valueType}> result;");
            var contextualKey = OpenKeyAccessExpression(document, site, "(*this)");
            sb.AppendLine($"        if (!({contextualKey} == binding.key)) return result;");
        }

        var definition = module.Types.FirstOrDefault(def =>
            (IrOptions.CppTypeName(def.Options) ?? CppNames.SanitizeTypeName(def.Name)) == site.ContextOwner);
        if (definition is null)
        {
            sb.AppendLine(site.Selector.Levels > 0
                ? "        return result;"
                : "        return std::nullopt;");
            sb.AppendLine("    }");
            sb.AppendLine();
            return;
        }

        if (site.Selector.Levels == 0 &&
            OpenContainerFields(definition.Type) is { } ownerFields &&
            ownerFields.SingleOrDefault(field => field.Name == carrierPath[0]) is { } ownerField)
        {
            var source = "(*this)." + FieldName(ownerField);
            var optionalOwnerField = (ownerField.Optional || ownerField.ExtensionAddition == true) &&
                                     ownerField.Default is null;
            if (optionalOwnerField)
            {
                sb.AppendLine($"        if (!{source}.has_value()) return std::nullopt;");
                source = "(*" + source + ")";
            }
            if (ShouldEmitLazy(document, module, ownerField.Type, ownerField.Options) ||
                ShouldEmitRetainEncoded(document, module, ownerField.Type, ownerField.Options))
                source += ".get()";
            sb.AppendLine($"        return asn1kit::try_decode_unique({source}, binding);");
            sb.AppendLine("    }");
            sb.AppendLine();
            return;
        }

        if (site.Selector.Levels == 0)
            sb.AppendLine($"        std::optional<{valueType}> result;");

        var uniqueCarrierFields = _openSitesByType.TryGetValue(
            (site.Container.Module.Name, site.Container.TypeName), out var carrierSites)
            ? carrierSites.Where(s => s.Container.TypeName == site.Container.TypeName && s.Selector.Levels == 0)
                .Select(s => s.Field.Name).Distinct(StringComparer.Ordinal).Count()
            : 1;
        var carrierMethod = "try_decode" +
                            (uniqueCarrierFields == 1 ? "" : "_" + OpenStemName(site.Field));

        EmitRoute(definition.Type, module, "(*this)", 0, "        ");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine();

        void EmitRoute(TypeExpr expressionType, IrModule expressionModule, string expression,
            int index, string indent)
        {
            while (expressionType is RefType reference)
            {
                var found = FindWithModule(document, expressionModule, reference);
                if (found is null) return;
                expressionModule = found.Value.Module;
                expressionType = found.Value.Def.Type;
            }
            expressionType = UnwrapAliases(document, expressionModule, expressionType);

            if (index == carrierPath.Count)
            {
                if (site.Selector.Levels == 0)
                {
                    sb.AppendLine($"{indent}if (auto decoded = {expression}.{carrierMethod}(binding)) {{");
                }
                else
                {
                    var raw = expression + "." + FieldName(site.Field);
                    if (site.Field.Optional)
                    {
                        sb.AppendLine($"{indent}if (!{raw}.has_value()) return result;");
                        raw = "*" + raw;
                    }
                    var decode = site.Payload.Kind switch
                    {
                        OpenPayloadKind.Contained => "asn1kit::decode_contained<T>",
                        OpenPayloadKind.Many => "asn1kit::decode_each<T>",
                        _ => "asn1kit::decode_open<T>"
                    };
                    sb.AppendLine($"{indent}{{ auto decoded = std::optional<{valueType}>{{{decode}({raw})}};");
                }
                sb.AppendLine($"{indent}    asn1kit::merge_unique(result, std::move(decoded));");
                sb.AppendLine($"{indent}}}");
                return;
            }

            var step = carrierPath[index];
            if (step == "[]")
            {
                var element = expressionType switch
                {
                    SequenceOfType sequence => sequence.Element,
                    SetOfType set => set.Element,
                    _ => null
                };
                if (element is null) return;
                var local = "open_item_" + index.ToString(CultureInfo.InvariantCulture);
                sb.AppendLine($"{indent}for (const auto& {local} : {expression}) {{");
                EmitRoute(element, expressionModule, local, index + 1, indent + "    ");
                sb.AppendLine($"{indent}}}");
                return;
            }

            if (step == "containing") return;
            if (OpenContainerFields(expressionType) is not { } fields) return;
            var field = fields.SingleOrDefault(candidate => candidate.Name == step);
            if (field is null) return;
            var access = expression + "." + FieldName(field);
            var optional = (field.Optional || field.ExtensionAddition == true) && field.Default is null;
            if (optional)
            {
                sb.AppendLine($"{indent}if ({access}.has_value()) {{");
                access = "(*" + access + ")";
                if (ShouldEmitLazy(document, expressionModule, field.Type, field.Options) ||
                    ShouldEmitRetainEncoded(document, expressionModule, field.Type, field.Options))
                    access += ".get()";
                EmitRoute(field.Type, expressionModule, access, index + 1, indent + "    ");
                sb.AppendLine($"{indent}}}");
            }
            else
            {
                if (ShouldEmitLazy(document, expressionModule, field.Type, field.Options) ||
                    ShouldEmitRetainEncoded(document, expressionModule, field.Type, field.Options))
                    access += ".get()";
                EmitRoute(field.Type, expressionModule, access, index + 1, indent);
            }
        }
    }

    private static string EscapeCppString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private void EmitOpenViews(StringBuilder sb, IrModule module, string owner)
    {
        var viewTables = OpenViews(module, owner)
            .Select(static view => view.Table)
            .ToHashSet(StringComparer.Ordinal);
        if (viewTables.Count == 0) return;
        var sites = _openSitesByType[(module.Name, owner)]
            .GroupBy(site => OpenViewUsageIdentity((module.Name, owner), site),
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .Where(site => SupportsOpenView((module.Name, owner), site))
            .Where(site => viewTables.Contains(
                OpenViewPath((module.Name, owner), site).Table))
            .ToList();
        foreach (var tableGroup in sites
                     .GroupBy(site =>
                             OpenViewPath((module.Name, owner), site).Table,
                         StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var names = _openViewNames[(module.Name, owner, tableGroup.Key)];
            var tableSites = tableGroup
                .OrderBy(site =>
                        OpenViewPath((module.Name, owner), site).Field,
                    StringComparer.Ordinal)
                .ToList();

            if (tableSites.Count > 1)
            {
                foreach (var site in tableSites)
                {
                    var path = OpenViewPath((module.Name, owner), site);
                    var fieldNames = _openFieldViewNames[
                        (module.Name, owner, tableGroup.Key, path.Field)];
                    EmitOpenFunctionView(sb, module, owner, fieldNames.ViewType, site);
                }
                EmitOpenTableView(sb, module, owner, names.ViewType, tableGroup.Key, tableSites);
            }
            else
            {
                var site = tableSites[0];
                EmitOpenFunctionView(sb, module, owner, names.ViewType, site);
            }

        }
    }

    private void EmitOpenFunctionView(
        StringBuilder sb, IrModule module, string owner, string viewType, OpenSite site)
    {
        var members = OpenBindingMembers(site).ToList();
        var decodeMethod = OpenOwnerDecodeMethod(module, owner, site);
        var canMutate = IsDirectOpenOwner((module.Name, owner), site);
        var mutateMethod = canMutate
            ? OpenOwnerCarrierMethod(module, owner, site, "set")
            : null;
        var createMethod = canMutate
            ? OpenOwnerCarrierMethod(module, owner, site, "create")
            : null;
        var usesDefaultAccess = canMutate && decodeMethod == "try_decode" &&
                                mutateMethod == "set" && createMethod == "create";

        sb.AppendLine("template <typename Target>");
        sb.AppendLine($"struct {viewType} {{");
        EmitOpenViewEntriesAlias(sb, module, owner, site);
        if (usesDefaultAccess)
        {
            sb.AppendLine("    using access = asn1kit::open_direct_access;");
        }
        else
        {
            sb.AppendLine("    struct access {");
            sb.AppendLine("        template <typename Source, typename Binding>");
            sb.AppendLine("        static auto decode(const Source& source, const Binding& binding) {");
            sb.AppendLine($"            return source.{decodeMethod}(binding);");
            sb.AppendLine("        }");
            if (canMutate)
            {
                sb.AppendLine();
                sb.AppendLine("        template <typename Source, typename Binding, typename Value>");
                sb.AppendLine("        static void set(Source& source, const Binding& binding, Value&& value) {");
                sb.AppendLine(
                    $"            source.{mutateMethod}(binding, std::forward<Value>(value));");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        template <typename Source, typename Binding, typename Value>");
                sb.AppendLine("        static Source create(const Binding& binding, Value&& value) {");
                sb.AppendLine(
                    $"            return Source::{createMethod}(binding, std::forward<Value>(value));");
                sb.AppendLine("        }");
            }
            sb.AppendLine("    };");
        }
        sb.AppendLine();

        sb.AppendLine($"    using base = asn1kit::open_function_view<Target, access>;");
        sb.AppendLine();
        sb.AppendLine($"    explicit {viewType}(Target& target)");
        sb.AppendLine(
            "        requires (!asn1kit::is_open_factory_target_v<Target> && !asn1kit::is_open_owned_target_v<Target>)");
        sb.AppendLine("        : view_(target) {}");
        sb.AppendLine();
        sb.AppendLine($"    explicit {viewType}(asn1kit::open_owner_t<Target>&& owner)");
        sb.AppendLine("        requires asn1kit::is_open_owned_target_v<Target>");
        sb.AppendLine("        : view_(std::move(owner)) {}");
        if (canMutate)
        {
            sb.AppendLine();
            sb.AppendLine($"    {viewType}()");
            sb.AppendLine("        requires asn1kit::is_open_factory_target_v<Target> = default;");
        }
        sb.AppendLine();
        foreach (var member in members)
            sb.AppendLine(
                $"    ASN1KIT_DETAIL_OPEN_VIEW_ENTRY({member.Method}, {member.Descriptor})");
        sb.AppendLine();
        sb.AppendLine("private:");
        sb.AppendLine("    [[no_unique_address]] base view_;");
        sb.AppendLine("};");
        sb.AppendLine();
    }

    private void EmitOpenViewEntriesAlias(
        StringBuilder sb, IrModule module, string owner, OpenSite site)
    {
        var entriesName = _openCatalogEntryNames[
            (site.Container.Module.Name, site.CatalogStem)];
        var qualifiedEntries = string.Equals(
            ModuleNamespace(module), ModuleNamespace(site.Container.Module),
            StringComparison.Ordinal)
            ? "detail::" + entriesName
            : "::" + ModuleNamespace(site.Container.Module) + "::detail::" + entriesName;
        sb.AppendLine($"    using open_entries = {qualifiedEntries};");
        sb.AppendLine();
    }

    private void EmitOpenTableView(
        StringBuilder sb,
        IrModule module,
        string owner,
        string viewType,
        string table,
        IReadOnlyList<OpenSite> sites)
    {
        var hasFactory = sites.Any(site => IsDirectOpenOwner((module.Name, owner), site));
        sb.AppendLine("template <typename Target>");
        sb.AppendLine($"struct {viewType} {{");
        sb.AppendLine($"    explicit {viewType}(Target& target)");
        sb.AppendLine(
            "        requires (!asn1kit::is_open_factory_target_v<Target> && !asn1kit::is_open_owned_target_v<Target>)");
        sb.AppendLine("        : target_(target) {}");
        sb.AppendLine();
        sb.AppendLine($"    explicit {viewType}(asn1kit::open_owner_t<Target>&& owner)");
        sb.AppendLine("        requires asn1kit::is_open_owned_target_v<Target>");
        sb.AppendLine("        : target_(std::move(owner)) {}");
        if (hasFactory)
        {
            sb.AppendLine();
            sb.AppendLine($"    {viewType}()");
            sb.AppendLine("        requires asn1kit::is_open_factory_target_v<Target> = default;");
        }
        sb.AppendLine();
        foreach (var site in sites)
        {
            var path = OpenViewPath((module.Name, owner), site);
            var fieldType = _openFieldViewNames[(module.Name, owner, table, path.Field)].ViewType;
            var canCreate = IsDirectOpenOwner((module.Name, owner), site);
            sb.AppendLine("    [[nodiscard]] auto " + path.Field + "() const &");
            if (!canCreate)
                sb.AppendLine("        requires (!asn1kit::is_open_factory_target_v<Target>)");
            sb.AppendLine("    {");
            sb.AppendLine("        if constexpr (asn1kit::is_open_factory_target_v<Target>)");
            sb.AppendLine($"            return {fieldType}<Target>{{}};");
            sb.AppendLine("        else if constexpr (asn1kit::is_open_owned_target_v<Target>)");
            sb.AppendLine(
                $"            return {fieldType}<const asn1kit::open_owner_t<Target>>{{target_.get()}};");
            sb.AppendLine("        else");
            sb.AppendLine($"            return {fieldType}<Target>{{target_.get()}};");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [[nodiscard]] auto " + path.Field + "() &&");
            if (!canCreate)
                sb.AppendLine("        requires (!asn1kit::is_open_factory_target_v<Target>)");
            sb.AppendLine("    {");
            sb.AppendLine("        if constexpr (asn1kit::is_open_factory_target_v<Target>)");
            sb.AppendLine($"            return {fieldType}<Target>{{}};");
            sb.AppendLine("        else if constexpr (asn1kit::is_open_owned_target_v<Target>)");
            sb.AppendLine(
                $"            return {fieldType}<Target>{{std::move(target_).take()}};");
            sb.AppendLine("        else");
            sb.AppendLine($"            return {fieldType}<Target>{{target_.get()}};");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void " + path.Field + "() const && = delete;");
            sb.AppendLine();
        }
        sb.AppendLine("private:");
        sb.AppendLine("    asn1kit::open_view_target<Target> target_;");
        sb.AppendLine("};");
        sb.AppendLine();
    }

    private IEnumerable<(OpenBinding Binding, string Descriptor, string Method)> OpenBindingMembers(
        OpenSite site)
    {
        var descriptorNames = new HashSet<string>(StringComparer.Ordinal);
        var methodNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "open_entries", "access", "base", "view_", "target_"
        };
        foreach (var binding in site.Bindings)
        {
            var descriptor = binding.MemberName;
            for (var suffix = 2; !descriptorNames.Add(descriptor); suffix++)
                descriptor = binding.MemberName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            var method = descriptor;
            if (!methodNames.Add(method))
            {
                var baseName = descriptor + "_entry";
                method = baseName;
                for (var suffix = 2; !methodNames.Add(method); suffix++)
                    method = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            }
            yield return (binding, descriptor, method);
        }
    }

    private string OpenOwnerDecodeMethod(IrModule module, string owner, OpenSite site)
    {
        if (IsDirectOpenOwner((module.Name, owner), site))
            return OpenOwnerCarrierMethod(module, owner, site, "try_decode");
        var openFieldIndex = -1;
        for (var i = site.ContextPath.Count - 1; i >= 0; i--)
        {
            if (site.ContextPath[i] != site.Field.Name) continue;
            openFieldIndex = i;
            break;
        }
        var carrierPath = openFieldIndex < 0
            ? Array.Empty<string>()
            : site.ContextPath.Take(openFieldIndex).ToArray();
        var routeStem = carrierPath.First(part => part is not "[]" and not "containing");
        return "try_decode_" + ToSnakeCaseIdentifier(routeStem);
    }

    private string OpenOwnerCarrierMethod(
        IrModule module, string owner, OpenSite site, string method)
    {
        var directFields = _openSitesByType[(module.Name, owner)]
            .Where(candidate => IsDirectOpenOwner((module.Name, owner), candidate))
            .Select(static candidate => candidate.Field.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
        return directFields == 1 ? method : method + "_" + OpenStemName(site.Field);
    }

    private void EmitOpenCatalogEntries(
        StringBuilder sb, IrDocument document, IrModule module, OpenSite site)
    {
        var entriesName = _openCatalogEntryNames[(module.Name, site.CatalogStem)];
        sb.AppendLine($"struct {entriesName} {{");
        EmitOpenCatalogMembers(sb, document, module, site, "    ");
        sb.AppendLine("};");
        sb.AppendLine();
    }

    private void EmitOpenCatalogMembers(
        StringBuilder sb, IrDocument document, IrModule module, OpenSite site, string indent)
    {
        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<(OpenBinding Binding, string Member)>();
        foreach (var binding in site.Bindings)
        {
            var member = binding.MemberName;
            for (var suffix = 2; !memberNames.Add(member); suffix++)
                member = binding.MemberName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            members.Add((binding, member));
        }

        var alias = "binding_type";
        for (var suffix = 2; memberNames.Contains(alias); suffix++)
            alias = "binding_type_" + suffix.ToString(CultureInfo.InvariantCulture);
        var contextName = site.Container.Module.Name + "." + site.CatalogStem;
        sb.AppendLine($"{indent}template <typename T>");
        sb.AppendLine($"{indent}using {alias} = asn1kit::open_binding<");
        sb.AppendLine($"{indent}    asn1kit::open_context<\"{EscapeCppString(contextName)}\">,");
        sb.AppendLine($"{indent}    {OpenKeyCppType(document, site)}, T>;");
        sb.AppendLine();

        foreach (var (binding, member) in members)
        {
            var leafType = OpenBindingCppType(
                document, module, site.Container.TypeName,
                binding.Binding.Name ?? "Value", binding.Binding.Type);
            var valueType = site.Payload.Kind == OpenPayloadKind.Many
                ? $"std::vector<{leafType}>"
                : leafType;
            var keyExpr = OpenBindingKeyExpression(document, module, site, binding.Binding.Key);
            sb.AppendLine($"{indent}inline static const {alias}<{valueType}> {member}{{{keyExpr}}};");
        }
        sb.AppendLine();
    }

    private string OpenKeyExpression(IrDocument document, OpenSite site)
    {
        var root = site.Selector.Levels == 0
            ? "src"
            : "parent" + (site.Selector.Levels - 1).ToString(CultureInfo.InvariantCulture);
        return OpenKeyAccessExpression(document, site, root);
    }

    private string OpenKeyAssignmentTarget(IrDocument document, OpenSite site)
    {
        return OpenKeyAccessExpression(document, site, "src");
    }

    private string OpenKeyAccessExpression(IrDocument document, OpenSite site, string root)
    {
        var keyContainer = site.Selector.Levels == 0
            ? site.Container
            : site.Parents[site.Selector.Levels - 1];
        var expression = root;
        var container = keyContainer;
        for (var index = 0; index < site.Selector.Path.Count; index++)
        {
            var part = site.Selector.Path[index];
            if (part is "[]" or "containing")
            {
                throw new NotSupportedException(
                    "C++ open-type Binding selector path cannot contain '" + part + "'.");
            }

            var field = container.Fields.Single(f => f.Name == part);
            expression += "." + FieldName(field);
            if (field.Optional)
            {
                expression = "(*" + expression + ")";
            }

            if (ShouldEmitLazy(document, container.Module, field.Type, field.Options) ||
                ShouldEmitRetainEncoded(document, container.Module, field.Type, field.Options))
            {
                expression += ".get()";
            }

            if (index == site.Selector.Path.Count - 1)
            {
                break;
            }

            container = OpenKeyStepContainer(document, container, field);
        }

        return expression;
    }

    private (TypeExpr Type, IrModule Module, IrComponent Field) OpenKeyType(IrDocument document, OpenSite site)
    {
        if (site.Selector.Path.Count == 0)
        {
            throw new NotSupportedException(
                "C++ open-type Binding requires a non-empty selector path.");
        }

        var keyContainer = site.Selector.Levels == 0
            ? site.Container
            : site.Parents[site.Selector.Levels - 1];
        var container = keyContainer;
        IrComponent field = null!;
        TypeExpr type = null!;
        var module = container.Module;
        for (var index = 0; index < site.Selector.Path.Count; index++)
        {
            var part = site.Selector.Path[index];
            if (part is "[]" or "containing")
            {
                throw new NotSupportedException(
                    "C++ open-type Binding selector path cannot contain '" + part + "'.");
            }

            field = container.Fields.Single(f => f.Name == part);
            (module, type) = ResolveAliasTarget(document, container.Module, field.Type);
            if (index == site.Selector.Path.Count - 1)
            {
                break;
            }

            container = OpenKeyStepContainer(document, container, field);
        }

        if (type is not OidType and not IntegerType)
        {
            throw new NotSupportedException(
                "C++ open-type Binding supports only OBJECT IDENTIFIER or INTEGER selectors.");
        }

        return (type, module, field);
    }

    private OpenContainer OpenKeyStepContainer(
        IrDocument document, OpenContainer container, IrComponent field)
    {
        var (fieldModule, type) = ResolveAliasTarget(document, container.Module, field.Type);
        var fields = OpenContainerFields(type)
            ?? throw new NotSupportedException(
                "Open-type selector must traverse SEQUENCE or SET.");
        var owner = container.TypeName + "_" + CppNames.SanitizeTypeName(field.Name);
        if (field.Type is RefType reference)
        {
            var found = FindWithModule(document, container.Module, reference);
            if (found is not null)
            {
                var (_, def) = ResolveAliasOfTarget(document, found.Value.Module, found.Value.Def);
                owner = IrOptions.CppTypeName(def.Options) ?? CppNames.SanitizeTypeName(def.Name);
            }
        }

        return new OpenContainer(fieldModule, owner, owner, fields);
    }

    private string OpenKeyCppType(IrDocument document, OpenSite site)
    {
        var (type, module, _) = OpenKeyType(document, site);
        if (type is OidType) return "asn1kit::oid";
        var representation = TryResolveIntegerRepresentation(document, module, type)
            ?? IrOptions.IntegerRepresentations.Der;
        return MapIntegerCppType(representation);
    }

    private static (IrModule Module, TypeExpr Type) ResolveAliasTarget(
        IrDocument document, IrModule module, TypeExpr type)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (type is RefType reference)
        {
            var key = (reference.Module ?? module.Name) + "::" + reference.Name;
            if (!visited.Add(key))
                throw new NotSupportedException($"Circular type alias '{reference.Name}'.");
            var found = FindWithModule(document, module, reference)
                ?? throw new NotSupportedException($"Cannot resolve type reference '{reference.Name}'.");
            module = found.Module;
            type = found.Def.Type;
        }

        return (module, type);
    }

    private string OpenBindingKeyExpression(
        IrDocument document, IrModule module, OpenSite site, string key)
    {
        var (type, keyModule, _) = OpenKeyType(document, site);
        if (type is IntegerType)
        {
            var representation = TryResolveIntegerRepresentation(document, keyModule, type)
                ?? IrOptions.IntegerRepresentations.Der;
            return representation switch
            {
                IrOptions.IntegerRepresentations.Int32 =>
                    $"std::int32_t{{{CppNames.EscapeString(key)}}}",
                IrOptions.IntegerRepresentations.UInt32 =>
                    $"std::uint32_t{{{CppNames.EscapeString(key)}}}",
                IrOptions.IntegerRepresentations.Int64 =>
                    $"std::int64_t{{{CppNames.EscapeString(key)}}}",
                IrOptions.IntegerRepresentations.UInt64 =>
                    $"std::uint64_t{{{CppNames.EscapeString(key)}}}",
                IrOptions.IntegerRepresentations.BigInt or IrOptions.IntegerRepresentations.Der =>
                    $"asn1kit::integer::from_decimal(\"{CppNames.EscapeString(key)}\")",
                _ => throw new NotSupportedException(
                    $"Unknown integer representation '{representation}'.")
            };
        }

        return KnownOidExpression(document, module, key) ??
               $"asn1kit::oid_literal<\"{CppNames.EscapeString(key)}\">().get()";
    }

    private static TypeExpr QualifyOpenTypeReferences(
        IrDocument document, IrModule sourceModule, IrModule targetModule, TypeExpr type)
    {
        if (sourceModule == targetModule) return type;
        var node = JsonSerializer.SerializeToNode(type, IrSerializer.JsonOptions)!;
        void Qualify(JsonNode? current)
        {
            if (current is not JsonObject obj) return;
            if (obj["kind"]?.GetValue<string>() == TypeKinds.Ref)
            {
                var reference = obj.Deserialize<TypeExpr>(IrSerializer.JsonOptions) as RefType;
                var found = FindWithModule(document, sourceModule, reference!);
                if (found is not null) obj["module"] = found.Value.Module.Name;
            }

            if (obj["element"] is { } element) Qualify(element);
            if (obj["containing"] is { } containing) Qualify(containing);
            foreach (var collection in new[] { "components", "bindings" })
            {
                if (obj[collection] is not JsonArray array) continue;
                foreach (var child in array) Qualify(child?["type"]);
            }
        }

        Qualify(node);
        return node.Deserialize<TypeExpr>(IrSerializer.JsonOptions)!;
    }

    private void CollectOpenTypeNested(
        IrDocument document,
        IrModule module,
        OpenPlan plan,
        Queue<(string Name, TypeExpr Type)> queue)
    {
        foreach (var binding in plan.Bindings)
        {
            OfferNested(
                document,
                module,
                binding.Site.Container.TypeName + "_" + CppNames.SanitizeTypeName(binding.MemberName),
                binding.Binding.Type,
                queue);
        }
    }
}

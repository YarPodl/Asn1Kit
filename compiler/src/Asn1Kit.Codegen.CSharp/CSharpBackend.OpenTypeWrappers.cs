using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.CSharp;

public sealed partial class CSharpBackend
{
    private sealed record OpenContainer(IrModule Module, string Owner, string CsType,
        IReadOnlyList<IrComponent> Fields, IReadOnlyList<OpenAccess> Route, bool Extensible = false, bool ValueType = false);

    private IrModule _openWrapperModule = null!;

    private sealed record OpenAccess(string? Property, bool Optional, bool Wrapped,
        bool Many = false, OpenPayload? Contained = null);

    // Payload nodes describe only the transparent OF / CONTAINING path to the raw ANY.
    private sealed record OpenPayload(IrModule Module, string Owner, string Hint, TypeExpr Type,
        string RawType, bool Many, bool BitString, OpenPayload? Child);

    private sealed record OpenWrapperSite(string Context, string? Owner, IrComponent? Source,
        OpenContainer Container, IrComponent Field, OpenPayload Payload, AnyType Any,
        IrOpenTypeSelector Selector, IReadOnlyList<OpenContainer> Parents,
        IReadOnlyList<OpenAccess> Route, bool SourceValueType, List<OpenWrapper> Wrappers,
        string? Table, IReadOnlyList<string> Path)
    {
        public string DecodeMethod { get; set; } = "";
        public string? DecodeBindingStem { get; set; }
        public string? GetMethod { get; set; }
        /// <summary>True when this site emits the shared container TryDecode/Set for its stem.</summary>
        public bool EmitContainerApi { get; set; }
    }

    private sealed record OpenWrapper(string Name, string BindingName, IrOpenTypeBinding Binding, OpenWrapperSite Site)
    {
        public string? CodecExpression { get; set; }
    }
    private sealed record OpenDecodeAlternative(string? Name, TypeExpr Type, JsonObject? Options, string CsType);
    private sealed record OpenDecodeBindingMember(OpenWrapper Wrapper, string Name, string CsType,
        IReadOnlyList<OpenDecodeAlternative>? Alternatives);
    private sealed record OpenWrapperPlan(List<OpenWrapper> Wrappers, List<OpenWrapperSite> Sites);

    private sealed class ModuleOpenTypeCodecEntry
    {
        public ModuleOpenTypeCodecEntry(string name, string valueType, string owner, string hint, TypeExpr bindingType)
        {
            Name = name;
            ValueType = valueType;
            Owner = owner;
            Hint = hint;
            BindingType = bindingType;
        }

        public string Name { get; }
        public string ValueType { get; }
        public string Owner { get; }
        public string Hint { get; }
        public TypeExpr BindingType { get; }
    }

    private sealed class ModuleOpenTypeCodecCache
    {
        public ModuleOpenTypeCodecCache(string className) => ClassName = className;

        public string ClassName { get; }
        public Dictionary<string, ModuleOpenTypeCodecEntry> ByIdentity { get; } =
            new(StringComparer.Ordinal);
        public HashSet<string> FieldNames { get; } = new(StringComparer.Ordinal);
        public List<ModuleOpenTypeCodecEntry> Ordered { get; } = new();
    }

    private ModuleOpenTypeCodecCache? _openTypeCodecCache;
    private readonly HashSet<string> _openContainerConvenienceNames = new(StringComparer.Ordinal);

    private OpenWrapperPlan PlanOpenTypeWrappers(IrDocument document, IrModule module,
        IReadOnlyList<(string Name, TypeExpr Type)> types, HashSet<string> emitted)
    {
        _openWrapperModule = module;
        var sites = new List<OpenWrapperSite>();
        var wrappers = new List<OpenWrapper>();
        var shared = new Dictionary<string, OpenWrapper>(StringComparer.Ordinal);
        var names = new HashSet<string>(emitted, StringComparer.Ordinal) { OidCatalogName(module) };

        foreach (var (owner, type) in types)
        {
            var fields = OpenContainerFields(type);
            if (fields is null) continue;
            var parent = new OpenContainer(module, owner, owner, fields, Array.Empty<OpenAccess>(),
                ValueType: IsCSharpValueTypeEmit(module, owner, type));
            foreach (var field in fields)
                Discover(field.Type, module, owner, field.Name, owner + PropertyName(field, owner).TrimStart('@'),
                    owner, field, new List<OpenAccess> { FieldAccess(module, owner, field) },
                    new List<OpenContainer> { parent }, new HashSet<string>(StringComparer.Ordinal));
        }
        foreach (var definition in module.Types.Where(t => IrOptions.ShouldGenerate(t.Options) && IsCollapsibleAlias(t.Type)))
        {
            var context = IrOptions.CSharpTypeName(definition.Options) ?? SanitizeIdentifier(definition.Name);
            Discover(definition.Type, module, context, "", context, null, null,
                new List<OpenAccess>(), new List<OpenContainer>(), new HashSet<string>(StringComparer.Ordinal));
        }
        return new OpenWrapperPlan(wrappers, sites);

        OpenAccess FieldAccess(IrModule contextModule, string owner, IrComponent field) =>
            new(PropertyName(field, owner), field.Optional,
                ShouldEmitLazy(document, contextModule, field.Type, field.Options) ||
                ShouldEmitRetainEncoded(document, contextModule, field.Type, field.Options));

        void Discover(TypeExpr expression, IrModule contextModule, string namingOwner, string hint,
            string context, string? sourceOwner, IrComponent? source, List<OpenAccess> route,
            List<OpenContainer> parents, HashSet<string> visited)
        {
            if (expression is RefType reference)
            {
                var found = FindWithModule(document, contextModule, reference);
                if (found is null || !IrOptions.ShouldGenerate(found.Value.Def.Options)) return;
                var identity = found.Value.Module.Name + "." + found.Value.Def.Name;
                if (!visited.Add(identity)) return;
                foreach (var use in reference.OpenTypes ?? Enumerable.Empty<IrOpenTypeUse>())
                    ResolveUse(reference, contextModule, namingOwner, hint, context, sourceOwner, source,
                        use, route, parents);
                var targetOwner = IrOptions.CSharpTypeName(found.Value.Def.Options) ?? SanitizeIdentifier(found.Value.Def.Name);
                if (found.Value.Module != module) targetOwner = ModuleNamespace(found.Value.Module) + "." + targetOwner;
                // Structured definitions have their own source methods. Follow only transparent aliases here.
                if (OpenContainerFields(found.Value.Def.Type) is null)
                    Discover(found.Value.Def.Type, found.Value.Module, targetOwner, "", context,
                        sourceOwner, source, route, parents, visited);
                visited.Remove(identity);
                return;
            }
            if (IsSingleAlternativeChoice(expression))
            {
                Discover(((ChoiceType)expression).Components[0].Type, contextModule, namingOwner, hint,
                    context, sourceOwner, source, route, parents, visited);
                return;
            }
            if (expression is SequenceOfType or SetOfType)
            {
                ResolveOfItemNaming(document, contextModule, expression, namingOwner, hint, out var itemOwner, out var itemHint);
                var element = expression is SequenceOfType sequence ? sequence.Element : ((SetOfType)expression).Element;
                Discover(element, contextModule, itemOwner, itemHint, context, sourceOwner, source,
                    route.Append(new OpenAccess(null, false, IsWrapped(element, contextModule), Many: true)).ToList(),
                    parents, visited);
            }
            else if (ContainedType(expression) is { } content)
            {
                var payload = BuildOpenPayload(document, contextModule, namingOwner, hint, expression);
                ContainedNaming(document, contextModule, expression, namingOwner, hint,
                    out var contentModule, out var contentOwner, out var contentHint);
                Discover(content, contentModule, contentOwner, contentHint, context, sourceOwner, source,
                    route.Append(new OpenAccess(null, false, false, Contained: payload)).ToList(), parents, visited);
            }
        }

        bool IsWrapped(TypeExpr expression, IrModule contextModule) =>
            ShouldEmitLazy(document, contextModule, expression, expression.Options) ||
            ShouldEmitRetainEncoded(document, contextModule, expression, expression.Options);

        void ResolveUse(TypeExpr expression, IrModule contextModule, string namingOwner, string hint,
            string context, string? sourceOwner, IrComponent? source, IrOpenTypeUse use,
            List<OpenAccess> prefix, List<OpenContainer> sourceParents)
        {
            if (use.Bindings.Count == 0) return;
            var bindingModule = contextModule;
            var route = prefix.ToList();
            var parents = sourceParents.ToList();
            OpenContainer? container = null;
            IrComponent? openField = null;
            OpenPayload? payload = null;
            var pathIndex = 0;
            while (true)
            {
                if (expression is RefType reference)
                {
                    var found = FindWithModule(document, contextModule, reference);
                    if (found is null || !IrOptions.ShouldGenerate(found.Value.Def.Options)) return;
                    var named = IrOptions.CSharpTypeName(found.Value.Def.Options) ?? SanitizeIdentifier(found.Value.Def.Name);
                    namingOwner = ModuleNamespace(found.Value.Module) + "." + named;
                    hint = "";
                    contextModule = found.Value.Module;
                    expression = found.Value.Def.Type;
                    continue;
                }
                if (IsSingleAlternativeChoice(expression))
                {
                    expression = ((ChoiceType)expression).Components[0].Type;
                    continue;
                }
                if (OpenContainerFields(expression) is { } fields)
                {
                    if (pathIndex >= use.Path.Count) return;
                    var owner = hint.Length == 0 ? namingOwner : namingOwner + "_" + SanitizeIdentifier(hint);
                    container = new OpenContainer(contextModule, owner, owner, fields, route.ToList(),
                        expression is SequenceType { Extensible: true } or SetType { Extensible: true },
                        IsCSharpValueTypeEmit(contextModule, owner.Split('.').Last(), expression));
                    parents.Add(container);
                    var part = use.Path[pathIndex++];
                    openField = fields.Single(f => f.Name == part);
                    payload = BuildOpenPayload(document, contextModule, owner, openField.Name, openField.Type);
                    expression = openField.Type;
                    namingOwner = owner;
                    hint = openField.Name;
                    // The leaf's owner is its nearest SEQUENCE/SET, not the collection/CONTAINING wrapper.
                    if (OpenElement(document, contextModule, expression) is AnyType)
                        continue;
                    route.Add(FieldAccess(contextModule, owner, openField));
                    continue;
                }
                if (expression is SequenceOfType or SetOfType)
                {
                    if (pathIndex >= use.Path.Count || use.Path[pathIndex++] != "[]") return;
                    ResolveOfItemNaming(document, contextModule, expression, namingOwner, hint, out var itemOwner, out var itemHint);
                    expression = expression is SequenceOfType sequence ? sequence.Element : ((SetOfType)expression).Element;
                    namingOwner = itemOwner; hint = itemHint;
                    if (container is null || OpenElement(document, contextModule, expression) is not AnyType)
                        route.Add(new OpenAccess(null, false, IsWrapped(expression, contextModule), Many: true));
                    continue;
                }
                if (ContainedType(expression) is { } content)
                {
                    if (pathIndex >= use.Path.Count || use.Path[pathIndex++] != "containing") return;
                    if (container is null || OpenElement(document, contextModule, content) is not AnyType)
                        route.Add(new OpenAccess(null, false, false,
                            Contained: BuildOpenPayload(document, contextModule, namingOwner, hint, expression)));
                    ContainedNaming(document, contextModule, expression, namingOwner, hint,
                        out var contentModule, out var contentOwner, out var contentHint);
                    contextModule = contentModule; namingOwner = contentOwner; hint = contentHint;
                    expression = content;
                    continue;
                }
                if (expression is not AnyType { Bindings: null } any || container is null || openField is null ||
                    payload is null || pathIndex != use.Path.Count || any.Selector is not { } selector) return;
                if (selector.Levels >= parents.Count) return;
                var ancestors = parents.Take(parents.Count - 1).Reverse().Take(selector.Levels).ToList();
                var site = new OpenWrapperSite(context, sourceOwner, source, container, openField, payload,
                    any, selector, ancestors, container.Route, sourceParents.FirstOrDefault()?.ValueType ?? false,
                    new List<OpenWrapper>(), use.Table, use.Path);
                foreach (var binding in use.Bindings)
                {
                    var qualified = new IrOpenTypeBinding { Key = binding.Key, Name = binding.Name,
                        Type = QualifyOpenTypeReferences(document, bindingModule, module, binding.Type) };
                    var identity = container.CsType + "\n" + openField.Name + "\n" +
                        JsonSerializer.Serialize(payload.Type, IrSerializer.JsonOptions) + "\n" +
                        JsonSerializer.Serialize(qualified.Type, IrSerializer.JsonOptions) + "\n" + binding.Key + "\n" +
                        JsonSerializer.Serialize(selector, IrSerializer.JsonOptions) + "\n" + string.Join(";", ancestors.Select(p => p.CsType));
                    if (!shared.TryGetValue(identity, out var wrapper))
                    {
                        var semantic = OpenBindingIdentifier(binding);
                        var baseName = SanitizeIdentifier(semantic) + container.CsType.Split('.').Last();
                        var name = baseName;
                        if (!ReserveCodecNames(name))
                        {
                            name = context + baseName;
                            for (var suffix = 2; !ReserveCodecNames(name); suffix++) name = context + baseName + suffix;
                        }
                        wrapper = new OpenWrapper(name, semantic, qualified, site);
                        shared.Add(identity, wrapper); wrappers.Add(wrapper);
                    }
                    site.Wrappers.Add(wrapper);
                }
                if (site.Wrappers.Count > 0) sites.Add(site);
                return;
            }
        }

        bool ReserveCodecNames(string name)
        {
            if (names.Contains(name) || names.Contains(name + "BindingCodec")) return false;
            names.Add(name);
            names.Add(name + "BindingCodec");
            return true;
        }
    }

    private static string OpenBindingIdentifier(IrOpenTypeBinding binding)
    {
        var semantic = binding.Name;
        if (semantic is null)
            semantic = binding.Type is RefType reference
                ? reference.Name
                : "Oid" + binding.Key.Replace('.', '_');
        foreach (var leading in new[] { "ext-", "aa-", "at-" })
        {
            if (!semantic.StartsWith(leading, StringComparison.Ordinal)) continue;
            semantic = semantic[leading.Length..];
            break;
        }
        return SanitizeIdentifier(semantic);
    }

    private static void AssignOpenWrapperApiNames(OpenWrapperPlan plan, HashSet<string> names)
    {
        foreach (var wrapper in plan.Wrappers) names.Add(wrapper.Name);

        // One catalog stem per table identity (container + open field + selector + bindings).
        var byIdentity = new Dictionary<string, List<OpenWrapperSite>>(StringComparer.Ordinal);
        foreach (var site in plan.Sites)
        {
            var identity = OpenTableIdentity(site);
            if (!byIdentity.TryGetValue(identity, out var list))
                byIdentity.Add(identity, list = new List<OpenWrapperSite>());
            list.Add(site);
        }

        foreach (var sites in byIdentity.Values)
        {
            var preferred = sites
                .OrderBy(static s => PreferredOpenTableStem(s).Length)
                .ThenBy(static s => PreferredOpenTableStem(s), StringComparer.Ordinal)
                .ThenBy(static s => s.Context, StringComparer.Ordinal)
                .First();
            var stemBase = PreferredOpenTableStem(preferred);
            var stem = stemBase;
            if (!ReserveOpenWrapperBindingNames(stem, names))
            {
                var disambiguated = stemBase + preferred.Container.CsType.Split('.').Last();
                stem = disambiguated;
                for (var suffix = 2; !ReserveOpenWrapperBindingNames(stem, names); suffix++)
                    stem = disambiguated + suffix;
            }
            foreach (var site in sites)
                site.DecodeBindingStem = stem;
        }

        // Container TryDecode/Set: open-field name (TryDecodeParameters). Different Binding<T>
        // stems overload the same method name; emit the body once per stem.
        var emittedStems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in plan.Sites
                     .OrderBy(static s => s.DecodeBindingStem, StringComparer.Ordinal)
                     .ThenBy(static s => s.Context, StringComparer.Ordinal))
        {
            var field = PropertyName(site.Field, site.Container.Owner).TrimStart('@');
            site.DecodeMethod = "TryDecode" + field;
            var stem = site.DecodeBindingStem!;
            if (emittedStems.Add(stem))
                site.EmitContainerApi = true;
        }

        // Owner-scoped TryGet names stay per owner; they share the table stem's Binding type.
        foreach (var group in plan.Sites.GroupBy(s => s.Owner ?? s.Context))
        {
            var methods = new HashSet<string>(StringComparer.Ordinal);
            foreach (var site in group)
            {
                if (site.Owner is null || site.Source is null) continue;
                var method = "TryGet" + PropertyName(site.Source, site.Owner).TrimStart('@');
                if (!methods.Add(method))
                {
                    var baseMethod = method + PropertyName(site.Field, site.Container.Owner).TrimStart('@');
                    method = baseMethod;
                    for (var suffix = 2; !methods.Add(method); suffix++) method = baseMethod + suffix;
                }
                site.GetMethod = method;
            }
        }
    }

    private static string PreferredOpenTableStem(OpenWrapperSite site)
    {
        var openField = PropertyName(site.Field, site.Container.Owner).TrimStart('@');
        var container = site.Container.CsType.Split('.').Last();
        if (!string.IsNullOrEmpty(site.Table))
        {
            var table = SanitizeIdentifier(site.Table);
            // OF / nested paths: bare object-set name (CertExtensions, Numbers).
            // Direct field: table + open field (SignatureAlgorithmsParameters).
            return site.Path.Any(static part => part == "[]") ? table : table + openField;
        }
        return container + openField;
    }

    /// <summary>
    /// Table identity ignores the SEQUENCE owner: only the open-type container, field,
    /// selector ancestors, and binding set matter.
    /// </summary>
    private static string OpenTableIdentity(OpenWrapperSite site)
    {
        var bindings = string.Join("\n", site.Wrappers.Select(static wrapper =>
            wrapper.Binding.Key + "\t" + (wrapper.Binding.Name ?? "") + "\t" +
            JsonSerializer.Serialize(wrapper.Binding.Type, IrSerializer.JsonOptions)));
        return site.Container.CsType + "\n" + site.Field.Name + "\n" +
               JsonSerializer.Serialize(site.Selector, IrSerializer.JsonOptions) + "\n" +
               string.Join(";", site.Parents.Select(static parent => parent.CsType)) + "\n" +
               JsonSerializer.Serialize(site.Payload.Type, IrSerializer.JsonOptions) + "\n" +
               bindings;
    }

    private static bool ReserveOpenWrapperBindingNames(string stem, HashSet<string> names)
    {
        var generated = new[]
        {
            stem + "Decoder", stem + "Encoder", stem + "Binding", stem + "DecoderBinding", stem + "Bindings"
        };
        if (generated.Any(names.Contains)) return false;
        foreach (var name in generated) names.Add(name);
        return true;
    }

    private static bool ReserveOpenBindingMemberName(string name, HashSet<string> names)
    {
        var generated = new[] { name, "Decode" + name, "Encode" + name };
        if (generated.Any(names.Contains)) return false;
        foreach (var symbol in generated) names.Add(symbol);
        return true;
    }

    private static IReadOnlyList<IrComponent>? OpenContainerFields(TypeExpr expression) => expression switch
    {
        SequenceType sequence => sequence.Components,
        SetType set => set.Components,
        _ => null
    };

    private OpenPayload BuildOpenPayload(IrDocument document, IrModule module, string owner, string hint, TypeExpr original)
    {
        var raw = CsType(document, _openWrapperModule, owner, hint,
            QualifyOpenTypeReferences(document, module, _openWrapperModule, original), false);
        var type = UnwrapAliases(document, module, original);
        if (type is SequenceOfType or SetOfType)
        {
            ResolveOfItemNaming(document, module, original, owner, hint, out var itemOwner, out var itemHint);
            var element = type is SequenceOfType sequence ? sequence.Element : ((SetOfType)type).Element;
            return new OpenPayload(module, owner, hint, original, raw, true, false,
                BuildOpenPayload(document, module, itemOwner, itemHint, element));
        }
        if (ContainedType(type) is { } content)
        {
            ContainedNaming(document, module, original, owner, hint, out var contentModule, out var contentOwner, out var contentHint);
            return new OpenPayload(module, owner, hint, original, raw, false, type is BitStringType,
                BuildOpenPayload(document, contentModule, contentOwner, contentHint, content));
        }
        return new OpenPayload(module, owner, hint, original, raw, false, false, null);
    }

    private string OpenWrapperValueType(IrDocument document, IrModule module, OpenWrapper wrapper, OpenPayload node) =>
        node.Child is null ? CsType(document, module, wrapper.Name, "Value", wrapper.Binding.Type, false) :
        OpenWrapperValueType(document, module, wrapper, node.Child) + (node.Many ? "[]" : "");

    private static string OpenDecodedHint(string? name) => "Decoded" + SanitizeIdentifier(name ?? "Value");

    private void EmitOpenTypeWrappers(StringBuilder sb, IrDocument document, IrModule module, OpenWrapperPlan plan)
    {
        foreach (var wrapper in plan.Wrappers.Where(static wrapper => wrapper.CodecExpression is null))
            EmitOpenBindingCodec(sb, document, module, wrapper);
        foreach (var site in plan.Sites
                     .Where(static site => site.DecodeMethod.Length > 0)
                     .GroupBy(static site => site.DecodeBindingStem, StringComparer.Ordinal)
                     .Select(static group => group.First()))
            EmitOpenDecodeBindingDescriptor(sb, document, module, site);
        // Container TryDecode/Set once per table stem (avoids ambiguous extension methods).
        var containerSites = plan.Sites.Where(static site => site.EmitContainerApi && site.DecodeMethod.Length > 0)
            .ToList();
        if (containerSites.Count > 0)
        {
            _openContainerConvenienceNames.Clear();
            sb.AppendLine($"public static class {SanitizeIdentifier(module.Name)}OpenTypeExtensions");
            sb.AppendLine("{");
            foreach (var site in containerSites)
                EmitOpenWrapperDecodeMethod(sb, document, module, site, site.DecodeMethod);
            foreach (var site in containerSites.Where(static site =>
                         site.Route.Count > 0 && site.Route.Any(static step => step.Many) &&
                         site.Selector.Levels == 0))
                EmitOpenArrayGetMethod(sb, document, site);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        foreach (var group in plan.Sites.Where(static site => site.GetMethod is not null)
                     .GroupBy(s => s.Owner ?? s.Context))
        {
            sb.AppendLine($"public static class {group.Key}OpenTypeExtensions");
            sb.AppendLine("{");
            foreach (var site in group)
                EmitOpenWrapperGetMethod(sb, document, site, site.GetMethod!);
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private void AssignOpenCodecExpressions(IrDocument document, IrModule module, OpenWrapperPlan plan)
    {
        foreach (var wrapper in plan.Wrappers)
        {
            if (SimpleOpenPayloadKind(wrapper.Site.Payload) is null) continue;
            wrapper.CodecExpression = ResolveModuleOpenTypeCodec(
                document, module, wrapper.Name, wrapper.Name, "Value", wrapper.Binding.Type);
        }
    }

    private string ResolveModuleOpenTypeCodec(
        IrDocument document,
        IrModule module,
        string preferredFieldName,
        string owner,
        string hint,
        TypeExpr bindingType)
    {
        if (TryRuntimeOpenCodecExpression(document, module, bindingType, out var runtimeCodec))
            return runtimeCodec;

        _openTypeCodecCache ??= new ModuleOpenTypeCodecCache(
            "__" + SanitizeIdentifier(module.Name) + "OpenTypeCodecs");

        var valueType = CsType(document, module, owner, hint, bindingType, false);
        var identity = valueType + "\n" + JsonSerializer.Serialize(bindingType, IrSerializer.JsonOptions);
        if (!_openTypeCodecCache.ByIdentity.TryGetValue(identity, out var entry))
        {
            var name = preferredFieldName;
            for (var suffix = 2; !_openTypeCodecCache.FieldNames.Add(name); suffix++)
                name = preferredFieldName + suffix;
            entry = new ModuleOpenTypeCodecEntry(name, valueType, owner, hint, bindingType);
            _openTypeCodecCache.ByIdentity.Add(identity, entry);
            _openTypeCodecCache.Ordered.Add(entry);
        }

        return _openTypeCodecCache.ClassName + "." + entry.Name;
    }

    private void EmitOpenTypeCodecCache(StringBuilder sb, IrDocument document, IrModule module)
    {
        if (_openTypeCodecCache is null || _openTypeCodecCache.Ordered.Count == 0) return;
        sb.AppendLine($"internal static class {_openTypeCodecCache.ClassName}");
        sb.AppendLine("{");
        foreach (var entry in _openTypeCodecCache.Ordered)
        {
            if (UsesGeneratedNamedCodec(document, module, entry.BindingType))
            {
                sb.AppendLine($"    internal static Asn1Codec<{entry.ValueType}> {entry.Name} {{ get; }} =");
                sb.AppendLine($"        new(static reader => {entry.ValueType}.Decode(reader), static (writer, value) => value.Encode(writer));");
                sb.AppendLine();
                continue;
            }
            sb.AppendLine($"    internal static Asn1Codec<{entry.ValueType}> {entry.Name} {{ get; }} = new(");
            sb.AppendLine("        static reader =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            {entry.ValueType} decoded;");
            EmitDecodeAssign(sb, document, module, entry.Owner, entry.Hint, entry.BindingType, null,
                "            ", "reader", "decoded");
            sb.AppendLine("            return decoded;");
            sb.AppendLine("        },");
            sb.AppendLine("        static (writer, value) =>");
            sb.AppendLine("        {");
            EmitEncodeValue(sb, document, module, entry.Owner, entry.Hint, entry.BindingType,
                "            ", "writer", "value");
            sb.AppendLine("        });");
            sb.AppendLine();
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private bool UsesGeneratedNamedCodec(IrDocument document, IrModule module, TypeExpr type)
    {
        if (type.Tag is not null || type is not RefType reference) return false;
        var found = FindWithModule(document, module, reference);
        if (found is null || !IrOptions.ShouldGenerate(found.Value.Def.Options)) return false;
        return found.Value.Def.Type switch
        {
            SequenceType => true,
            SetType => true,
            ChoiceType choice when !IsSingleAlternativeChoice(choice) => true,
            BitStringType bits when IsNamedBitString(bits) => true,
            _ => false
        };
    }

    private static string? SimpleOpenPayloadKind(OpenPayload payload)
    {
        if (payload.Child is null && payload.RawType == "Asn1Any") return "direct";
        if (payload.Many && payload.RawType == "Asn1Any[]" &&
            payload.Child is { Child: null, RawType: "Asn1Any" }) return "many";
        if (!payload.Many && payload.RawType == "Asn1Contained<Asn1Any>" &&
            payload.Child is { Child: null, RawType: "Asn1Any" }) return "contained";
        return null;
    }

    private bool TryRuntimeOpenCodecExpression(
        IrDocument document,
        IrModule module,
        TypeExpr bindingType,
        out string expression)
    {
        expression = "";
        var type = UnwrapAliases(document, module, bindingType);
        if (type.Tag is not null) return false;
        expression = type switch
        {
            BooleanType => "Asn1Codecs.Boolean",
            IntegerType => TryResolveIntegerRepresentation(document, module, bindingType) switch
            {
                IrOptions.IntegerRepresentations.Int32 => "Asn1Codecs.Int32",
                IrOptions.IntegerRepresentations.UInt32 => "Asn1Codecs.UInt32",
                IrOptions.IntegerRepresentations.Int64 => "Asn1Codecs.Int64",
                IrOptions.IntegerRepresentations.UInt64 => "Asn1Codecs.UInt64",
                IrOptions.IntegerRepresentations.BigInt => "Asn1Codecs.BigInteger",
                _ => "Asn1Codecs.Integer"
            },
            OctetStringType { Containing: null } => "Asn1Codecs.OctetString",
            NullType => "Asn1Codecs.Null",
            OidType => "Asn1Codecs.ObjectIdentifier",
            BitStringType { Containing: null, NamedBits: null } => "Asn1Codecs.BitString",
            AnyType { Bindings: null } => "Asn1Codecs.Any",
            StringType text => text.Form switch
            {
                StringTypes.Utf8 => "Asn1Codecs.Utf8String",
                StringTypes.Printable => "Asn1Codecs.PrintableString",
                StringTypes.Teletex or StringTypes.T61 => "Asn1Codecs.TeletexString",
                StringTypes.Ia5 => "Asn1Codecs.Ia5String",
                StringTypes.Numeric => "Asn1Codecs.NumericString",
                StringTypes.Visible => "Asn1Codecs.VisibleString",
                StringTypes.Bmp => "Asn1Codecs.BmpString",
                StringTypes.Universal => "Asn1Codecs.UniversalString",
                StringTypes.General => "Asn1Codecs.GeneralString",
                StringTypes.Graphic => "Asn1Codecs.GraphicString",
                StringTypes.Videotex => "Asn1Codecs.VideotexString",
                _ => ""
            },
            TimeType { FractionDigits: null or 3 } time => time.Form switch
            {
                TimeTypes.Utc => "Asn1Codecs.UtcTime",
                TimeTypes.Generalized => "Asn1Codecs.GeneralizedTime",
                _ => ""
            },
            _ => ""
        };
        return expression.Length != 0;
    }

    private static string OpenCodecDecode(OpenWrapper wrapper, string raw)
    {
        if (wrapper.CodecExpression is null) return $"{wrapper.Name}BindingCodec.Decode({raw})";
        var codec = wrapper.CodecExpression;
        return SimpleOpenPayloadKind(wrapper.Site.Payload) switch
        {
            "direct" => $"{codec}.Decode({raw})",
            "many" => $"Asn1Codecs.DecodeEach({raw}, {codec})",
            "contained" => $"Asn1Codecs.DecodeContained({raw}, {codec})",
            _ => $"{wrapper.Name}BindingCodec.Decode({raw})"
        };
    }

    private static string OpenCodecEncode(OpenWrapper wrapper, string value)
    {
        if (wrapper.CodecExpression is null) return $"{wrapper.Name}BindingCodec.Encode({value})";
        var codec = wrapper.CodecExpression;
        return SimpleOpenPayloadKind(wrapper.Site.Payload) switch
        {
            "direct" => $"{codec}.Encode({value})",
            "many" => $"Asn1Codecs.EncodeEach({value}, {codec})",
            "contained" => $"Asn1Codecs.EncodeContained({value}, {codec})",
            _ => $"{wrapper.Name}BindingCodec.Encode({value})"
        };
    }

    // Direct/complex codecs already match Encoder<T>; many/contained still need a thin adapter.
    private static string? OpenCodecEncodeMethodGroup(OpenWrapper wrapper)
    {
        if (wrapper.CodecExpression is null) return $"{wrapper.Name}BindingCodec.Encode";
        return SimpleOpenPayloadKind(wrapper.Site.Payload) == "direct"
            ? $"{wrapper.CodecExpression}.Encode"
            : null;
    }

    private void EmitOpenBindingCodec(StringBuilder sb, IrDocument document, IrModule module, OpenWrapper wrapper)
    {
        var valueType = OpenWrapperValueType(document, module, wrapper, wrapper.Site.Payload);
        var rawType = wrapper.Site.Payload.RawType;
        sb.AppendLine($"internal static class {wrapper.Name}BindingCodec");
        sb.AppendLine("{");
        sb.AppendLine($"    internal static {valueType} Decode({rawType} raw) => Decode0(raw);");
        sb.AppendLine($"    internal static {rawType} Encode({valueType} value) => Encode0(value);");
        EmitOpenPayloadMethods(sb, document, module, wrapper, wrapper.Site.Payload, 0);
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private IReadOnlyList<OpenDecodeBindingMember> OpenDecodeBindingMembers(
        IrDocument document, IrModule module, OpenWrapperSite site)
    {
        var result = new List<OpenDecodeBindingMember>();
        var names = new HashSet<string>(StringComparer.Ordinal) { "Create" };
        foreach (var wrapper in site.Wrappers)
        {
            var fullName = wrapper.BindingName;
            for (var suffix = 2; !ReserveOpenBindingMemberName(fullName, names); suffix++)
                fullName = wrapper.BindingName + suffix;
            result.Add(new OpenDecodeBindingMember(
                wrapper,
                fullName,
                OpenWrapperValueType(document, module, wrapper, site.Payload),
                null));

            if (site.Payload.Child is not null) continue;
            var alternatives = ExpandOpenTypeChoice(document, module, module, null,
                    wrapper.Binding.Type, null, new HashSet<ChoiceType>())
                .Select(alternative => new OpenDecodeAlternative(
                    alternative.Name,
                    alternative.Type,
                    alternative.Options,
                    CsType(document, module, wrapper.Name, OpenDecodedHint(alternative.Name),
                        alternative.Type, false, alternative.Options)))
                .ToList();
            if (alternatives.Count <= 1) continue;
            foreach (var group in alternatives.GroupBy(static alternative => alternative.CsType))
            {
                var baseName = fullName + OpenTypeClrGroupPropertyName(group.Key);
                var name = baseName;
                for (var suffix = 2; !ReserveOpenBindingMemberName(name, names); suffix++)
                    name = baseName + suffix;
                result.Add(new OpenDecodeBindingMember(wrapper, name, group.Key, group.ToList()));
            }
        }
        return result;
    }

    private void EmitOpenDecodeBindingDescriptor(
        StringBuilder sb, IrDocument document, IrModule module, OpenWrapperSite site)
    {
        var stem = site.DecodeBindingStem!;
        var keyType = OpenKeyType(document, site).Type is OidType ? "Asn1Oid" : "BigInteger";
        var keyName = keyType == "Asn1Oid" ? "Oid" : "Key";
        var keyParameter = CamelCaseIdentifier(keyName);
        var parameters = $"{site.Container.CsType} source{OpenParentParameters(site)}";
        var members = OpenDecodeBindingMembers(document, module, site);
        sb.AppendLine($"public delegate T {stem}Decoder<T>({parameters});");
        sb.AppendLine($"public delegate {site.Payload.RawType} {stem}Encoder<T>(T value);");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {stem}Binding<T>");
        sb.AppendLine("{");
        sb.AppendLine($"    internal {stem}Binding({keyType} {keyParameter}, {stem}Decoder<T> decoder, {stem}Encoder<T> encoder)");
        sb.AppendLine("    {");
        sb.AppendLine($"        {keyName} = {keyParameter};");
        sb.AppendLine("        Decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));");
        sb.AppendLine("        Encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public {keyType} {keyName} {{ get; }}");
        sb.AppendLine($"    internal {stem}Decoder<T> Decoder {{ get; }}");
        sb.AppendLine($"    internal {stem}Encoder<T> Encoder {{ get; }}");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {stem}DecoderBinding<T>");
        sb.AppendLine("{");
        sb.AppendLine($"    internal {stem}DecoderBinding({keyType} {keyParameter}, {stem}Decoder<T> decoder)");
        sb.AppendLine("    {");
        sb.AppendLine($"        {keyName} = {keyParameter};");
        sb.AppendLine("        Decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public {keyType} {keyName} {{ get; }}");
        sb.AppendLine($"    internal {stem}Decoder<T> Decoder {{ get; }}");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"public static class {stem}Bindings");
        sb.AppendLine("{");
        sb.AppendLine($"    public static {stem}Binding<T> Create<T>({keyType} {keyParameter}, {stem}Decoder<T> decoder, {stem}Encoder<T> encoder) =>");
        sb.AppendLine($"        new({keyParameter}, decoder, encoder);");
        sb.AppendLine();
        sb.AppendLine($"    public static {stem}DecoderBinding<T> Create<T>({keyType} {keyParameter}, {stem}Decoder<T> decoder) =>");
        sb.AppendLine($"        new({keyParameter}, decoder);");
        foreach (var member in members)
        {
            sb.AppendLine();
            var descriptor = member.Alternatives is null ? "Binding" : "DecoderBinding";
            var encodeMethodGroup = member.Alternatives is null
                ? OpenCodecEncodeMethodGroup(member.Wrapper)
                : null;
            var encoder = member.Alternatives is null
                ? $", {encodeMethodGroup ?? $"Encode{member.Name}"}"
                : "";
            sb.AppendLine($"    public static {stem}{descriptor}<{member.CsType}> {member.Name} {{ get; }} =");
            sb.AppendLine($"        new({OpenBindingKeyValue(document, module, site, member.Wrapper.Binding.Key)}, Decode{member.Name}{encoder});");
            sb.AppendLine();
            if (member.Alternatives is null)
            {
                sb.AppendLine($"    private static {member.CsType} Decode{member.Name}({parameters})");
                sb.AppendLine("    {");
                var raw = OpenRawValue(sb, document, site, "source", "        ");
                sb.AppendLine($"        return {OpenCodecDecode(member.Wrapper, raw)};");
                sb.AppendLine("    }");
                if (encodeMethodGroup is null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"    private static {site.Payload.RawType} Encode{member.Name}({member.CsType} value) =>");
                    sb.AppendLine($"        {OpenCodecEncode(member.Wrapper, "value")};");
                }
            }
            else if (TryOpenFlattenedHomogeneousValueDecode(document, module, site, members, member,
                         out var typedDecode))
            {
                sb.AppendLine($"    private static {member.CsType} Decode{member.Name}({parameters}) =>");
                sb.AppendLine($"        {typedDecode}.Value;");
            }
            else if (TryOpenStringChoiceForms(document, module, member, out var forms))
            {
                sb.AppendLine($"    private static readonly Asn1StringForm[] Decode{member.Name}Forms = {{ {forms} }};");
                sb.AppendLine();
                sb.AppendLine($"    private static {member.CsType} Decode{member.Name}({parameters})");
                sb.AppendLine("    {");
                var raw = OpenRawValue(sb, document, site, "source", "        ");
                sb.AppendLine($"        return Asn1Codecs.DecodeStringChoice({raw}, Decode{member.Name}Forms);");
                sb.AppendLine("    }");
            }
            else
            {
                sb.AppendLine($"    private static {member.CsType} Decode{member.Name}({parameters})");
                sb.AppendLine("    {");
                var raw = OpenRawValue(sb, document, site, "source", "        ");
                sb.AppendLine($"        var reader = new Asn1Reader({raw}.EncodedMemory, Asn1Encoding.Ber);");
                sb.AppendLine("        if (!reader.TryPeekTag(out var tag)) throw new Asn1Exception(\"Missing open-type value.\");");
                foreach (var alternative in member.Alternatives)
                {
                    var decodeHint = OpenDecodedHint(alternative.Name);
                    sb.AppendLine($"        if ({PeekMatchExpr(document, module, alternative.Type, "tag")})");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            {alternative.CsType} decoded;");
                    EmitDecodeAssign(sb, document, module, member.Wrapper.Name, decodeHint,
                        alternative.Type, null, "            ", "reader", "decoded",
                        fieldOptions: alternative.Options);
                    sb.AppendLine("            reader.ThrowIfNotEmpty();");
                    sb.AppendLine("            return decoded;");
                    sb.AppendLine("        }");
                }
                sb.AppendLine("        throw new Asn1Exception(\"Tag does not match the selected open-type binding.\");");
                sb.AppendLine("    }");
            }
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private bool TryOpenFlattenedHomogeneousValueDecode(
        IrDocument document,
        IrModule module,
        OpenWrapperSite site,
        IReadOnlyList<OpenDecodeBindingMember> members,
        OpenDecodeBindingMember member,
        out string typedDecode)
    {
        typedDecode = "";
        var typed = members.FirstOrDefault(candidate =>
            candidate.Wrapper == member.Wrapper && candidate.Alternatives is null);
        if (typed is null) return false;
        if (!TryResolveMultiAlternativeChoice(document, module, member.Wrapper.Binding.Type, out var choice))
            return false;
        if (TryHomogeneousChoiceCsType(document, module, member.Wrapper.Name, choice) != member.CsType)
            return false;
        typedDecode = $"Decode{typed.Name}(source{OpenParentArguments(site)})";
        return true;
    }

    private bool TryResolveMultiAlternativeChoice(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        out ChoiceType choice)
    {
        choice = null!;
        var unwrapped = UnwrapAliases(document, module, type);
        if (unwrapped is ChoiceType direct && !IsSingleAlternativeChoice(direct))
        {
            choice = direct;
            return true;
        }

        if (unwrapped is RefType reference)
        {
            var found = FindWithModule(document, module, reference);
            if (found?.Def.Type is ChoiceType named && !IsSingleAlternativeChoice(named))
            {
                choice = named;
                return true;
            }
        }

        return false;
    }

    private bool TryOpenStringChoiceForms(
        IrDocument document,
        IrModule module,
        OpenDecodeBindingMember member,
        out string formsExpression)
    {
        formsExpression = "";
        if (member.CsType != "string" || member.Alternatives is null) return false;
        var forms = new List<string>();
        foreach (var alternative in member.Alternatives)
        {
            if (UnwrapAliases(document, module, alternative.Type) is not StringType text) return false;
            forms.Add(StringFormEnum(text.Form));
        }
        if (forms.Count == 0) return false;
        formsExpression = string.Join(", ", forms);
        return true;
    }

    private string OpenBindingKeyValue(IrDocument document, IrModule module, OpenWrapperSite site, string key)
    {
        if (OpenKeyType(document, site).Type is OidType)
            return KnownOidExpression(document, module, key) ??
                   $"Asn1Oid.Parse(\"{EscapeCSharpString(key)}\")";
        return $"BigInteger.Parse(\"{EscapeCSharpString(key)}\", CultureInfo.InvariantCulture)";
    }

    private string OpenRawValue(StringBuilder sb, IrDocument document, OpenWrapperSite site,
        string source, string indent)
    {
        var raw = source + "." + PropertyName(site.Field, site.Container.Owner);
        if (OpenFieldOptional(site.Field))
        {
            sb.AppendLine($"{indent}if ({raw} is not {{ }} raw) throw new Asn1Exception(\"Missing open-type value.\");");
            raw = "raw";
        }
        if (ShouldEmitLazy(document, site.Container.Module, site.Field.Type, site.Field.Options) ||
            ShouldEmitRetainEncoded(document, site.Container.Module, site.Field.Type, site.Field.Options)) raw += ".Value";
        return raw;
    }

    private string OpenKeyExpression(IrDocument document, OpenWrapperSite site, string source)
    {
        var container = site.Selector.Levels == 0 ? site.Container : site.Parents[site.Selector.Levels - 1];
        var expression = site.Selector.Levels == 0 ? source : "parent" + (site.Selector.Levels - 1);
        foreach (var part in site.Selector.Path)
        {
            var field = container.Fields.Single(f => f.Name == part);
            expression += "." + PropertyName(field, container.Owner);
            if (ShouldEmitLazy(document, container.Module, field.Type, field.Options) ||
                ShouldEmitRetainEncoded(document, container.Module, field.Type, field.Options)) expression += ".Value";
            var (fieldModule, type) = ResolveDefaultTarget(document, container.Module, field.Type);
            if (OpenContainerFields(type) is { } fields)
                container = new OpenContainer(fieldModule, CsType(document, container.Module, container.Owner, field.Name, field.Type, false), "", fields, Array.Empty<OpenAccess>());
        }
        return expression;
    }

    private (TypeExpr Type, IrModule Module) OpenKeyType(IrDocument document, OpenWrapperSite site)
    {
        var container = site.Selector.Levels == 0 ? site.Container : site.Parents[site.Selector.Levels - 1];
        TypeExpr type = null!;
        var module = container.Module;
        var fields = container.Fields;
        foreach (var part in site.Selector.Path)
        {
            type = fields.Single(f => f.Name == part).Type;
            (module, type) = ResolveDefaultTarget(document, module, type);
            fields = OpenContainerFields(type) ?? Array.Empty<IrComponent>();
        }
        return (type, module);
    }

    private string OpenKeyComparison(IrDocument document, OpenWrapperSite site, string source, string expected)
    {
        var key = OpenKeyExpression(document, site, source);
        var (type, module) = OpenKeyType(document, site);
        if (type is IntegerType)
        {
            if ((TryResolveIntegerRepresentation(document, module, type) ?? IrOptions.IntegerRepresentations.Der) == IrOptions.IntegerRepresentations.Der)
                key += ".ToBigInteger()";
            return key + " == " + expected;
        }
        return key + ".Equals(" + expected + ")";
    }

    private static string OpenParentParameters(OpenWrapperSite site) =>
        string.Concat(site.Parents.Select((p, i) => $", {p.CsType} parent{i}"));
    private static string OpenParentArguments(OpenWrapperSite site) =>
        string.Concat(site.Parents.Select((_, i) => $", parent{i}"));

    private void EmitOpenKeyOwnerCopies(StringBuilder sb, IrDocument document, IrModule module, OpenWrapperSite site)
    {
        var container = site.Container;
        var target = "result";
        for (var index = 0; index < site.Selector.Path.Count - 1; index++)
        {
            var field = container.Fields.Single(f => f.Name == site.Selector.Path[index]);
            var access = target + "." + PropertyName(field, container.Owner);
            var (definingModule, type) = ResolveDefaultTarget(document, container.Module, field.Type);
            var fields = OpenContainerFields(type) ?? throw new NotSupportedException("Open-type selector must traverse SEQUENCE or SET.");
            var named = NamedTypeName(document, module, container.Owner, field.Name,
                QualifyOpenTypeReferences(document, container.Module, module, field.Type));
            var copy = "keyCopy" + index;
            var source = "keySource" + index;
            var lazy = ShouldEmitLazy(document, container.Module, field.Type, field.Options);
            var retained = ShouldEmitRetainEncoded(document, container.Module, field.Type, field.Options);
            sb.AppendLine($"        var {copy} = new {named}();");
            sb.AppendLine($"        if ({access} is {{ }} {source})"); sb.AppendLine("        {");
            var original = source + (lazy || retained ? ".Value" : "");
            foreach (var member in fields)
                sb.AppendLine($"            {copy}.{PropertyName(member, named)} = {original}.{PropertyName(member, named)};");
            if (type is SequenceType {Extensible: true} or SetType {Extensible: true})
                sb.AppendLine($"            {copy}.UnknownExtensions = {original}.UnknownExtensions;");
            sb.AppendLine("        }");
            var assignment = lazy
                ? "Asn1Lazy<" + named + ">.FromValue(" + copy + ")"
                : retained
                    ? "new Asn1Value<" + named + ">(" + copy + ")"
                    : copy;
            sb.AppendLine($"        {access} = {assignment};");
            target = copy;
            container = new OpenContainer(definingModule, named, named, fields, Array.Empty<OpenAccess>());
        }
    }

    private void EmitOpenKeyAssignment(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        OpenWrapperSite site,
        string keyValue)
    {
        var container = site.Container;
        for (var index = 0; index < site.Selector.Path.Count - 1; index++)
        {
            var owner = container.Fields.Single(field => field.Name == site.Selector.Path[index]);
            var (definingModule, type) = ResolveDefaultTarget(document, container.Module, owner.Type);
            var fields = OpenContainerFields(type) ??
                throw new NotSupportedException("Open-type selector must traverse SEQUENCE or SET.");
            var named = NamedTypeName(document, module, container.Owner, owner.Name,
                QualifyOpenTypeReferences(document, container.Module, module, owner.Type));
            container = new OpenContainer(definingModule, named, named, fields, Array.Empty<OpenAccess>());
        }

        var keyField = container.Fields.Single(field => field.Name == site.Selector.Path[^1]);
        var target = site.Selector.Path.Count == 1
            ? "result." + PropertyName(keyField, container.Owner)
            : "keyCopy" + (site.Selector.Path.Count - 2) + "." + PropertyName(keyField, container.Owner);
        var lazy = ShouldEmitLazy(document, container.Module, keyField.Type, keyField.Options);
        var retained = ShouldEmitRetainEncoded(document, container.Module, keyField.Type, keyField.Options);
        var (keyType, keyModule) = OpenKeyType(document, site);
        var valueType = keyType is OidType
            ? "Asn1Oid"
            : MapIntegerCsType(
                TryResolveIntegerRepresentation(document, keyModule, keyType) ?? IrOptions.IntegerRepresentations.Der,
                optional: false);
        var assignment = lazy
            ? $"Asn1Lazy<{valueType}>.FromValue({keyValue})"
            : retained
                ? $"new Asn1Value<{valueType}>({keyValue})"
                : keyValue;
        sb.AppendLine($"        {target} = {assignment};");
    }

    private void EmitOpenPayloadMethods(StringBuilder sb, IrDocument document, IrModule module,
        OpenWrapper wrapper, OpenPayload node, int depth)
        => EmitOpenPayloadMethods(sb, document, module, wrapper, node, "", depth);

    private void EmitOpenPayloadMethods(StringBuilder sb, IrDocument document, IrModule module,
        OpenWrapper wrapper, OpenPayload node, string prefix, int depth)
    {
        var typed = OpenWrapperValueType(document, module, wrapper, node);
        sb.AppendLine(); sb.AppendLine($"    private static {typed} Decode{prefix}{depth}({node.RawType} raw)"); sb.AppendLine("    {");
        if (node.Child is null)
        {
            sb.AppendLine("        var reader = new Asn1Reader(raw.EncodedMemory, Asn1Encoding.Ber);");
            sb.AppendLine($"        {typed} decoded;");
            EmitDecodeAssign(sb, document, module, wrapper.Name, "Value", wrapper.Binding.Type, null,
                "        ", "reader", "decoded");
            sb.AppendLine("        reader.ThrowIfNotEmpty();"); sb.AppendLine("        return decoded;");
        }
        else if (node.Many)
        {
            var child = OpenWrapperValueType(document, module, wrapper, node.Child);
            sb.AppendLine("        ArgumentNullException.ThrowIfNull(raw);");
            sb.AppendLine($"        if (raw.Length == 0) return Array.Empty<{child}>();");
            sb.AppendLine($"        var result = {OpenArrayAllocation(child, "raw.Length")};");
            sb.AppendLine($"        for (var i = 0; i < raw.Length; i++) result[i] = Decode{prefix}{depth + 1}(raw[i]);");
            sb.AppendLine("        return result;");
        }
        else
        {
            sb.AppendLine("        if (raw.UnusedBits != 0) throw new Asn1Exception(\"Typed contained value must be octet-aligned.\");");
            sb.AppendLine($"        if (raw.HasValue) return Decode{prefix}{depth + 1}(raw.Value);");
            sb.AppendLine("        var reader = new Asn1Reader(raw.Contents, Asn1Encoding.Ber);");
            sb.AppendLine($"        {node.Child.RawType} contents;");
            EmitDecodeAssign(sb, document, module, node.Child.Owner, node.Child.Hint,
                QualifyOpenTypeReferences(document, node.Child.Module, module, node.Child.Type), null,
                "        ", "reader", "contents");
            sb.AppendLine("        reader.ThrowIfNotEmpty();");
            sb.AppendLine($"        return Decode{prefix}{depth + 1}(contents);");
        }
        sb.AppendLine("    }"); sb.AppendLine();
        sb.AppendLine($"    private static {node.RawType} Encode{prefix}{depth}({typed} value)"); sb.AppendLine("    {");
        if (node.Child is null)
        {
            sb.AppendLine("        var writer = new Asn1Writer(Asn1Encoding.Der);");
            EmitEncodeValue(sb, document, module, wrapper.Name, "Value", wrapper.Binding.Type, "        ", "writer", "value");
            sb.AppendLine("        return new Asn1Any(writer.Encode());");
        }
        else if (node.Many)
        {
            sb.AppendLine("        ArgumentNullException.ThrowIfNull(value);");
            sb.AppendLine($"        if (value.Length == 0) return Array.Empty<{node.Child.RawType}>();");
            sb.AppendLine($"        var result = {OpenArrayAllocation(node.Child.RawType, "value.Length")};");
            sb.AppendLine($"        for (var i = 0; i < value.Length; i++) result[i] = Encode{prefix}{depth + 1}(value[i]);");
            sb.AppendLine("        return result;");
        }
        else sb.AppendLine($"        return {node.RawType}.FromValue(Encode{prefix}{depth + 1}(value));");
        sb.AppendLine("    }");
        if (node.Child is not null)
            EmitOpenPayloadMethods(sb, document, module, wrapper, node.Child, prefix, depth + 1);
    }

    private void EmitOpenWrapperDecodeMethod(StringBuilder sb, IrDocument document, IrModule module,
        OpenWrapperSite site, string method)
    {
        var stem = site.DecodeBindingStem!;
        var keyName = OpenKeyType(document, site).Type is OidType ? "Oid" : "Key";
        var members = OpenDecodeBindingMembers(document, module, site);
        sb.AppendLine($"    public static bool {method}<T>(this {site.Container.CsType} source{OpenParentParameters(site)}, {stem}Binding<T> binding, out T value)");
        EmitOpenDecodeBody(sb, document, site, keyName);
        sb.AppendLine();
        sb.AppendLine($"    public static bool {method}<T>(this {site.Container.CsType} source{OpenParentParameters(site)}, {stem}DecoderBinding<T> binding, out T value)");
        EmitOpenDecodeBody(sb, document, site, keyName);
        sb.AppendLine();
        foreach (var member in members.Where(static member =>
                     member.Alternatives is null && member.CsType != "Asn1Null"))
        {
            var convenience = method + member.Name;
            if (!_openContainerConvenienceNames.Add(convenience)) continue;
            sb.AppendLine($"    public static bool {convenience}(this {site.Container.CsType} source{OpenParentParameters(site)}, out {member.CsType} value) =>");
            sb.AppendLine($"        {method}(source{OpenParentArguments(site)}, {stem}Bindings.{member.Name}, out value);");
            sb.AppendLine();
        }

        var setMethod = "Set" + method["TryDecode".Length..];
        var thisParameter = site.Container.ValueType
            ? $"this ref {site.Container.CsType} source"
            : $"this {site.Container.CsType} source";
        sb.AppendLine($"    public static void {setMethod}<T>({thisParameter}{OpenParentParameters(site)}, {stem}Binding<T> binding, T value)");
        sb.AppendLine("    {");
        if (!site.Container.ValueType) sb.AppendLine("        ArgumentNullException.ThrowIfNull(source);");
        foreach (var (parent, index) in site.Parents.Select((parent, index) => (parent, index)))
            if (!parent.ValueType) sb.AppendLine($"        ArgumentNullException.ThrowIfNull(parent{index});");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(binding);");
        sb.AppendLine("        var result = source;");
        if (site.Selector.Levels == 0)
        {
            EmitOpenKeyOwnerCopies(sb, document, module, site);
            var (keyType, keyModule) = OpenKeyType(document, site);
            var keyValue = "binding." + keyName;
            if (keyType is IntegerType)
            {
                var representation = TryResolveIntegerRepresentation(document, keyModule, keyType) ??
                                     IrOptions.IntegerRepresentations.Der;
                keyValue = representation == IrOptions.IntegerRepresentations.Der
                    ? $"Asn1Integer.FromBigInteger(binding.{keyName})"
                    : $"({MapIntegerCsType(representation, false)})binding.{keyName}";
            }
            EmitOpenKeyAssignment(sb, document, module, site, keyValue);
        }
        else
        {
            sb.AppendLine($"        if (!({OpenKeyComparison(document, site, "result", "binding." + keyName)}))");
            sb.AppendLine("            throw new ArgumentException(\"Enclosing selector does not match the open-type binding.\", nameof(binding));");
        }
        var encoded = "binding.Encoder(value)";
        if (ShouldEmitLazy(document, site.Container.Module, site.Field.Type, site.Field.Options))
            encoded = $"Asn1Lazy<{site.Payload.RawType}>.FromValue({encoded})";
        else if (ShouldEmitRetainEncoded(document, site.Container.Module, site.Field.Type, site.Field.Options))
            encoded = $"new Asn1Value<{site.Payload.RawType}>({encoded})";
        sb.AppendLine($"        result.{PropertyName(site.Field, site.Container.Owner)} = {encoded};");
        if (site.Container.ValueType) sb.AppendLine("        source = result;");
        sb.AppendLine("    }");
        foreach (var member in members.Where(static member => member.Alternatives is null))
        {
            var convenience = setMethod + member.Name;
            if (!_openContainerConvenienceNames.Add(convenience)) continue;
            var sourceArgument = site.Container.ValueType ? "ref source" : "source";
            sb.AppendLine();
            if (member.CsType == "Asn1Null")
            {
                sb.AppendLine($"    public static void {convenience}({thisParameter}{OpenParentParameters(site)}) =>");
                sb.AppendLine($"        {setMethod}({sourceArgument}{OpenParentArguments(site)}, {stem}Bindings.{member.Name}, Asn1Null.Value);");
            }
            else
            {
                sb.AppendLine($"    public static void {convenience}({thisParameter}{OpenParentParameters(site)}, {member.CsType} value) =>");
                sb.AppendLine($"        {setMethod}({sourceArgument}{OpenParentArguments(site)}, {stem}Bindings.{member.Name}, value);");
            }
        }
    }

    private void EmitOpenDecodeBody(StringBuilder sb, IrDocument document, OpenWrapperSite site, string keyName)
    {
        sb.AppendLine("    {"); sb.AppendLine("        value = default!;");
        if (!site.Container.ValueType) sb.AppendLine("        ArgumentNullException.ThrowIfNull(source);");
        foreach (var (parent, index) in site.Parents.Select((parent, index) => (parent, index)))
            if (!parent.ValueType) sb.AppendLine($"        ArgumentNullException.ThrowIfNull(parent{index});");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(binding);");
        if (OpenFieldOptional(site.Field))
            sb.AppendLine($"        if (source.{PropertyName(site.Field, site.Container.Owner)} is null) return false;");
        sb.AppendLine($"        if (!({OpenKeyComparison(document, site, "source", "binding." + keyName)})) return false;");
        sb.AppendLine($"        value = binding.Decoder(source{OpenParentArguments(site)});");
        sb.AppendLine("        return true;"); sb.AppendLine("    }");
    }

    /// <summary>
    /// OF helper: <c>extensions.TryGet(CertExtensionsBindings.X, out var value)</c> without the SEQUENCE owner.
    /// </summary>
    private void EmitOpenArrayGetMethod(StringBuilder sb, IrDocument document, OpenWrapperSite site)
    {
        var stem = site.DecodeBindingStem!;
        var keyName = OpenKeyType(document, site).Type is OidType ? "Oid" : "Key";
        var arrayType = site.Container.CsType + "[]";
        sb.AppendLine($"    public static bool TryGet<T>(this {arrayType} source, {stem}Binding<T> binding, out T value)");
        sb.AppendLine("        => TryGet(source, binding, out value, out _);");
        sb.AppendLine();
        sb.AppendLine($"    public static bool TryGet<T>(this {arrayType} source, {stem}Binding<T> binding, out T value, out {site.Container.CsType} raw)");
        sb.AppendLine("    {");
        sb.AppendLine("        value = default!;");
        sb.AppendLine("        raw = default!;");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(source);");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(binding);");
        sb.AppendLine($"        {site.Container.CsType}? match = null;");
        sb.AppendLine("        foreach (var item in source)");
        sb.AppendLine("        {");
        var many = site.Route.Last(static step => step.Many);
        var element = many.Wrapped ? "item.Value" : "item";
        sb.AppendLine($"            if ({OpenKeyComparison(document, site, element, "binding." + keyName)})");
        sb.AppendLine("            {");
        sb.AppendLine("                if (match is not null) throw new Asn1Exception(\"Multiple values match open-type source TryGet.\");");
        sb.AppendLine($"                match = {element};");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        if (match is null) return false;");
        var matched = site.Container.ValueType ? "match.Value" : "match";
        if (OpenFieldOptional(site.Field))
            sb.AppendLine($"        if ({matched}.{PropertyName(site.Field, site.Container.Owner)} is null) return false;");
        sb.AppendLine($"        raw = {matched};");
        sb.AppendLine($"        value = binding.Decoder({matched}{OpenParentArguments(site)});");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();
        foreach (var member in OpenDecodeBindingMembers(document, _openWrapperModule, site)
                     .Where(static member => member.Alternatives is null && member.CsType != "Asn1Null"))
        {
            var convenience = "TryGet" + member.Name;
            if (!_openContainerConvenienceNames.Add(convenience + "@" + arrayType)) continue;
            sb.AppendLine($"    public static bool {convenience}(this {arrayType} source, out {member.CsType} value) =>");
            sb.AppendLine($"        TryGet(source, {stem}Bindings.{member.Name}, out value);");
            sb.AppendLine();
        }
    }

    private void EmitOpenWrapperGetMethod(StringBuilder sb, IrDocument document,
        OpenWrapperSite site, string method)
    {
        var stem = site.DecodeBindingStem!;
        var keyName = OpenKeyType(document, site).Type is OidType ? "Oid" : "Key";
        sb.AppendLine($"    public static bool {method}<T>(this {site.Owner} source, {stem}Binding<T> binding, out T value)");
        sb.AppendLine($"        => {method}(source, binding, out value, out _);");
        sb.AppendLine();
        sb.AppendLine($"    public static bool {method}<T>(this {site.Owner} source, {stem}Binding<T> binding, out T value, out {site.Container.CsType} raw)");
        sb.AppendLine("    {"); sb.AppendLine("        value = default!;");
        sb.AppendLine("        raw = default!;");
        if (!site.SourceValueType) sb.AppendLine("        ArgumentNullException.ThrowIfNull(source);");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(binding);");
        sb.AppendLine($"        {site.Container.CsType}? match = null;");
        foreach (var (parent, index) in site.Parents.Select((p, i) => (p, i)))
        {
            sb.AppendLine($"        {parent.CsType} parent{index} = default!;");
        }
        EmitRoute(0, "source", "        ", new Dictionary<int, string> { [0] = "source" });
        sb.AppendLine("        if (match is null) return false;");
        var matched = "match" + (site.Container.ValueType ? ".Value" : "");
        if (OpenFieldOptional(site.Field))
            sb.AppendLine($"        if ({matched}.{PropertyName(site.Field, site.Container.Owner)} is null) return false;");
        sb.AppendLine($"        raw = {matched};");
        sb.AppendLine($"        value = binding.Decoder({matched}{OpenParentArguments(site)});");
        sb.AppendLine("        return true;"); sb.AppendLine("    }"); sb.AppendLine();
        foreach (var member in OpenDecodeBindingMembers(document, _openWrapperModule, site)
                     .Where(static member => member.Alternatives is null && member.CsType != "Asn1Null"))
        {
            sb.AppendLine($"    public static bool {method}{member.Name}(this {site.Owner} source, out {member.CsType} value) =>");
            sb.AppendLine($"        {method}(source, {stem}Bindings.{member.Name}, out value);");
            sb.AppendLine();
        }

        void EmitRoute(int index, string expression, string indent, Dictionary<int, string> routes)
        {
            if (index == site.Route.Count)
            {
                var comparison = OpenKeyComparison(document, site, expression, "binding." + keyName);
                foreach (var (parent, i) in site.Parents.Select((p, i) => (p, i)))
                {
                    var ancestor = routes[parent.Route.Count];
                    comparison = comparison.Replace("parent" + i + ".", ancestor + ".", StringComparison.Ordinal);
                }
                sb.AppendLine($"{indent}if ({comparison})"); sb.AppendLine(indent + "{");
                sb.AppendLine($"{indent}    if (match is not null) throw new Asn1Exception(\"Multiple values match open-type source {EscapeCSharpString(method)}.\");");
                sb.AppendLine($"{indent}    match = {expression};");
                foreach (var (parent, i) in site.Parents.Select((p, i) => (p, i)))
                    sb.AppendLine($"{indent}    parent{i} = {routes[parent.Route.Count]};");
                sb.AppendLine(indent + "}");
                return;
            }
            var step = site.Route[index];
            var local = "node" + index;
            if (step.Many)
            {
                sb.AppendLine($"{indent}foreach (var {local} in {expression})"); sb.AppendLine(indent + "{");
                var item = local + (step.Wrapped ? ".Value" : "");
                var next = new Dictionary<int, string>(routes) { [index + 1] = item };
                EmitRoute(index + 1, item, indent + "    ", next);
                sb.AppendLine(indent + "}");
            }
            else if (step.Contained is { Child: { } child })
            {
                sb.AppendLine($"{indent}if ({expression}.UnusedBits != 0) throw new Asn1Exception(\"Typed contained value must be octet-aligned.\");");
                sb.AppendLine($"{indent}{child.RawType} {local};");
                sb.AppendLine($"{indent}if ({expression}.HasValue) {local} = {expression}.Value;");
                sb.AppendLine(indent + "else"); sb.AppendLine(indent + "{");
                sb.AppendLine($"{indent}    var reader{index} = new Asn1Reader({expression}.Contents, Asn1Encoding.Ber);");
                EmitDecodeAssign(sb, document, child.Module, child.Owner, child.Hint, child.Type, null,
                    indent + "    ", "reader" + index, local);
                sb.AppendLine($"{indent}    reader{index}.ThrowIfNotEmpty();"); sb.AppendLine(indent + "}");
                var next = new Dictionary<int, string>(routes) { [index + 1] = local };
                EmitRoute(index + 1, local, indent, next);
            }
            else
            {
                var access = expression + "." + step.Property;
                sb.AppendLine($"{indent}if ({access} is {{ }} {local})"); sb.AppendLine(indent + "{");
                var nextExpression = local + (step.Wrapped ? ".Value" : "");
                var next = new Dictionary<int, string>(routes) { [index + 1] = nextExpression };
                EmitRoute(index + 1, nextExpression, indent + "    ", next);
                sb.AppendLine(indent + "}");
            }
        }
    }

    private static string OpenArrayAllocation(string elementType, string length)
    {
        var suffix = "";
        while (elementType.EndsWith("[]", StringComparison.Ordinal))
        {
            elementType = elementType[..^2]; suffix += "[]";
        }
        return "new " + elementType + "[" + length + "]" + suffix;
    }

    private static bool OpenFieldOptional(IrComponent field) =>
        (field.Optional || field.ExtensionAddition == true) && field.Default is null;
}

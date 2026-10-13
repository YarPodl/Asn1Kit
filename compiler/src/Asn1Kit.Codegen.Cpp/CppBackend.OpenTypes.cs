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
        string? Table,
        IReadOnlyList<string> Path,
        List<OpenBinding> Bindings)
    {
        public string BindingStem { get; set; } = "";
        public string CatalogStem { get; set; } = "";
        public bool EmitCatalog { get; set; }
    }

    private sealed record OpenBinding(string MemberName, IrOpenTypeBinding Binding, OpenSite Site);

    private sealed class OpenPlan
    {
        public List<OpenSite> Sites { get; } = new();
        public List<OpenBinding> Bindings { get; } = new();
    }

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
        var names = new HashSet<string>(emittedNames, StringComparer.Ordinal) { OidCatalogName(module) };
        _ = nested;

        foreach (var definition in module.Types.Where(static t => IrOptions.ShouldGenerate(t.Options)))
        {
            if (IrOptions.CppAliasOf(definition.Options) is not null) continue;
            if (IsCollapsibleAlias(definition.Type)) continue;
            var typeName = IrOptions.CppTypeName(definition.Options)
                ?? CppNames.SanitizeTypeName(definition.Name);
            WalkDefinitionScoped(
                document, module, typeName, definition.Type, Array.Empty<OpenContainer>(), plan, shared);
        }

        foreach (var other in document.Modules)
        {
            foreach (var definition in other.Types)
                ScanIncoming(definition.Type, other);
            foreach (var definition in other.Values)
                ScanIncoming(definition.Type, other);
        }

        AssignOpenStems(plan, names);
        return plan;

        void ScanIncoming(TypeExpr expression, IrModule contextModule)
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
                                ResolveUse(document, contextModule, found.Value, use, plan, shared, names);
                        }
                    }

                    if (reference.OpenTypes is not null)
                    {
                        foreach (var use in reference.OpenTypes)
                        foreach (var binding in use.Bindings)
                            ScanIncoming(binding.Type, contextModule);
                    }

                    break;
                case AnyType { Bindings: { } bodyBindings }:
                    foreach (var binding in bodyBindings)
                        ScanIncoming(binding.Type, contextModule);
                    break;
                case SequenceType sequence:
                    foreach (var field in sequence.Components)
                        ScanIncoming(field.Type, contextModule);
                    break;
                case SetType set:
                    foreach (var field in set.Components)
                        ScanIncoming(field.Type, contextModule);
                    break;
                case ChoiceType choice:
                    foreach (var alternative in choice.Components)
                        ScanIncoming(alternative.Type, contextModule);
                    break;
                case SequenceOfType of:
                    ScanIncoming(of.Element, contextModule);
                    break;
                case SetOfType of:
                    ScanIncoming(of.Element, contextModule);
                    break;
                case OctetStringType { Containing: { } containing }:
                    ScanIncoming(containing, contextModule);
                    break;
                case BitStringType { Containing: { } containing }:
                    ScanIncoming(containing, contextModule);
                    break;
            }
        }
    }

    private void WalkDefinitionScoped(
        IrDocument document,
        IrModule module,
        string owner,
        TypeExpr type,
        IReadOnlyList<OpenContainer> parents,
        OpenPlan plan,
        Dictionary<string, OpenBinding> shared)
    {
        type = UnwrapAliases(document, module, type);
        if (IsSingleAlternativeChoice(type))
        {
            WalkDefinitionScoped(
                document, module, owner, ((ChoiceType)type).Components[0].Type, parents, plan, shared);
            return;
        }

        if (type is SequenceOfType or SetOfType)
        {
            var element = type is SetOfType setOf ? setOf.Element : ((SequenceOfType)type).Element;
            WalkDefinitionScoped(document, module, owner + "_Item", element, parents, plan, shared);
            return;
        }

        if (OpenContainerFields(type) is not { } fields) return;

        var container = new OpenContainer(module, owner, owner, fields);
        foreach (var field in fields)
        {
            TryRegisterDefinitionScoped(
                document, module, container, field, parents, plan, shared);
            var fieldType = UnwrapAliases(document, module, field.Type);
            if (fieldType is RefType) continue;
            if (fieldType is SequenceOfType or SetOfType or SequenceType or SetType or ChoiceType)
            {
                var childParents = parents.Append(container).ToList();
                var childOwner = owner + "_" + CppNames.SanitizeTypeName(field.Name);
                WalkDefinitionScoped(
                    document, module, childOwner, fieldType, childParents, plan, shared);
            }
        }
    }

    private void TryRegisterDefinitionScoped(
        IrDocument document,
        IrModule module,
        OpenContainer container,
        IrComponent field,
        IReadOnlyList<OpenContainer> outerParents,
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
            definitionAny.Table, new[] { field.Name }, module, plan, shared);
    }

    private void ResolveUse(
        IrDocument document,
        IrModule contextModule,
        (IrModule Module, IrTypeDef Def) target,
        IrOpenTypeUse use,
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
        string? table,
        IReadOnlyList<string> path,
        IrModule bindingModule,
        OpenPlan plan,
        Dictionary<string, OpenBinding> shared)
    {
        if (container.Module != emittingModule) return;
        var site = new OpenSite(
            container, openField, payload, selector, parents.ToList(), table, path.ToList(),
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
            var catalogName = stem + "_bindings";
            if (!names.Add(catalogName))
            {
                var disambiguated = stemBase + "_" + CppNames.SanitizeFieldName(preferred.Container.TypeName);
                stem = disambiguated;
                catalogName = stem + "_bindings";
                for (var suffix = 2; !names.Add(catalogName); suffix++)
                {
                    stem = disambiguated + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                    catalogName = stem + "_bindings";
                }
            }

            foreach (var site in sites)
                site.CatalogStem = stem;
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in plan.Sites.OrderBy(static s => s.CatalogStem, StringComparer.Ordinal))
        {
            if (emitted.Add(site.CatalogStem))
                site.EmitCatalog = true;
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
    /// strip that before appending "_binding" / "_bindings".
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
                if (i > 0 && sb[^1] != '_') sb.Append('_');
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
        if (plan.Sites.Count == 0) return;

        var emittedStems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in plan.Sites
                     .OrderBy(static s => s.BindingStem, StringComparer.Ordinal)
                     .ThenBy(static s => s.Container.TypeName, StringComparer.Ordinal))
        {
            if (!emittedStems.Add(site.BindingStem)) continue;
            EmitOpenBindingType(sb, document, module, site);
        }

        foreach (var site in plan.Sites.Where(static s => s.EmitCatalog)
                     .OrderBy(static s => s.CatalogStem, StringComparer.Ordinal))
        {
            EmitOpenCatalog(sb, document, module, site);
        }
    }

    private void EmitOpenBindingType(
        StringBuilder sb, IrDocument document, IrModule module, OpenSite site)
    {
        var stem = site.BindingStem;
        var container = site.Container.TypeName;
        var field = FieldName(site.Field);
        var keyExpr = OpenKeyExpression(document, site);
        var keyCppType = OpenKeyCppType(document, site);
        var optional = site.Field.Optional;
        var ancestor = site.Selector.Levels > 0;
        var parentParams = string.Concat(
            site.Parents.Select((p, i) => $", const {p.TypeName}& parent{i.ToString(CultureInfo.InvariantCulture)}"));
        var parentArgs = string.Concat(
            site.Parents.Select((_, i) => $", parent{i.ToString(CultureInfo.InvariantCulture)}"));

        var valueParam = site.Payload.Kind == OpenPayloadKind.Many ? "std::vector<T>" : "T";
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

        sb.AppendLine($"template <typename T>");
        sb.AppendLine($"struct {stem}_binding {{");
        sb.AppendLine($"    {keyCppType} key{{}};");
        sb.AppendLine();
        sb.AppendLine(
            $"    [[nodiscard]] bool try_decode(const {container}& src{parentParams}, {valueParam}& out) const {{");
        if (optional)
            sb.AppendLine($"        if (!src.{field}.has_value()) return false;");
        sb.AppendLine($"        if (!({keyExpr} == key)) return false;");
        var raw = optional ? $"*src.{field}" : $"src.{field}";
        sb.AppendLine($"        out = {decodeCall}({raw});");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    void set({container}& src{parentParams}, const {valueParam}& value) const {{");
        if (ancestor)
        {
            sb.AppendLine($"        if (!({keyExpr} == key)) {{");
            sb.AppendLine(
                "            throw asn1kit::exception(\"Enclosing selector does not match the open-type binding.\");");
            sb.AppendLine("        }");
        }
        else
            sb.AppendLine($"        {OpenKeyAssignmentTarget(document, site)} = key;");
        sb.AppendLine($"        src.{field} = {encodeCall}(value);");
        sb.AppendLine("    }");
        if (site.Payload.Kind == OpenPayloadKind.Many)
        {
            sb.AppendLine();
            sb.AppendLine($"    void set({container}& src{parentParams}, const T& value) const {{");
            sb.AppendLine($"        set(src{parentArgs}, std::vector<T>{{value}});");
            sb.AppendLine("    }");
        }

        if (!ancestor)
        {
            sb.AppendLine();
            sb.AppendLine($"    [[nodiscard]] {container} create(const {valueParam}& value) const {{");
            sb.AppendLine($"        {container} src{{}};");
            sb.AppendLine("        set(src, value);");
            sb.AppendLine("        return src;");
            sb.AppendLine("    }");
            if (site.Payload.Kind == OpenPayloadKind.Many)
            {
                sb.AppendLine();
                sb.AppendLine($"    [[nodiscard]] {container} create(const T& value) const {{");
                sb.AppendLine("        return create(std::vector<T>{value});");
                sb.AppendLine("    }");
            }
        }

        sb.AppendLine("};");
        sb.AppendLine();
    }

    private void EmitOpenCatalog(
        StringBuilder sb, IrDocument document, IrModule module, OpenSite site)
    {
        var catalog = site.CatalogStem + "_bindings";
        var stem = site.BindingStem;
        sb.AppendLine($"namespace {catalog} {{");
        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in site.Bindings)
        {
            var member = binding.MemberName;
            for (var suffix = 2; !memberNames.Add(member); suffix++)
                member = binding.MemberName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            var valueType = OpenBindingCppType(
                document, module, site.Container.TypeName, "Value", binding.Binding.Type);
            var keyExpr = OpenBindingKeyExpression(document, module, site, binding.Binding.Key);
            sb.AppendLine($"inline const {stem}_binding<{valueType}> {member}{{");
            sb.AppendLine($"    {keyExpr}}};");
            sb.AppendLine();
        }

        sb.AppendLine($"}} // namespace {catalog}");
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

        foreach (var definition in module.Values)
        {
            if (definition.Value is IrOidValue oid &&
                string.Equals(oid.Value, key, StringComparison.Ordinal) &&
                IrOptions.ShouldGenerate(definition.Options))
            {
                return $"{OidCatalogName(module)}::{CppNames.SanitizeTypeName(definition.Name)}.get()";
            }
        }

        return $"asn1kit::oid_literal<\"{CppNames.EscapeString(key)}\">().get()";
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

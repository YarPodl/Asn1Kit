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

    private sealed record OpenPayload(TypeExpr Type, bool DirectAny);

    private sealed record OpenSite(
        OpenContainer Container,
        IrComponent Field,
        OpenPayload Payload,
        IrOpenTypeSelector Selector,
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

        foreach (var (owner, type) in nested)
        {
            var fields = OpenContainerFields(type);
            if (fields is null) continue;
            var container = new OpenContainer(module, owner, owner, fields);
            foreach (var field in fields)
                TryRegisterDefinitionScoped(document, module, container, field, plan, shared, names);
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

    private void TryRegisterDefinitionScoped(
        IrDocument document,
        IrModule module,
        OpenContainer container,
        IrComponent field,
        OpenPlan plan,
        Dictionary<string, OpenBinding> shared,
        HashSet<string> names)
    {
        if (OpenElement(document, module, field.Type) is not AnyType
            {
                Selector: { Levels: 0 } selector,
                Bindings.Count: > 0,
                TableExtensible: not null
            } definitionAny)
        {
            return;
        }

        var payload = BuildOpenPayload(document, module, field.Type);
        if (!payload.DirectAny) return;
        AddSite(document, module, container, field, payload, selector, definitionAny.Bindings!,
            definitionAny.Table, new[] { field.Name }, module, plan, shared, names);
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

            if (OpenContainerFields(expression) is { } fields)
            {
                if (pathIndex >= use.Path.Count) return;
                var owner = hint.Length == 0
                    ? namingOwner
                    : namingOwner + "_" + CppNames.SanitizeTypeName(hint);
                container = new OpenContainer(module, owner, owner, fields);
                var part = use.Path[pathIndex++];
                openField = fields.Single(f => f.Name == part);
                payload = BuildOpenPayload(document, module, openField.Type);
                expression = openField.Type;
                namingOwner = owner;
                hint = openField.Name;
                if (OpenElement(document, module, expression) is AnyType)
                    continue;
                continue;
            }

            if (expression is SequenceOfType or SetOfType)
            {
                // MVP: skip OF open-type payloads.
                return;
            }

            if (ContainedType(expression) is not null)
            {
                // MVP: skip CONTAINING open-type payloads.
                return;
            }

            if (expression is not AnyType any || any.Bindings is { Count: > 0 } ||
                container is null || openField is null || payload is null ||
                pathIndex != use.Path.Count || any.Selector is not { Levels: 0 } selector)
            {
                return;
            }

            if (!payload.DirectAny) return;
            AddSite(document, target.Module, container, openField, payload, selector, use.Bindings,
                use.Table, use.Path, contextModule, plan, shared, names);
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
        IReadOnlyList<IrOpenTypeBinding> bindings,
        string? table,
        IReadOnlyList<string> path,
        IrModule bindingModule,
        OpenPlan plan,
        Dictionary<string, OpenBinding> shared,
        HashSet<string> names)
    {
        if (container.Module != emittingModule) return;
        var site = new OpenSite(container, openField, payload, selector, table, path.ToList(), new List<OpenBinding>());
        foreach (var binding in bindings)
        {
            var qualified = new IrOpenTypeBinding
            {
                Key = binding.Key,
                Name = binding.Name,
                Type = QualifyOpenTypeReferences(document, bindingModule, emittingModule, binding.Type)
            };
            var identity = container.TypeName + "\n" + openField.Name + "\n" +
                JsonSerializer.Serialize(payload.Type, IrSerializer.JsonOptions) + "\n" +
                JsonSerializer.Serialize(qualified.Type, IrSerializer.JsonOptions) + "\n" + binding.Key + "\n" +
                JsonSerializer.Serialize(selector, IrSerializer.JsonOptions);
            if (!shared.TryGetValue(identity, out var wrapper))
            {
                var member = OpenBindingMemberName(binding);
                var reserved = member;
                for (var suffix = 2; !names.Add(reserved); suffix++)
                    reserved = member + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                wrapper = new OpenBinding(reserved, qualified, site);
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
            var stemBase = FieldName(site.Field);
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
        var openField = FieldName(site.Field);
        if (!string.IsNullOrEmpty(site.Table))
        {
            var table = CppNames.SanitizeFieldName(site.Table);
            return site.Path.Any(static part => part == "[]") ? table : table + "_" + openField;
        }

        return CppNames.SanitizeFieldName(site.Container.TypeName) + "_" + openField;
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
            return new OpenPayload(original, DirectAny: false);
        if (ContainedType(type) is not null)
            return new OpenPayload(original, DirectAny: false);
        if (OpenElement(document, module, original) is AnyType)
            return new OpenPayload(original, DirectAny: true);
        return new OpenPayload(original, DirectAny: false);
    }

    private TypeExpr? OpenElement(IrDocument document, IrModule module, TypeExpr type)
    {
        type = UnwrapAliases(document, module, type);
        return type is AnyType any ? any : null;
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
        var keyField = OpenKeyFieldName(document, site);
        var optional = site.Field.Optional;

        sb.AppendLine($"template <typename T>");
        sb.AppendLine($"struct {stem}_binding {{");
        sb.AppendLine("    asn1kit::oid key{};");
        sb.AppendLine();
        sb.AppendLine($"    [[nodiscard]] bool try_decode(const {container}& src, T& out) const {{");
        if (optional)
            sb.AppendLine($"        if (!src.{field}.has_value()) return false;");
        sb.AppendLine($"        if (!(src.{keyField} == key)) return false;");
        var raw = optional ? $"*src.{field}" : $"src.{field}";
        sb.AppendLine($"        out = asn1kit::decode_open<T>({raw});");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    void set({container}& src, const T& value) const {{");
        sb.AppendLine($"        src.{keyField} = key;");
        sb.AppendLine($"        src.{field} = asn1kit::encode_open(value);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    [[nodiscard]] {container} create(const T& value) const {{");
        sb.AppendLine($"        {container} src{{}};");
        sb.AppendLine("        set(src, value);");
        sb.AppendLine("        return src;");
        sb.AppendLine("    }");
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
            var keyExpr = OpenBindingKeyExpression(document, module, binding.Binding.Key);
            sb.AppendLine($"inline const {stem}_binding<{valueType}> {member}{{");
            sb.AppendLine($"    {keyExpr}}};");
            sb.AppendLine();
        }

        sb.AppendLine($"}} // namespace {catalog}");
        sb.AppendLine();
    }

    private string OpenKeyFieldName(IrDocument document, OpenSite site)
    {
        if (site.Selector.Path.Count != 1)
        {
            throw new NotSupportedException(
                "C++ open-type Binding MVP supports only a single-component local selector path.");
        }

        var keyComponent = site.Container.Fields.Single(f => f.Name == site.Selector.Path[0]);
        var (keyModule, keyType) = ResolveAliasTarget(document, site.Container.Module, keyComponent.Type);
        if (keyType is not OidType)
        {
            throw new NotSupportedException(
                "C++ open-type Binding MVP supports only OBJECT IDENTIFIER selectors.");
        }

        _ = keyModule;
        return FieldName(keyComponent);
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

    private string OpenBindingKeyExpression(IrDocument document, IrModule module, string key)
    {
        foreach (var definition in module.Values)
        {
            if (definition.Value is IrOidValue oid &&
                string.Equals(oid.Value, key, StringComparison.Ordinal) &&
                IrOptions.ShouldGenerate(definition.Options))
            {
                return $"{OidCatalogName(module)}::{CppNames.SanitizeTypeName(definition.Name)}_oid()";
            }
        }

        return $"asn1kit::oid::parse(\"{CppNames.EscapeString(key)}\")";
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

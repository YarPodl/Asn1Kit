using System.Text;
using System.Text.Json;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.CSharp;

public sealed partial class CSharpBackend
{
    private sealed record OpenTypeUseSite(
        IrComponent Field,
        IrOpenTypeUse Use,
        string ContainerOwner,
        string ContainerType,
        IrComponent OpenField,
        IrComponent KeyField,
        IrModule KeyModule,
        TypeExpr KeyType,
        bool ContainerValueType,
        bool FieldOptional,
        bool OpenOptional,
        bool Lazy,
        bool Retained);

    private sealed record OpenTypeUseMember(IrOpenTypeBinding Binding, int Index, string Name)
    {
        public string CodecExpression { get; set; } = "";
    }

    private sealed record OpenTypeUsePlan(
        string Owner,
        OpenTypeUseSite Site,
        string Method,
        string CatalogStem,
        string BindingStem,
        string ContainerDecodeMethod,
        bool SharedCatalog,
        IReadOnlyList<OpenTypeUseMember> Members);

    private readonly Dictionary<string, List<OpenTypeUsePlan>> _openTypeUsePlans =
        new(StringComparer.Ordinal);

    private IEnumerable<OpenTypeUseSite> OpenTypeUseSites(IrDocument document, IrModule module,
        IReadOnlyList<IrComponent> fields)
    {
        foreach (var field in fields)
        {
            var current = field.Type;
            var currentModule = module;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current is RefType reference)
            {
                var resolved = FindWithModule(document, currentModule, reference);
                if (resolved is null) break;
                var key = resolved.Value.Module.Name + "." + reference.Name;
                if (!visited.Add(key)) break;
                foreach (var use in reference.OpenTypes ?? Enumerable.Empty<IrOpenTypeUse>())
                {
                    if (use.Path.Count != 1 || use.Bindings.Count == 0 ||
                        resolved.Value.Def.Type is not SequenceType sequence)
                        continue;
                    var target = sequence.Components.FirstOrDefault(c => c.Name == use.Path[0]);
                    if (target?.Type is not AnyType any || any.Selector is not { Levels: 0, Path.Count: 1 } selector)
                        continue;
                    var keyField = sequence.Components.FirstOrDefault(c => c.Name == selector.Path[0]);
                    if (keyField is null) continue;
                    var (keyModule, keyType) = ResolveDefaultTarget(document, resolved.Value.Module, keyField.Type);
                    if (keyType is not OidType and not IntegerType) continue;
                    var containerOwner = IrOptions.CSharpTypeName(resolved.Value.Def.Options) ??
                                         SanitizeIdentifier(resolved.Value.Def.Name);
                    var containerType = resolved.Value.Module == module
                        ? containerOwner
                        : ModuleNamespace(resolved.Value.Module) + "." + containerOwner;
                    yield return new OpenTypeUseSite(
                        field,
                        use,
                        containerOwner,
                        containerType,
                        target,
                        keyField,
                        keyModule,
                        keyType,
                        IsCSharpValueTypeEmit(resolved.Value.Module, containerOwner, sequence),
                        OpenFieldOptional(field),
                        OpenFieldOptional(target),
                        ShouldEmitLazy(document, module, field.Type, field.Options),
                        ShouldEmitRetainEncoded(document, module, field.Type, field.Options));
                }
                current = resolved.Value.Def.Type;
                currentModule = resolved.Value.Module;
            }
        }
    }

    private void CollectOpenTypeUseNested(IrDocument document, IrModule module, string owner, TypeExpr type,
        Queue<(string Name, TypeExpr Type)> queue)
    {
        IReadOnlyList<IrComponent>? fields = type switch
        {
            SequenceType sequence => sequence.Components,
            SetType set => set.Components,
            _ => null
        };
        if (fields is null) return;
        foreach (var site in OpenTypeUseSites(document, module, fields))
        {
            for (var i = 0; i < site.Use.Bindings.Count; i++)
            {
                var hint = OpenTypeUseHint(site, i);
                OfferNested(document, module, owner + "_" + hint, site.Use.Bindings[i].Type, queue);
            }
        }
    }

    private static string OpenTypeUseHint(OpenTypeUseSite site, int index) =>
        SanitizeIdentifier(site.Field.Name) + "_" + SanitizeIdentifier(site.OpenField.Name) + "_" + (index + 1);

    private void PlanOpenTypeUses(IrDocument document, IrModule module,
        IReadOnlyList<(string Name, TypeExpr Type)> types, HashSet<string> names, OpenWrapperPlan wrappers)
    {
        _openTypeUsePlans.Clear();
        foreach (var (owner, type) in types)
        {
            var fields = OpenContainerFields(type);
            if (fields is null) continue;
            var methods = new HashSet<string>(StringComparer.Ordinal);
            foreach (var site in OpenTypeUseSites(document, module, fields))
            {
                var methodBase = PropertyName(site.Field, owner).TrimStart('@') +
                                 PropertyName(site.OpenField, site.ContainerOwner).TrimStart('@');
                var method = methodBase;
                for (var suffix = 2; !methods.Add(method); suffix++) method = methodBase + suffix;

                var matched = FindMatchingWrapperSite(wrappers, site);
                string catalogStem;
                string bindingStem;
                string containerDecode;
                bool shared;
                if (matched is not null)
                {
                    catalogStem = matched.CatalogStem!;
                    bindingStem = matched.BindingStem!;
                    containerDecode = matched.DecodeMethod;
                    shared = true;
                }
                else
                {
                    var stemBase = PreferredOpenTypeUseStem(site, owner);
                    catalogStem = stemBase;
                    for (var suffix = 2; !ReserveOpenTypeUseBindingNames(catalogStem, names); suffix++)
                        catalogStem = stemBase + suffix;
                    bindingStem = catalogStem;
                    containerDecode = "TryDecode" + PropertyName(site.OpenField, site.ContainerOwner).TrimStart('@');
                    shared = false;
                }

                var members = new List<OpenTypeUseMember>();
                var memberNames = new HashSet<string>(StringComparer.Ordinal) { "Create" };
                for (var i = 0; i < site.Use.Bindings.Count; i++)
                {
                    var binding = site.Use.Bindings[i];
                    var baseName = OpenBindingIdentifier(binding);
                    var name = baseName;
                    for (var suffix = 2; !ReserveOpenBindingMemberName(name, memberNames); suffix++)
                        name = baseName + suffix;
                    members.Add(new OpenTypeUseMember(binding, i, name));
                }

                if (!_openTypeUsePlans.TryGetValue(owner, out var plans))
                    _openTypeUsePlans.Add(owner, plans = new List<OpenTypeUsePlan>());
                plans.Add(new OpenTypeUsePlan(owner, site, method, catalogStem, bindingStem, containerDecode,
                    shared, members));
            }
        }
    }

    private static string PreferredOpenTypeUseStem(OpenTypeUseSite site, string owner)
    {
        var openField = PropertyName(site.OpenField, site.ContainerOwner).TrimStart('@');
        if (!string.IsNullOrEmpty(site.Use.Table))
            return SanitizeIdentifier(site.Use.Table) + openField;
        return owner.Split('.').Last() + PropertyName(site.Field, owner).TrimStart('@') + openField;
    }

    private static OpenWrapperSite? FindMatchingWrapperSite(OpenWrapperPlan wrappers, OpenTypeUseSite use)
    {
        var useBindings = SerializeOpenTypeUseBindings(use.Use);
        var useContainer = use.ContainerType.Split('.').Last();
        foreach (var site in wrappers.Sites)
        {
            if (!string.Equals(site.Container.CsType.Split('.').Last(), useContainer, StringComparison.Ordinal))
                continue;
            if (!string.Equals(site.Field.Name, use.OpenField.Name, StringComparison.Ordinal)) continue;
            if (site.Selector.Levels != 0 || site.Selector.Path.Count != 1) continue;
            if (site.CatalogStem is null || site.BindingStem is null || site.DecodeMethod.Length == 0) continue;
            var wrapperBindings = string.Join("\n", site.Wrappers.Select(static wrapper =>
                wrapper.Binding.Key + "\t" + (wrapper.Binding.Name ?? "") + "\t" +
                JsonSerializer.Serialize(wrapper.Binding.Type, IrSerializer.JsonOptions)));
            if (!string.Equals(wrapperBindings, useBindings, StringComparison.Ordinal)) continue;
            return site;
        }
        return null;
    }

    private static string SerializeOpenTypeUseBindings(IrOpenTypeUse use) =>
        string.Join("\n", use.Bindings.Select(static binding =>
            binding.Key + "\t" + (binding.Name ?? "") + "\t" +
            JsonSerializer.Serialize(binding.Type, IrSerializer.JsonOptions)));

    private static bool ReserveOpenTypeUseBindingNames(string stem, HashSet<string> names)
    {
        var generated = new[] { stem + "Decoder", stem + "Encoder", stem + "Binding", stem + "Bindings" };
        if (generated.Any(names.Contains)) return false;
        foreach (var name in generated) names.Add(name);
        return true;
    }

    private void AssignOpenTypeUseMemberCodecs(IrDocument document, IrModule module)
    {
        foreach (var plan in _openTypeUsePlans.Values.SelectMany(static plans => plans))
        {
            if (plan.SharedCatalog) continue;
            foreach (var member in plan.Members)
            {
                var hint = OpenTypeUseHint(plan.Site, member.Index);
                var valueType = CsType(document, module, plan.Owner, hint, member.Binding.Type, false);
                var preferred = member.Name == valueType.Split('.').Last()
                    ? member.Name + "Codec"
                    : member.Name;
                member.CodecExpression = ResolveModuleOpenTypeCodec(
                    document, module, preferred, plan.Owner, hint, member.Binding.Type);
            }
        }
    }

    private void EmitOpenTypeUseDescriptors(StringBuilder sb, IrDocument document, IrModule module)
    {
        var plans = _openTypeUsePlans.Values.SelectMany(static plans => plans)
            .Where(static plan => !plan.SharedCatalog)
            .GroupBy(static plan => plan.CatalogStem, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
        if (plans.Count == 0) return;

        foreach (var plan in plans)
        {
            var keyType = plan.Site.KeyType is OidType ? "Asn1Oid" : "BigInteger";
            var keyName = keyType == "Asn1Oid" ? "Oid" : "Key";
            var stem = plan.CatalogStem;
            sb.AppendLine($"public sealed record {stem}Binding<T>({keyType} {keyName}, Func<Asn1Any, T> Decoder, Func<T, Asn1Any> Encoder);");
            sb.AppendLine();
            sb.AppendLine($"public static class {stem}Bindings");
            sb.AppendLine("{");
            for (var index = 0; index < plan.Members.Count; index++)
            {
                var member = plan.Members[index];
                var hint = OpenTypeUseHint(plan.Site, member.Index);
                var csType = CsType(document, module, plan.Owner, hint, member.Binding.Type, false);
                var codec = member.CodecExpression;
                if (index > 0) sb.AppendLine();
                sb.AppendLine($"    public static {stem}Binding<{csType}> {member.Name} {{ get; }} =");
                sb.AppendLine(
                    $"        new({OpenTypeUseKeyValue(document, module, plan.Site, member.Binding.Key)}, {codec}.Decode, {codec}.Encode);");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private string OpenTypeUseKeyValue(IrDocument document, IrModule module, OpenTypeUseSite site, string key)
    {
        if (site.KeyType is OidType)
            return KnownOidExpression(document, module, key) ??
                   $"Asn1Oid.Parse(\"{EscapeCSharpString(key)}\")";
        return $"BigInteger.Parse(\"{EscapeCSharpString(key)}\", CultureInfo.InvariantCulture)";
    }

    /// <summary>
    /// Instance Uses forwarders were removed: callers use carrier TryDecode/Set on the nested
    /// open-type container (and array.TryGet for OF). Non-shared catalogs still emit via
    /// <see cref="EmitOpenTypeUseDescriptors"/>.
    /// </summary>
    private void EmitOpenTypeUseMethods(StringBuilder sb, IrDocument document, IrModule module, string owner)
    {
    }

}

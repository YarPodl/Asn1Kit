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
                string stem;
                string containerDecode;
                bool shared;
                if (matched is not null)
                {
                    stem = matched.DecodeBindingStem!;
                    containerDecode = matched.DecodeMethod;
                    shared = true;
                }
                else
                {
                    var stemBase = PreferredOpenTypeUseStem(site, owner);
                    stem = stemBase;
                    for (var suffix = 2; !ReserveOpenTypeUseBindingNames(stem, names); suffix++)
                        stem = stemBase + suffix;
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
                plans.Add(new OpenTypeUsePlan(owner, site, method, stem, containerDecode, shared, members));
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
            if (site.DecodeBindingStem is null || site.DecodeMethod.Length == 0) continue;
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
            .GroupBy(static plan => plan.BindingStem, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
        if (plans.Count == 0) return;

        foreach (var plan in plans)
        {
            var keyType = plan.Site.KeyType is OidType ? "Asn1Oid" : "BigInteger";
            var keyName = keyType == "Asn1Oid" ? "Oid" : "Key";
            var keyParameter = CamelCaseIdentifier(keyName);
            var stem = plan.BindingStem;
            sb.AppendLine($"public sealed record {stem}Binding<T>({keyType} {keyName}, Func<Asn1Any, T> Decoder, Func<T, Asn1Any> Encoder);");
            sb.AppendLine();
            sb.AppendLine($"public static class {stem}Bindings");
            sb.AppendLine("{");
            sb.AppendLine($"    public static {stem}Binding<T> Create<T>({keyType} {keyParameter}, Func<Asn1Any, T> decoder, Func<T, Asn1Any> encoder) =>");
            sb.AppendLine($"        new({keyParameter}, decoder, encoder);");
            foreach (var member in plan.Members)
            {
                var hint = OpenTypeUseHint(plan.Site, member.Index);
                var csType = CsType(document, module, plan.Owner, hint, member.Binding.Type, false);
                var codec = member.CodecExpression;
                sb.AppendLine();
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

    private void EmitOpenTypeUseMethods(StringBuilder sb, IrDocument document, IrModule module, string owner)
    {
        if (!_openTypeUsePlans.TryGetValue(owner, out var plans)) return;
        foreach (var plan in plans)
        {
            if (plan.SharedCatalog)
            {
                EmitOpenTypeUseForwarders(sb, document, module, plan);
                continue;
            }

            var site = plan.Site;
            var fieldName = PropertyName(site.Field, owner);
            var openName = PropertyName(site.OpenField, site.ContainerOwner);
            var keyName = PropertyName(site.KeyField, site.ContainerOwner);
            var descriptorKey = site.KeyType is OidType ? "Oid" : "Key";
            sb.AppendLine();
            sb.AppendLine($"    public bool TryDecode{plan.Method}<T>({plan.BindingStem}Binding<T> binding, out T value)");
            sb.AppendLine("    {");
            sb.AppendLine("        value = default!;");
            sb.AppendLine("        ArgumentNullException.ThrowIfNull(binding);");
            EmitOpenTypeUseContainer(sb, plan, fieldName, returnsBoolean: true);
            if (site.OpenOptional)
                sb.AppendLine($"        if (container.{openName} is not {{ }} raw) return false;");
            else
                sb.AppendLine($"        var raw = container.{openName};");
            sb.AppendLine($"        if (!({OpenTypeUseKeyComparison(document, site, "container." + keyName, "binding." + descriptorKey)})) return false;");
            sb.AppendLine("        value = binding.Decoder(raw);");
            sb.AppendLine("        return true;");
            sb.AppendLine("    }");

            foreach (var member in plan.Members)
            {
                if (UnwrapAliases(document, module, member.Binding.Type) is NullType) continue;
                var hint = OpenTypeUseHint(site, member.Index);
                var csType = CsType(document, module, owner, hint, member.Binding.Type, false);
                sb.AppendLine();
                sb.AppendLine($"    public bool TryDecode{plan.Method}{member.Name}(out {csType} value) =>");
                sb.AppendLine($"        TryDecode{plan.Method}({plan.BindingStem}Bindings.{member.Name}, out value);");
            }

            sb.AppendLine();
            sb.AppendLine($"    public void Set{plan.Method}<T>({plan.BindingStem}Binding<T> binding, T value)");
            sb.AppendLine("    {");
            sb.AppendLine("        ArgumentNullException.ThrowIfNull(binding);");
            EmitOpenTypeUseContainer(sb, plan, fieldName, returnsBoolean: false);
            sb.AppendLine($"        if (!({OpenTypeUseKeyComparison(document, site, "container." + keyName, "binding." + descriptorKey)}))");
            sb.AppendLine("            throw new ArgumentException(\"Descriptor key does not match the selected open-type binding.\", nameof(binding));");
            sb.AppendLine($"        container.{openName} = binding.Encoder(value);");
            EmitOpenTypeUseContainerAssignment(sb, plan, fieldName);
            sb.AppendLine("    }");

            foreach (var member in plan.Members)
            {
                var hint = OpenTypeUseHint(site, member.Index);
                var csType = CsType(document, module, owner, hint, member.Binding.Type, false);
                sb.AppendLine();
                if (UnwrapAliases(document, module, member.Binding.Type) is NullType)
                {
                    sb.AppendLine($"    public void Set{plan.Method}{member.Name}() =>");
                    sb.AppendLine($"        Set{plan.Method}({plan.BindingStem}Bindings.{member.Name}, Asn1Null.Value);");
                }
                else
                {
                    sb.AppendLine($"    public void Set{plan.Method}{member.Name}({csType} value) =>");
                    sb.AppendLine($"        Set{plan.Method}({plan.BindingStem}Bindings.{member.Name}, value);");
                }
            }
        }
    }

    private void EmitOpenTypeUseForwarders(StringBuilder sb, IrDocument document, IrModule module,
        OpenTypeUsePlan plan)
    {
        var site = plan.Site;
        var fieldName = PropertyName(site.Field, plan.Owner);
        var setMethod = "Set" + plan.ContainerDecodeMethod["TryDecode".Length..];
        sb.AppendLine();
        sb.AppendLine($"    public bool TryDecode{plan.Method}<T>({plan.BindingStem}Binding<T> binding, out T value)");
        sb.AppendLine("    {");
        sb.AppendLine("        value = default!;");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(binding);");
        EmitOpenTypeUseContainer(sb, plan, fieldName, returnsBoolean: true);
        sb.AppendLine($"        return container.{plan.ContainerDecodeMethod}(binding, out value);");
        sb.AppendLine("    }");

        foreach (var member in plan.Members)
        {
            if (UnwrapAliases(document, module, member.Binding.Type) is NullType) continue;
            var hint = OpenTypeUseHint(site, member.Index);
            var csType = CsType(document, module, plan.Owner, hint, member.Binding.Type, false);
            sb.AppendLine();
            sb.AppendLine($"    public bool TryDecode{plan.Method}{member.Name}(out {csType} value) =>");
            sb.AppendLine($"        TryDecode{plan.Method}({plan.BindingStem}Bindings.{member.Name}, out value);");
        }

        sb.AppendLine();
        sb.AppendLine($"    public void Set{plan.Method}<T>({plan.BindingStem}Binding<T> binding, T value)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(binding);");
        EmitOpenTypeUseContainer(sb, plan, fieldName, returnsBoolean: false);
        if (site.ContainerValueType)
            sb.AppendLine($"        container.{setMethod}(binding, value);");
        else
            sb.AppendLine($"        container.{setMethod}(binding, value);");
        EmitOpenTypeUseContainerAssignment(sb, plan, fieldName);
        sb.AppendLine("    }");

        foreach (var member in plan.Members)
        {
            var hint = OpenTypeUseHint(site, member.Index);
            var csType = CsType(document, module, plan.Owner, hint, member.Binding.Type, false);
            sb.AppendLine();
            if (UnwrapAliases(document, module, member.Binding.Type) is NullType)
            {
                sb.AppendLine($"    public void Set{plan.Method}{member.Name}() =>");
                sb.AppendLine($"        Set{plan.Method}({plan.BindingStem}Bindings.{member.Name}, Asn1Null.Value);");
            }
            else
            {
                sb.AppendLine($"    public void Set{plan.Method}{member.Name}({csType} value) =>");
                sb.AppendLine($"        Set{plan.Method}({plan.BindingStem}Bindings.{member.Name}, value);");
            }
        }
    }

    private void EmitOpenTypeUseContainer(StringBuilder sb, OpenTypeUsePlan plan, string fieldName,
        bool returnsBoolean)
    {
        var missing = returnsBoolean
            ? "return false;"
            : $"throw new Asn1Exception(\"Missing {fieldName}.\");";
        var site = plan.Site;
        if (site.Lazy)
        {
            sb.AppendLine($"        if ({fieldName} is null) {missing}");
            sb.AppendLine($"        var container = {fieldName}.Value;");
        }
        else if (site.Retained)
        {
            if (site.FieldOptional)
            {
                sb.AppendLine($"        if ({fieldName} is not {{ }} stored) {missing}");
                sb.AppendLine("        var container = stored.Value;");
            }
            else sb.AppendLine($"        var container = {fieldName}.Value;");
        }
        else if (site.ContainerValueType && site.FieldOptional)
        {
            sb.AppendLine($"        if ({fieldName} is not {{ }} container) {missing}");
        }
        else
        {
            if (!site.ContainerValueType) sb.AppendLine($"        if ({fieldName} is null) {missing}");
            sb.AppendLine($"        var container = {fieldName};");
        }
    }

    private static void EmitOpenTypeUseContainerAssignment(StringBuilder sb, OpenTypeUsePlan plan,
        string fieldName)
    {
        if (plan.Site.Lazy)
            sb.AppendLine($"        {fieldName} = Asn1Lazy<{plan.Site.ContainerType}>.FromValue(container);");
        else if (plan.Site.Retained)
            sb.AppendLine($"        {fieldName} = new Asn1Value<{plan.Site.ContainerType}>(container);");
        else
            sb.AppendLine($"        {fieldName} = container;");
    }

    private string OpenTypeUseKeyComparison(IrDocument document, OpenTypeUseSite site,
        string actual, string expected)
    {
        if (site.KeyType is OidType) return actual + ".Equals(" + expected + ")";
        if ((TryResolveIntegerRepresentation(document, site.KeyModule, site.KeyType) ??
             IrOptions.IntegerRepresentations.Der) == IrOptions.IntegerRepresentations.Der)
            actual += ".ToBigInteger()";
        return actual + " == " + expected;
    }
}

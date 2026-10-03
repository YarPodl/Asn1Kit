using System.Text;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.CSharp;

public sealed partial class CSharpBackend
{
    private sealed record OpenTypeUseSite(IrComponent Field, IrOpenTypeUse Use, string Key, bool Optional);

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
                    if (use.Path.Count != 1 || use.Bindings.Count == 0 || resolved.Value.Def.Type is not SequenceType sequence)
                        continue;
                    var target = sequence.Components.FirstOrDefault(c => c.Name == use.Path[0]);
                    if (target?.Type is not AnyType any || any.Selector is not { Levels: 0, Path.Count: 1 } selector)
                        continue;
                    if (sequence.Components.All(c => c.Name != selector.Path[0])) continue;
                    yield return new OpenTypeUseSite(field, use, selector.Path[0], target.Optional);
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
        SanitizeIdentifier(site.Field.Name) + "_" + SanitizeIdentifier(site.Use.Path[0]) + "_" + (index + 1);

    private void EmitOpenTypeUseMethods(StringBuilder sb, IrDocument document, IrModule module, string owner,
        IReadOnlyList<IrComponent> fields)
    {
        foreach (var site in OpenTypeUseSites(document, module, fields))
        {
            var fieldName = PropertyName(site.Field, owner);
            var openName = SanitizeIdentifier(site.Use.Path[0]);
            var keyName = SanitizeIdentifier(site.Key);
            var method = SanitizeIdentifier(site.Field.Name) + openName;
            sb.AppendLine();
            sb.AppendLine($"    public bool TryDecode{method}<T>(out T value)");
            sb.AppendLine("    {");
            sb.AppendLine("        value = default!;");
            if (site.Optional)
                sb.AppendLine($"        if ({fieldName} is null || {fieldName}.{openName} is not {{ }} raw) return false;");
            else
            {
                sb.AppendLine($"        if ({fieldName} is null) return false;");
                sb.AppendLine($"        var raw = {fieldName}.{openName};");
            }
            sb.AppendLine($"        switch ({fieldName}.{keyName}.ToString())");
            sb.AppendLine("        {");
            for (var i = 0; i < site.Use.Bindings.Count; i++)
            {
                var binding = site.Use.Bindings[i];
                var hint = OpenTypeUseHint(site, i);
                var csType = CsType(document, module, owner, hint, binding.Type, false);
                sb.AppendLine($"            case \"{EscapeCSharpString(binding.Key)}\":");
                sb.AppendLine("            {");
                sb.AppendLine($"                if (typeof(T) != typeof({csType})) return false;");
                sb.AppendLine("                var inner = new Asn1Reader(raw.EncodedMemory);");
                sb.Append("                var decoded = ");
                EmitDecodeExpr(sb, document, module, owner, hint, binding.Type, "inner");
                sb.AppendLine(";");
                sb.AppendLine("                inner.ThrowIfNotEmpty();");
                sb.AppendLine("                value = (T)(object)decoded;");
                sb.AppendLine("                return true;");
                sb.AppendLine("            }");
            }
            sb.AppendLine("            default: return false;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            sb.AppendLine();
            sb.AppendLine($"    public void Set{method}<T>(T value)");
            sb.AppendLine("    {");
            sb.AppendLine($"        if ({fieldName} is null) throw new Asn1Exception(\"Missing {fieldName}.\");");
            sb.AppendLine($"        switch ({fieldName}.{keyName}.ToString())");
            sb.AppendLine("        {");
            for (var i = 0; i < site.Use.Bindings.Count; i++)
            {
                var binding = site.Use.Bindings[i];
                var hint = OpenTypeUseHint(site, i);
                var csType = CsType(document, module, owner, hint, binding.Type, false);
                sb.AppendLine($"            case \"{EscapeCSharpString(binding.Key)}\":");
                sb.AppendLine("            {");
                sb.AppendLine($"                if (value is not {csType} typed) throw new ArgumentException(\"Value type does not match the selected open-type binding.\", nameof(value));");
                sb.AppendLine("                var writer = new Asn1Writer();");
                EmitEncodeValue(sb, document, module, owner, hint, binding.Type, "                ", "writer", "typed");
                sb.AppendLine($"                {fieldName}.{openName} = new Asn1Any(writer.Encode());");
                sb.AppendLine("                return;");
                sb.AppendLine("            }");
            }
            sb.AppendLine("            default: throw new Asn1Exception(\"Unknown open-type key.\");");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
    }
}

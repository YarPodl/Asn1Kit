using System.Text;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.Cpp;

public sealed partial class CppBackend
{
    private void EmitExtensionGroupChecks(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string owner,
        IReadOnlyList<IrComponent> fields,
        string indent,
        string? target)
    {
        foreach (var group in fields.Where(f => f.ExtensionGroup.HasValue).GroupBy(f => f.ExtensionGroup))
        {
            var required = group.Where(f => !f.Optional && f.Default is null).ToArray();
            if (required.Length == 0)
            {
                continue;
            }

            string Access(IrComponent field)
            {
                var name = FieldName(field);
                return target is null ? "this->" + name : target + "." + name;
            }

            var present = string.Join(
                " || ",
                group.Select(f => f.Default is null
                    ? FieldPresentExpression(f, Access(f))
                    : NeedsDefaultHelper(f)
                        ? "!" + IsDefaultHelperName(f) + "(" + Access(f) + ")"
                        : DefaultNotEqualExpression(document, module, owner, f, Access(f))));
            var missing = string.Join(
                " || ",
                required.Select(f => FieldAbsentExpression(f, Access(f))));
            sb.AppendLine(
                $"{indent}if (({present}) && ({missing})) throw asn1kit::exception(\"Incomplete extension group {group.Key}.\");");
        }
    }

    private static string FieldPresentExpression(IrComponent field, string access)
    {
        if ((field.Optional || field.ExtensionAddition == true) && field.Default is null)
        {
            return access + ".has_value()";
        }

        return "true";
    }

    private static string FieldAbsentExpression(IrComponent field, string access)
    {
        if ((field.Optional || field.ExtensionAddition == true) && field.Default is null)
        {
            return "!" + access + ".has_value()";
        }

        return "false";
    }

    private void EmitSequenceExtensionDecode(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string owner,
        SequenceType sequence,
        string indent)
    {
        var fields = sequence.Components;
        var start = fields.FindIndex(f => f.ExtensionAddition == true);
        if (start < 0)
        {
            start = fields.Count;
        }

        var end = start;
        while (end < fields.Count && fields[end].ExtensionAddition == true)
        {
            end++;
        }

        foreach (var field in fields.Take(start))
        {
            EmitDecodeField(sb, document, module, owner, field, fields, indent, "inner", "value");
        }

        sb.AppendLine($"{indent}auto next_extension = {start.ToString(System.Globalization.CultureInfo.InvariantCulture)};");
        sb.AppendLine($"{indent}while (true) {{");
        sb.AppendLine($"{indent}    asn1kit::tag extension_tag;");
        sb.AppendLine($"{indent}    if (!inner.try_peek_tag(extension_tag)) break;");
        if (end < fields.Count)
        {
            var postRoot = string.Join(
                " || ",
                fields.Skip(end).Select(f => PeekMatchExpr(document, module, f.Type, "extension_tag")));
            sb.AppendLine($"{indent}    if ({postRoot}) break;");
        }

        for (var i = start; i < end; i++)
        {
            var field = fields[i];
            var cond = i == start ? "if" : "else if";
            sb.AppendLine(
                $"{indent}    {cond} ({PeekMatchExpr(document, module, field.Type, "extension_tag")}) {{");
            sb.AppendLine(
                $"{indent}        if (next_extension > {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}) throw asn1kit::exception(\"Duplicate or unordered extension component '{field.Name}'.\");");
            EmitDecodeAssign(
                sb,
                document,
                module,
                owner,
                field.Name,
                field.Type,
                indent + "        ",
                "inner",
                "value." + FieldName(field),
                fieldOptions: field.Options);
            sb.AppendLine(
                $"{indent}        next_extension = {(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)};");
            sb.AppendLine($"{indent}    }}");
        }

        sb.AppendLine($"{indent}    {(end > start ? "else " : "")}{{");
        sb.AppendLine($"{indent}        (void)inner.read_any();");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");

        foreach (var field in fields.Skip(end))
        {
            EmitDecodeField(sb, document, module, owner, field, fields, indent, "inner", "value");
        }

        EmitExtensionGroupChecks(sb, document, module, owner, fields, indent, "value");
    }
}

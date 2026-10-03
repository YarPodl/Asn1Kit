using System.Text;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.CSharp;

public sealed partial class CSharpBackend
{
    private static void EmitExtensionProperty(StringBuilder sb) =>
        sb.AppendLine("    public IReadOnlyList<Asn1Extension> UnknownExtensions { get; set; } = Array.Empty<Asn1Extension>();");

    private void EmitExtensionGroupChecks(StringBuilder sb, IrDocument document, IrModule module, string owner,
        IReadOnlyList<IrComponent> fields, string indent, string? target)
    {
        foreach (var group in fields.Where(f => f.ExtensionGroup.HasValue).GroupBy(f => f.ExtensionGroup))
        {
            var required = group.Where(f => !f.Optional && f.Default is null).ToArray();
            if (required.Length == 0) continue;
            string Access(IrComponent field) => (target is null ? "" : target + ".") + PropertyName(field, owner);
            var present = string.Join(" || ", group.Select(f => f.Default is null
                ? Access(f) + " != null"
                : DefaultNotEqualExpression(document, module, owner, f, Access(f))));
            var missing = string.Join(" || ", required.Select(f => Access(f) + " == null"));
            sb.AppendLine($"{indent}if (({present}) && ({missing})) throw new Asn1Exception(\"Incomplete extension group {group.Key}.\");");
        }
    }

    private void EmitSequenceExtensionDecode(StringBuilder sb, IrDocument document, IrModule module,
        string owner, SequenceType sequence, string indent)
    {
        var fields = sequence.Components;
        var start = fields.FindIndex(f => f.ExtensionAddition == true);
        if (start < 0) start = fields.Count;
        var end = start;
        while (end < fields.Count && fields[end].ExtensionAddition == true) end++;
        foreach (var field in fields.Take(start))
            EmitDecodeField(sb, document, module, owner, field, fields, indent, "reader", "value");
        sb.AppendLine($"{indent}List<Asn1Extension>? unknownExtensions = null;");
        sb.AppendLine($"{indent}var nextExtension = {start};");
        sb.AppendLine($"{indent}while (reader.TryPeekTag(out var extensionTag))");
        sb.AppendLine($"{indent}{{");
        if (end < fields.Count)
            sb.AppendLine($"{indent}    if ({string.Join(" || ", fields.Skip(end).Select(f => PeekMatchExpr(document, module, f.Type, "extensionTag")))}) break;");
        for (var i = start; i < end; i++)
        {
            var field = fields[i];
            sb.AppendLine($"{indent}    {(i == start ? "if" : "else if")} ({PeekMatchExpr(document, module, field.Type, "extensionTag")})");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        if (nextExtension > {i}) throw new Asn1Exception(\"Duplicate or unordered extension component '{field.Name}'.\");");
            EmitDecodeAssign(sb, document, module, owner, field.Name, field.Type, fields, indent + "        ", "reader", "value." + PropertyName(field, owner), targetObject: "value", fieldOptions: field.Options);
            sb.AppendLine($"{indent}        nextExtension = {i + 1};");
            sb.AppendLine($"{indent}    }}");
        }
        sb.AppendLine($"{indent}    {(end > start ? "else " : "")}{{");
        sb.AppendLine($"{indent}        (unknownExtensions ??= new List<Asn1Extension>()).Add(new Asn1Extension(nextExtension, reader.ReadAny()));");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}if (unknownExtensions != null) value.UnknownExtensions = unknownExtensions;");
        foreach (var field in fields.Skip(end))
            EmitDecodeField(sb, document, module, owner, field, fields, indent, "reader", "value");
        EmitExtensionGroupChecks(sb, document, module, owner, fields, indent, "value");
    }

    private static void EmitUnknownExtensionEncode(StringBuilder sb, int position, string indent)
    {
        sb.AppendLine($"{indent}while (unknownIndex < UnknownExtensions.Count && UnknownExtensions[unknownIndex].Position <= {position})");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    writer.WriteAny(UnknownExtensions[unknownIndex++].Value);");
        sb.AppendLine($"{indent}}}");
    }
}

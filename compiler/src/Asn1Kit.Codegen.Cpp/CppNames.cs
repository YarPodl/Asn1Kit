using System.Globalization;
using System.Text;

namespace Asn1Kit.Codegen.Cpp;

internal static class CppNames
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break",
        "case", "catch", "char", "char8_t", "char16_t", "char32_t", "class", "compl", "concept",
        "const", "consteval", "constexpr", "constinit", "const_cast", "continue", "co_await",
        "co_return", "co_yield", "decltype", "default", "delete", "do", "double", "dynamic_cast",
        "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto",
        "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq",
        "nullptr", "operator", "or", "or_eq", "private", "protected", "public", "register",
        "reinterpret_cast", "requires", "return", "short", "signed", "sizeof", "static",
        "static_assert", "static_cast", "struct", "switch", "template", "this", "thread_local",
        "throw", "true", "try", "typedef", "typeid", "typename", "union", "unsigned", "using",
        "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq",
        "encode", "decode", "decode_content", "encode_content", "match_tag", "kind", "value"
    };

    public static string SanitizeTypeName(string name)
    {
        var parts = name.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var result = string.Concat(parts.Select(p =>
            char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : "")));
        if (string.IsNullOrEmpty(result))
        {
            result = "Type";
        }

        return Keywords.Contains(result) ? result + "_" : result;
    }

    public static string SanitizeFieldName(string name)
    {
        var parts = name.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
            {
                continue;
            }

            if (i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(part[0]));
            if (part.Length > 1)
            {
                sb.Append(part[1..]);
            }
        }

        var result = sb.Length == 0 ? "field" : sb.ToString();
        if (char.IsDigit(result[0]))
        {
            result = "f_" + result;
        }

        return Keywords.Contains(result) ? result + "_" : result;
    }

    public static string SanitizeEnumMember(string name) => SanitizeFieldName(name);

    public static string EscapeString(string value)
    {
        var escaped = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '"':
                    escaped.Append("\\\"");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                default:
                    if (c < 0x20 || c > 0x7E)
                    {
                        escaped.Append("\\u")
                            .Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        escaped.Append(c);
                    }

                    break;
            }
        }

        return escaped.ToString();
    }

    public static string SanitizeNamespaceSegment(string part)
    {
        var cleaned = new string(part.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrEmpty(cleaned))
        {
            cleaned = "asn1";
        }

        if (char.IsDigit(cleaned[0]))
        {
            cleaned = "ns_" + cleaned;
        }

        return Keywords.Contains(cleaned) ? cleaned + "_" : cleaned;
    }

    public static string NamespaceOpen(string ns)
    {
        var parts = ns.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append("namespace ").Append(SanitizeNamespaceSegment(part)).Append(" {\n");
        }

        return sb.ToString();
    }

    public static string NamespaceClose(string ns)
    {
        var parts = ns.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            sb.Append("} // namespace ").Append(SanitizeNamespaceSegment(parts[i])).Append('\n');
        }

        return sb.ToString();
    }

    public static string IncludeGuard(string moduleName) =>
        "ASN1KIT_GENERATED_" + SanitizeTypeName(moduleName).ToUpperInvariant() + "_HPP";
}

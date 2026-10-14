using System.Globalization;
using System.Text;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.Cpp;

public sealed partial class CppBackend
{
    private void EmitSequence(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string typeName,
        SequenceType type)
    {
        EmitStructuredType(sb, document, module, typeName, type, type.Components, isSet: false);
    }

    private void EmitSet(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string typeName,
        SetType type)
    {
        EmitStructuredType(sb, document, module, typeName, type, type.Components, isSet: true);
    }

    private void EmitStructuredType(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string typeName,
        TypeExpr type,
        IReadOnlyList<IrComponent> components,
        bool isSet)
    {
        var extensible = type switch
        {
            SequenceType sequence => sequence.Extensible,
            SetType set => set.Extensible,
            _ => false
        };

        sb.AppendLine($"struct {typeName} {{");
        foreach (var field in components)
        {
            var optional = (field.Optional || field.ExtensionAddition == true) && field.Default is null;
            var fieldType = CppType(
                document, module, typeName, field.Name, field.Type, optional, field.Options);
            var name = FieldName(field);
            var init = FieldInitializer(document, module, typeName, field, optional);
            sb.AppendLine($"    {fieldType} {name}{init};");
        }

        sb.AppendLine();
        EmitStructuredDefaultHelpers(sb, document, module, typeName, components);
        var naturalTag = UniversalTag(document, module, type);
        sb.AppendLine("    static constexpr bool match_tag(const asn1kit::tag& t) {");
        sb.AppendLine($"        return t.matches_ignore_constructed({naturalTag});");
        sb.AppendLine("    }");
        sb.AppendLine();
        // Single overload with default tag satisfies asn1_decodable / asn1_encodable
        // (decode(r) / encode(w) and tagged override). CHOICE keeps two overloads.
        sb.AppendLine(
            $"    static {typeName} decode(asn1kit::reader& r, const asn1kit::tag& t = {naturalTag}) {{");
        sb.AppendLine($"        {typeName} value{{}};");
        if (isSet)
        {
            EmitSetDecodeBody(sb, document, module, typeName, components, extensible);
        }
        else if (extensible && type is SequenceType sequence)
        {
            sb.AppendLine("        r.with_sequence(t, [&](asn1kit::reader& inner) {");
            EmitSequenceExtensionDecode(sb, document, module, typeName, sequence, "            ");
            sb.AppendLine("        });");
        }
        else
        {
            sb.AppendLine("        r.with_sequence(t, [&](asn1kit::reader& inner) {");
            foreach (var field in components)
            {
                EmitDecodeField(sb, document, module, typeName, field, components, "            ", "inner", "value");
            }

            sb.AppendLine("        });");
        }

        sb.AppendLine("        return value;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine(
            $"    void encode(asn1kit::writer& w, const asn1kit::tag& t = {naturalTag}) const {{");
        var enter = isSet
            ? (extensible ? "enter_set_of" : "enter_set")
            : "enter_sequence";
        sb.AppendLine($"        auto scope = w.{enter}(t);");
        if (extensible)
        {
            EmitExtensionGroupChecks(sb, document, module, typeName, components, "        ", null);
        }

        var encodeFields = isSet
            ? SortedSetComponents(document, module, components)
            : components;
        foreach (var field in encodeFields)
        {
            EmitEncodeField(sb, document, module, typeName, field, "        ", "w", field.Options);
        }

        sb.AppendLine("    }");
        sb.AppendLine("};");
    }

    private void EmitSetDecodeBody(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string typeName,
        IReadOnlyList<IrComponent> components,
        bool extensible)
    {
        sb.AppendLine("        r.with_set(t, [&](asn1kit::reader& inner) {");
        foreach (var field in components)
        {
            if (!field.Optional || field.Default is not null)
            {
                sb.AppendLine($"            bool seen_{CppNames.SanitizeFieldName(field.Name)} = false;");
            }
        }

        sb.AppendLine("            while (!inner.eof()) {");
        sb.AppendLine("                asn1kit::tag peeked;");
        sb.AppendLine("                if (!inner.try_peek_tag(peeked)) {");
        sb.AppendLine("                    break;");
        sb.AppendLine("                }");
        var first = true;
        foreach (var field in components)
        {
            var prop = FieldName(field);
            var cond = first ? "if" : "} else if";
            first = false;
            sb.AppendLine(
                $"                {cond} ({PeekMatchExpr(document, module, field.Type, "peeked")}) {{");
            if (!field.Optional || field.Default is not null)
            {
                var seen = $"seen_{CppNames.SanitizeFieldName(field.Name)}";
                sb.AppendLine(
                    $"                    if ({seen}) throw asn1kit::exception(\"Duplicate SET component '{field.Name}'.\");");
                sb.AppendLine($"                    {seen} = true;");
            }
            else
            {
                sb.AppendLine(
                    $"                    if (value.{prop}.has_value()) throw asn1kit::exception(\"Duplicate SET component '{field.Name}'.\");");
            }

            EmitDecodeAssign(
                sb,
                document,
                module,
                typeName,
                field.Name,
                field.Type,
                "                    ",
                "inner",
                $"value.{prop}",
                fieldOptions: field.Options);
        }

        if (extensible)
        {
            sb.AppendLine("                } else {");
            sb.AppendLine("                    (void)inner.read_any();");
            sb.AppendLine("                }");
        }
        else
        {
            sb.AppendLine("                } else {");
            sb.AppendLine("                    throw asn1kit::exception(\"Unknown SET component.\");");
            sb.AppendLine("                }");
        }

        sb.AppendLine("            }");
        foreach (var field in components)
        {
            if (!field.Optional && field.ExtensionAddition != true && field.Default is null)
            {
                sb.AppendLine(
                    $"            if (!seen_{CppNames.SanitizeFieldName(field.Name)}) throw asn1kit::exception(\"Missing SET component '{field.Name}'.\");");
            }
            else if (field.Default is not null)
            {
                var prop = FieldName(field);
                sb.AppendLine($"            if (!seen_{CppNames.SanitizeFieldName(field.Name)}) {{");
                sb.AppendLine(
                    $"                value.{prop} = {DefaultExpression(document, module, typeName, field)};");
                sb.AppendLine("            }");
            }
        }

        if (extensible)
        {
            EmitExtensionGroupChecks(sb, document, module, typeName, components, "            ", "value");
        }

        sb.AppendLine("        });");
    }

    private void EmitChoice(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string typeName,
        ChoiceType type,
        bool deferCodecs = false)
    {
        if (IsSingleAlternativeChoice(type))
        {
            throw new InvalidOperationException("Single-alternative CHOICE must be collapsed before emit.");
        }

        var alts = type.Components;
        var kinds = alts.Select(a => CppNames.SanitizeEnumMember(a.Name)).ToList();
        var payloadTypes = alts
            .Select(a => CppType(document, module, typeName, a.Name, a.Type, optional: false))
            .ToList();
        // The kind selects the ASN.1 alternative; storage only distinguishes C++ types.
        var variantTypes = payloadTypes.Distinct(StringComparer.Ordinal).ToList();
        var variantIndices = payloadTypes.Select(t => variantTypes.IndexOf(t)).ToList();
        var singlePayload = variantTypes.Count == 1;
        var constexprFactories = !type.Extensible
            && alts.All(alt => IsConstexprChoicePayload(document, module, alt.Type));

        sb.AppendLine($"enum class {typeName}_kind {{");
        for (var i = 0; i < kinds.Count; i++)
        {
            var trailing = i + 1 < kinds.Count || type.Extensible ? "," : "";
            sb.AppendLine($"    {kinds[i]}{trailing}");
        }

        if (type.Extensible)
        {
            sb.AppendLine("    unknown");
        }

        sb.AppendLine("};");
        sb.AppendLine();

        sb.AppendLine($"struct {typeName} {{");
        sb.AppendLine($"    {typeName}_kind kind{{}};");
        var storageType = singlePayload ? variantTypes[0] : $"std::variant<{string.Join(", ", variantTypes)}>";
        sb.AppendLine($"    {storageType} value{{}};");
        if (type.Extensible)
        {
            sb.AppendLine("    std::optional<asn1kit::any> unknown{};");
        }

        sb.AppendLine();

        for (var i = 0; i < alts.Count; i++)
        {
            var factory = "from_" + kinds[i];
            var constexprKeyword = constexprFactories ? "constexpr " : "";
            sb.AppendLine($"    static {constexprKeyword}{typeName} {factory}({payloadTypes[i]} v) {{");
            sb.AppendLine($"        {typeName} result{{}};");
            sb.AppendLine($"        result.kind = {typeName}_kind::{kinds[i]};");
            if (singlePayload)
            {
                sb.AppendLine("        result.value = std::move(v);");
            }
            else
            {
                sb.AppendLine($"        result.value.emplace<{variantIndices[i]}>(std::move(v));");
            }

            sb.AppendLine("        return result;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        if (type.Extensible)
        {
            sb.AppendLine($"    static {typeName} from_unknown(asn1kit::any v) {{");
            sb.AppendLine($"        {typeName} result{{}};");
            sb.AppendLine($"        result.kind = {typeName}_kind::unknown;");
            sb.AppendLine("        result.unknown = std::move(v);");
            sb.AppendLine("        return result;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("    static constexpr bool match_tag(const asn1kit::tag& t) {");
        if (type.Tag is not null)
        {
            sb.AppendLine(
                $"        return t.matches_ignore_constructed({TagFromIr(type.Tag, constructed: true)});");
        }
        else if (alts.Count == 0)
        {
            sb.AppendLine("        (void)t;");
            sb.AppendLine("        return false;");
        }
        else
        {
            var parts = alts.Select(a => PeekMatchExpr(document, module, a.Type, "t")).ToList();
            sb.AppendLine(
                parts.Count == 1
                    ? $"        return {parts[0]};"
                    : $"        return ({string.Join(" || ", parts)});");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // Tagged CHOICE needs decode_content/encode_content so with_explicit does not
        // re-enter natural decode (which would peel the wrapper twice). Untagged CHOICE
        // puts alternatives directly in decode(r) / encode(w).
        if (deferCodecs)
        {
            if (type.Tag is not null)
            {
                sb.AppendLine($"    static {typeName} decode_content(asn1kit::reader& r);");
                sb.AppendLine($"    static {typeName} decode(asn1kit::reader& r);");
                sb.AppendLine($"    static {typeName} decode(asn1kit::reader& r, const asn1kit::tag& t);");
                sb.AppendLine("    void encode_content(asn1kit::writer& w) const;");
                sb.AppendLine("    void encode(asn1kit::writer& w) const;");
                sb.AppendLine("    void encode(asn1kit::writer& w, const asn1kit::tag& t) const;");
            }
            else
            {
                sb.AppendLine($"    static {typeName} decode(asn1kit::reader& r);");
                sb.AppendLine($"    static {typeName} decode(asn1kit::reader& r, const asn1kit::tag& t);");
                sb.AppendLine("    void encode(asn1kit::writer& w) const;");
                sb.AppendLine("    void encode(asn1kit::writer& w, const asn1kit::tag& t) const;");
            }
        }
        else if (type.Tag is not null)
        {
            var wrapperTag = TagFromIr(type.Tag, constructed: true);
            sb.AppendLine($"    static {typeName} decode_content(asn1kit::reader& r) {{");
            EmitChoiceDecodeAlternatives(
                sb, document, module, typeName, type, alts, kinds, payloadTypes);
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    static {typeName} decode(asn1kit::reader& r) {{");
            sb.AppendLine($"        return decode(r, {wrapperTag});");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine(
                $"    static {typeName} decode(asn1kit::reader& r, const asn1kit::tag& t) {{");
            sb.AppendLine($"        {typeName} value{{}};");
            sb.AppendLine("        r.with_explicit(t, [&](asn1kit::reader& inner) {");
            sb.AppendLine("            value = decode_content(inner);");
            sb.AppendLine("        });");
            sb.AppendLine("        return value;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void encode_content(asn1kit::writer& w) const {");
            EmitChoiceEncodeAlternatives(
                sb, document, module, typeName, type, alts, kinds, variantIndices, singlePayload);
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void encode(asn1kit::writer& w) const {");
            sb.AppendLine($"        encode(w, {wrapperTag});");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void encode(asn1kit::writer& w, const asn1kit::tag& t) const {");
            sb.AppendLine("        auto scope = w.enter_explicit(t);");
            sb.AppendLine("        encode_content(w);");
            sb.AppendLine("    }");
        }
        else
        {
            sb.AppendLine($"    static {typeName} decode(asn1kit::reader& r) {{");
            EmitChoiceDecodeAlternatives(
                sb, document, module, typeName, type, alts, kinds, payloadTypes);
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine(
                $"    static {typeName} decode(asn1kit::reader& r, const asn1kit::tag& t) {{");
            sb.AppendLine("        (void)t;");
            sb.AppendLine("        return decode(r);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void encode(asn1kit::writer& w) const {");
            EmitChoiceEncodeAlternatives(
                sb, document, module, typeName, type, alts, kinds, variantIndices, singlePayload);
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void encode(asn1kit::writer& w, const asn1kit::tag& t) const {");
            sb.AppendLine("        (void)t;");
            sb.AppendLine("        encode(w);");
            sb.AppendLine("    }");
        }

        sb.AppendLine("};");
    }

    private void EmitChoiceCodecDefinitions(
        StringBuilder sb, IrDocument document, IrModule module, string typeName, ChoiceType type)
    {
        var alts = type.Components;
        var kinds = alts.Select(a => CppNames.SanitizeEnumMember(a.Name)).ToList();
        var payloadTypes = alts
            .Select(a => CppType(document, module, typeName, a.Name, a.Type, optional: false))
            .ToList();
        var variantTypes = payloadTypes.Distinct(StringComparer.Ordinal).ToList();
        var variantIndices = payloadTypes.Select(t => variantTypes.IndexOf(t)).ToList();
        var singlePayload = variantTypes.Count == 1;

        if (type.Tag is not null)
        {
            var wrapperTag = TagFromIr(type.Tag, constructed: true);
            sb.AppendLine($"inline {typeName} {typeName}::decode_content(asn1kit::reader& r) {{");
            EmitChoiceDecodeAlternatives(sb, document, module, typeName, type, alts, kinds,
                payloadTypes);
            sb.AppendLine("}");
            sb.AppendLine($"inline {typeName} {typeName}::decode(asn1kit::reader& r) {{ return decode(r, {wrapperTag}); }}");
            sb.AppendLine($"inline {typeName} {typeName}::decode(asn1kit::reader& r, const asn1kit::tag& t) {{");
            sb.AppendLine($"    {typeName} value{{}};");
            sb.AppendLine("    r.with_explicit(t, [&](asn1kit::reader& inner) { value = decode_content(inner); });");
            sb.AppendLine("    return value;");
            sb.AppendLine("}");
            sb.AppendLine($"inline void {typeName}::encode_content(asn1kit::writer& w) const {{");
            EmitChoiceEncodeAlternatives(sb, document, module, typeName, type, alts, kinds,
                variantIndices, singlePayload);
            sb.AppendLine("}");
            sb.AppendLine($"inline void {typeName}::encode(asn1kit::writer& w) const {{ encode(w, {wrapperTag}); }}");
            sb.AppendLine($"inline void {typeName}::encode(asn1kit::writer& w, const asn1kit::tag& t) const {{");
            sb.AppendLine("    auto scope = w.enter_explicit(t);");
            sb.AppendLine("    encode_content(w);");
            sb.AppendLine("}");
        }
        else
        {
            sb.AppendLine($"inline {typeName} {typeName}::decode(asn1kit::reader& r) {{");
            EmitChoiceDecodeAlternatives(sb, document, module, typeName, type, alts, kinds,
                payloadTypes);
            sb.AppendLine("}");
            sb.AppendLine($"inline {typeName} {typeName}::decode(asn1kit::reader& r, const asn1kit::tag& t) {{");
            sb.AppendLine("    (void)t;");
            sb.AppendLine("    return decode(r);");
            sb.AppendLine("}");
            sb.AppendLine($"inline void {typeName}::encode(asn1kit::writer& w) const {{");
            EmitChoiceEncodeAlternatives(sb, document, module, typeName, type, alts, kinds,
                variantIndices, singlePayload);
            sb.AppendLine("}");
            sb.AppendLine($"inline void {typeName}::encode(asn1kit::writer& w, const asn1kit::tag& t) const {{");
            sb.AppendLine("    (void)t;");
            sb.AppendLine("    encode(w);");
            sb.AppendLine("}");
        }
    }

    private bool IsConstexprChoicePayload(
        IrDocument document,
        IrModule module,
        TypeExpr type)
    {
        var resolvedModule = module;
        var resolved = UnwrapAliases(document, ref resolvedModule, type);
        switch (resolved)
        {
            case BooleanType:
            case NullType:
            case TimeType:
            case EnumeratedType:
                return true;
            case IntegerType:
                return IsConstexprInteger(document, resolvedModule, type);
            case RefType reference:
            {
                var found = FindWithModule(document, resolvedModule, reference);
                if (found is null)
                {
                    return false;
                }

                if (found.Value.Def.Type is EnumeratedType)
                {
                    return true;
                }

                return found.Value.Def.Type is IntegerType
                    && IsConstexprInteger(document, found.Value.Module, reference);
            }
            default:
                return false;
        }
    }

    private bool IsConstexprInteger(IrDocument document, IrModule module, TypeExpr type) =>
        TryResolveIntegerRepresentation(document, module, type) is
            IrOptions.IntegerRepresentations.Int32 or
            IrOptions.IntegerRepresentations.UInt32 or
            IrOptions.IntegerRepresentations.Int64 or
            IrOptions.IntegerRepresentations.UInt64;

    private void EmitChoiceDecodeAlternatives(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string typeName,
        ChoiceType type,
        IReadOnlyList<IrComponent> alts,
        IReadOnlyList<string> kinds,
        IReadOnlyList<string> payloadTypes)
    {
        sb.AppendLine("        asn1kit::tag peeked;");
        sb.AppendLine(
            "        if (!r.try_peek_tag(peeked)) throw asn1kit::exception(\"Expected CHOICE alternative.\");");
        for (var i = 0; i < alts.Count; i++)
        {
            var alt = alts[i];
            var cond = i == 0 ? "if" : "} else if";
            sb.AppendLine($"        {cond} ({PeekMatchExpr(document, module, alt.Type, "peeked")}) {{");
            sb.AppendLine($"            {payloadTypes[i]} alt{{}};");
            EmitDecodeAssign(
                sb,
                document,
                module,
                typeName,
                alt.Name,
                alt.Type,
                "            ",
                "r",
                "alt");
            sb.AppendLine($"            return from_{kinds[i]}(std::move(alt));");
        }

        if (type.Extensible)
        {
            sb.AppendLine("        } else {");
            sb.AppendLine("            return from_unknown(r.read_any());");
            sb.AppendLine("        }");
        }
        else
        {
            sb.AppendLine("        } else {");
            sb.AppendLine("            throw asn1kit::exception(\"Unknown CHOICE alternative.\");");
            sb.AppendLine("        }");
        }
    }

    private void EmitChoiceEncodeAlternatives(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string typeName,
        ChoiceType type,
        IReadOnlyList<IrComponent> alts,
        IReadOnlyList<string> kinds,
        IReadOnlyList<int> variantIndices,
        bool singlePayload)
    {
        sb.AppendLine("        switch (kind) {");
        for (var i = 0; i < alts.Count; i++)
        {
            var alt = alts[i];
            sb.AppendLine($"        case {typeName}_kind::{kinds[i]}:");
            sb.AppendLine("            {");
            if (singlePayload)
            {
                sb.AppendLine("                const auto& alt = value;");
            }
            else
            {
                sb.AppendLine($"                const auto& alt = std::get<{variantIndices[i]}>(value);");
            }

            EmitEncodeValue(
                sb,
                document,
                module,
                typeName,
                alt.Name,
                alt.Type,
                "                ",
                "w",
                "alt");
            sb.AppendLine("                break;");
            sb.AppendLine("            }");
        }

        if (type.Extensible)
        {
            sb.AppendLine($"        case {typeName}_kind::unknown:");
            sb.AppendLine(
                "            if (!unknown.has_value()) throw asn1kit::exception(\"Missing unknown CHOICE value.\");");
            sb.AppendLine("            w.write_any(*unknown);");
            sb.AppendLine("            break;");
        }

        sb.AppendLine("        }");
    }

    private void EmitEnumerated(StringBuilder sb, string typeName, EnumeratedType type)
    {
        var useLong = type.Values.Any(v => v.Value is < int.MinValue or > int.MaxValue);
        var underlying = useLong ? " : std::int64_t" : "";
        sb.AppendLine($"enum class {typeName}{underlying} {{");
        for (var i = 0; i < type.Values.Count; i++)
        {
            var value = type.Values[i];
            var comma = i + 1 < type.Values.Count ? "," : "";
            sb.AppendLine(
                $"    {CppNames.SanitizeEnumMember(value.Name)} = {IntegerLiteral(value.Value)}{comma}");
        }

        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine($"inline void encode_{typeName}(asn1kit::writer& w, {typeName} value, const asn1kit::tag& t = asn1kit::tag::enumerated) {{");
        sb.AppendLine(
            $"    w.write_enumerated_decimal(t, std::to_string(static_cast<{(useLong ? "std::int64_t" : "std::int32_t")}>(value)));");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            $"inline {typeName} decode_{typeName}(asn1kit::reader& r, const asn1kit::tag& t = asn1kit::tag::enumerated) {{");
        sb.AppendLine("    const auto text = r.read_enumerated_decimal(t);");
        sb.AppendLine($"    const auto number = std::stoll(text);");
        foreach (var value in type.Values)
        {
            sb.AppendLine(
                $"    if (number == {IntegerLiteral(value.Value)}) return {typeName}::{CppNames.SanitizeEnumMember(value.Name)};");
        }

        sb.AppendLine("    throw asn1kit::exception(\"Unknown ENUMERATED value.\");");
        sb.AppendLine("}");
    }

    private void EmitNamedIntegerConstants(StringBuilder sb, string typeName, IntegerType type)
    {
        var useLong = type.NamedValues!.Any(v => v.Value is < int.MinValue or > int.MaxValue);
        var cppType = useLong ? "std::int64_t" : "std::int32_t";
        sb.AppendLine($"struct {typeName} {{");
        foreach (var value in type.NamedValues!)
        {
            sb.AppendLine(
                $"    static constexpr {cppType} {CppNames.SanitizeTypeName(value.Name)} = {IntegerLiteral(value.Value)};");
        }

        sb.AppendLine("};");
    }

    private void EmitNamedBitString(StringBuilder sb, string typeName, BitStringType type)
    {
        sb.AppendLine($"enum class {typeName}Flags : std::uint32_t {{");
        sb.AppendLine("    none = 0,");
        for (var i = 0; i < type.NamedBits!.Count; i++)
        {
            var bit = type.NamedBits[i];
            var comma = i + 1 < type.NamedBits.Count ? "," : "";
            sb.AppendLine(
                $"    {CppNames.SanitizeEnumMember(bit.Name)} = 1u << {bit.Value.ToString(CultureInfo.InvariantCulture)}{comma}");
        }

        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine(
            $"inline constexpr {typeName}Flags operator|({typeName}Flags a, {typeName}Flags b) {{");
        sb.AppendLine(
            $"    return static_cast<{typeName}Flags>(static_cast<std::uint32_t>(a) | static_cast<std::uint32_t>(b));");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"struct {typeName} {{");
        sb.AppendLine("    asn1kit::bit_string value{};");
        sb.AppendLine();
        // Converting ctor + comparisons so SEQUENCE DEFAULT `= bit_string::copy_from(...)`
        // and encode omit-if-default `!= copy_from(...)` type-check (C# wraps named BIT STRING).
        sb.AppendLine($"    {typeName}() = default;");
        sb.AppendLine($"    {typeName}(asn1kit::bit_string bits) : value(std::move(bits)) {{}}");
        sb.AppendLine();
        sb.AppendLine(
            $"    friend bool operator==(const {typeName}& a, const {typeName}& b) noexcept {{");
        sb.AppendLine("        return a.value == b.value;");
        sb.AppendLine("    }");
        sb.AppendLine(
            $"    friend bool operator!=(const {typeName}& a, const {typeName}& b) noexcept {{");
        sb.AppendLine("        return !(a == b);");
        sb.AppendLine("    }");
        sb.AppendLine(
            $"    friend bool operator==(const {typeName}& a, const asn1kit::bit_string& b) noexcept {{");
        sb.AppendLine("        return a.value == b;");
        sb.AppendLine("    }");
        sb.AppendLine(
            $"    friend bool operator!=(const {typeName}& a, const asn1kit::bit_string& b) noexcept {{");
        sb.AppendLine("        return !(a == b);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    static bool bit_is_set(const asn1kit::bit_string& bits, int index) {");
        sb.AppendLine("        if (index < 0 || static_cast<std::size_t>(index) >= bits.bit_length()) return false;");
        sb.AppendLine("        const auto byte = bits.span()[static_cast<std::size_t>(index) / 8];");
        sb.AppendLine("        return (byte & static_cast<std::uint8_t>(0x80 >> (index % 8))) != 0;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    {typeName}Flags flags() const {{");
        sb.AppendLine($"        auto result = {typeName}Flags::none;");
        foreach (var bit in type.NamedBits!)
        {
            var member = CppNames.SanitizeEnumMember(bit.Name);
            sb.AppendLine(
                $"        if (bit_is_set(value, {bit.Value.ToString(CultureInfo.InvariantCulture)})) result = result | {typeName}Flags::{member};");
        }

        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    static {typeName} from_flags({typeName}Flags flags) {{");
        sb.AppendLine($"        {typeName} result{{}};");
        var maxBit = type.NamedBits.Max(b => b.Value);
        var byteCount = (maxBit / 8) + 1;
        sb.AppendLine($"        std::vector<std::uint8_t> data({byteCount.ToString(CultureInfo.InvariantCulture)}, 0);");
        foreach (var bit in type.NamedBits)
        {
            var member = CppNames.SanitizeEnumMember(bit.Name);
            var index = bit.Value.ToString(CultureInfo.InvariantCulture);
            sb.AppendLine(
                $"        if ((static_cast<std::uint32_t>(flags) & static_cast<std::uint32_t>({typeName}Flags::{member})) != 0) {{");
            sb.AppendLine(
                $"            data[{index} / 8] = static_cast<std::uint8_t>(data[{index} / 8] | static_cast<std::uint8_t>(0x80 >> ({index} % 8)));");
            sb.AppendLine("        }");
        }

        sb.AppendLine("        result.value = asn1kit::bit_string::copy_from(data, 0);");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    static constexpr bool match_tag(const asn1kit::tag& t) {");
        sb.AppendLine("        return t.matches_ignore_constructed(asn1kit::tag::bit_string);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine(
            $"    static {typeName} decode(asn1kit::reader& r, const asn1kit::tag& t = asn1kit::tag::bit_string) {{");
        sb.AppendLine($"        {typeName} result{{}};");
        sb.AppendLine("        result.value = r.read_bit_string(t);");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine(
            "    void encode(asn1kit::writer& w, const asn1kit::tag& t = asn1kit::tag::bit_string) const {");
        sb.AppendLine("        w.write_bit_string(t, value);");
        sb.AppendLine("    }");
        sb.AppendLine("};");
    }

    private void EmitStructuredDefaultHelpers(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string owner,
        IReadOnlyList<IrComponent> components)
    {
        var helpers = components.Where(NeedsDefaultHelper).ToList();
        if (helpers.Count == 0)
        {
            return;
        }

        foreach (var field in helpers)
        {
            var cppType = CppType(document, module, owner, field.Name, field.Type, optional: false);
            var defaultName = DefaultHelperName(field);
            sb.AppendLine($"    static {cppType} {defaultName}() {{");
            EmitDefaultConstruction(
                sb, document, module, owner, field.Name, field.Type, field.Default!, "        ");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine(
                $"    static bool {IsDefaultHelperName(field)}(const {cppType}& value) {{");
            var notEqual = DefaultNotEqualExpression(document, module, owner, field, "value");
            sb.AppendLine($"        return {InvertNotEqualToEqual(notEqual)};");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// <see cref="ValueNotEqualExpression"/> wraps structured/collection checks as <c>!(eq)</c>;
    /// strip that outer negate for <c>is_default_*</c> bodies.
    /// </summary>
    private static string InvertNotEqualToEqual(string notEqualExpression)
    {
        if (notEqualExpression.StartsWith("!(", StringComparison.Ordinal) &&
            notEqualExpression.EndsWith(')') &&
            IsWrappedByOuterParens(notEqualExpression, 1))
        {
            return notEqualExpression.Substring(2, notEqualExpression.Length - 3);
        }

        return "!(" + notEqualExpression + ")";
    }

    private static bool IsWrappedByOuterParens(string expression, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i == expression.Length - 1;
                }
            }
        }

        return false;
    }

    private void EmitDefaultConstruction(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string owner,
        string hint,
        TypeExpr declared,
        IrValue value,
        string indent)
    {
        var (typeModule, type) = ResolveAliasTarget(document, module, declared);
        if (value is IrStructuredValue structure && type is SequenceType or SetType)
        {
            var components = type is SequenceType seq ? seq.Components : ((SetType)type).Components;
            var typeName = CppType(document, module, owner, hint, declared, optional: false);
            sb.AppendLine($"{indent}{typeName} v{{}};");
            foreach (var pair in structure.Fields)
            {
                var field = components.Single(f => f.Name == pair.Key);
                var fieldExpr = ValueExpression(
                    document,
                    module,
                    typeName,
                    field.Name,
                    field.Type,
                    pair.Value,
                    typeModule,
                    indent);
                sb.AppendLine($"{indent}v.{FieldName(field)} = {fieldExpr};");
            }

            sb.AppendLine($"{indent}return v;");
            return;
        }

        sb.AppendLine(
            $"{indent}return {ValueExpression(document, module, owner, hint, declared, value, indent: indent)};");
    }

    private string FieldInitializer(
        IrDocument document,
        IrModule module,
        string owner,
        IrComponent field,
        bool optional)
    {
        if (optional)
        {
            return "{}";
        }

        if (field.Default is not null)
        {
            return " = " + DefaultExpression(document, module, owner, field);
        }

        var unwrapped = UnwrapAliases(document, module, field.Type);
        if (unwrapped is SequenceOfType or SetOfType)
        {
            return "{}";
        }

        if (unwrapped is StringType)
        {
            return "{}";
        }

        return "{}";
    }
}

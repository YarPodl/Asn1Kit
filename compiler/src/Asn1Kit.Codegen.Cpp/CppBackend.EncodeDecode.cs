using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.Cpp;

public sealed partial class CppBackend
{
    private void EmitEncodeField(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string owner,
        IrComponent field,
        string indent,
        string writer,
        JsonObject? fieldOptions = null)
    {
        fieldOptions ??= field.Options;
        var expr = "this->" + FieldName(field);
        if (field.Default is not null)
        {
            sb.AppendLine($"{indent}if ({DefaultNotEqualExpression(document, module, owner, field, expr)}) {{");
            EmitEncodeValue(
                sb,
                document,
                module,
                owner,
                field.Name,
                field.Type,
                indent + "    ",
                writer,
                expr,
                fieldOptions: fieldOptions);
            sb.AppendLine($"{indent}}}");
        }
        else if (field.Optional || field.ExtensionAddition == true)
        {
            sb.AppendLine($"{indent}if ({expr}.has_value()) {{");
            EmitEncodeValue(
                sb,
                document,
                module,
                owner,
                field.Name,
                field.Type,
                indent + "    ",
                writer,
                "(*" + expr + ")",
                fieldOptions: fieldOptions);
            sb.AppendLine($"{indent}}}");
        }
        else
        {
            EmitEncodeValue(
                sb,
                document,
                module,
                owner,
                field.Name,
                field.Type,
                indent,
                writer,
                expr,
                fieldOptions: fieldOptions);
        }
    }

    private void EmitEncodeValue(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string owner,
        string hint,
        TypeExpr type,
        string indent,
        string writer,
        string expr,
        string? forceTag = null,
        JsonObject? fieldOptions = null,
        bool encodeDeferredWrapper = true)
    {
        if (encodeDeferredWrapper && ShouldEmitLazy(document, module, type, fieldOptions))
        {
            sb.AppendLine($"{indent}if ({expr}.has_encoded()) {{");
            sb.AppendLine($"{indent}    {writer}.write_raw({expr}.encoded().span());");
            sb.AppendLine($"{indent}}} else {{");
            EmitEncodeValue(
                sb,
                document,
                module,
                owner,
                hint,
                type,
                indent + "    ",
                writer,
                expr + ".get()",
                forceTag,
                fieldOptions: null,
                encodeDeferredWrapper: false);
            sb.AppendLine($"{indent}}}");
            return;
        }

        if (encodeDeferredWrapper && ShouldEmitRetainEncoded(document, module, type, fieldOptions))
        {
            sb.AppendLine($"{indent}if ({expr}.has_original_encoding()) {{");
            sb.AppendLine($"{indent}    {writer}.write_raw({expr}.original_encoding().span());");
            sb.AppendLine($"{indent}}} else {{");
            EmitEncodeValue(
                sb,
                document,
                module,
                owner,
                hint,
                type,
                indent + "    ",
                writer,
                expr + ".get()",
                forceTag,
                fieldOptions: null,
                encodeDeferredWrapper: false);
            sb.AppendLine($"{indent}}}");
            return;
        }

        var original = type;
        var resolvedModule = module;
        type = UnwrapAliases(document, ref resolvedModule, type);

        if (type.Tag?.Mode == TagModes.Explicit && forceTag is null)
        {
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    auto explicit_scope = {writer}.enter_explicit({TagFromIr(type.Tag, constructed: true)});");
            var inner = CloneUntagged(type);
            EmitEncodeValue(
                sb,
                document,
                module,
                owner,
                hint,
                inner,
                indent + "    ",
                writer,
                expr);
            sb.AppendLine($"{indent}}}");
            return;
        }

        if (type is SequenceOfType or SetOfType)
        {
            EmitOfEncode(
                sb,
                document,
                module,
                original,
                owner,
                hint,
                type,
                indent,
                writer,
                expr,
                forceTag);
            return;
        }

        if (ContainedType(type) is { } content)
        {
            ContainedNaming(
                document,
                resolvedModule,
                original,
                owner,
                hint,
                out var contentModule,
                out var contentOwner,
                out var contentHint);
            var containedTag = forceTag ?? TagExpr(document, module, type);
            var resolvedContent = UnwrapAliases(document, contentModule, content);
            if (resolvedContent is AnyType)
            {
                if (type is BitStringType)
                    sb.AppendLine($"{indent}{writer}.write_bit_string({containedTag}, {expr});");
                else
                    sb.AppendLine($"{indent}{writer}.write_octet_string({containedTag}, {expr}.span());");
                return;
            }
            var method = type is BitStringType
                ? "write_bit_string_containing"
                : "write_octet_string_containing";
            sb.AppendLine(
                $"{indent}{writer}.{method}({containedTag}, {expr}, [&](asn1kit::writer& inner, const auto& value) {{");
            EmitEncodeValue(
                sb,
                document,
                contentModule,
                contentOwner,
                contentHint,
                content,
                indent + "    ",
                "inner",
                "value");
            sb.AppendLine($"{indent}}});");
            return;
        }

        if (type is AnyType)
        {
            if (forceTag is not null || type.Tag is not null)
            {
                var anyTag = forceTag ?? TagExpr(document, module, type);
                sb.AppendLine($"{indent}{writer}.write_any({anyTag}, {expr});");
            }
            else
            {
                sb.AppendLine($"{indent}{writer}.write_any({expr});");
            }

            return;
        }

        var encodeTag = forceTag ?? TagExpr(document, module, type);
        if (IsEnumeratedRefOrType(document, module, type))
        {
            var enumName = EnumeratedCodecName(document, module, owner, hint, type, "encode");
            sb.AppendLine($"{indent}{enumName}({writer}, {expr}, {encodeTag});");
            return;
        }

        if (type is RefType namedBitRef && Find(document, module, namedBitRef)?.Type is BitStringType { NamedBits.Count: > 0 })
        {
            EmitEncodeGenerated(sb, indent, writer, expr, encodeTag, forceTag, type);
            return;
        }

        if (IsNamedBitString(type))
        {
            EmitEncodeGenerated(sb, indent, writer, expr, encodeTag, forceTag, type);
            return;
        }

        if (TryGetNamedInteger(document, module, type) is { } namedIntegerEncode)
        {
            sb.AppendLine(
                $"{indent}{WriteCall(document, namedIntegerEncode.Module, writer, encodeTag, expr, namedIntegerEncode.Type)};");
            return;
        }

        var primitive = ResolvePrimitive(type);
        if (primitive is not null)
        {
            sb.AppendLine($"{indent}{WriteCall(document, module, writer, encodeTag, expr, type)};");
            return;
        }

        EmitEncodeGenerated(sb, indent, writer, expr, encodeTag, forceTag, type);
    }

    private static void EmitEncodeGenerated(
        StringBuilder sb,
        string indent,
        string writer,
        string expr,
        string encodeTag,
        string? forceTag,
        TypeExpr type)
    {
        if (forceTag is not null || type.Tag is not null)
        {
            sb.AppendLine($"{indent}{expr}.encode({writer}, {encodeTag});");
        }
        else
        {
            sb.AppendLine($"{indent}{expr}.encode({writer});");
        }
    }

    private void EmitOfEncode(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        TypeExpr original,
        string owner,
        string hint,
        TypeExpr ofType,
        string indent,
        string writer,
        string expr,
        string? forceTag)
    {
        ResolveOfItemNaming(document, module, original, owner, hint, out var itemOwner, out var itemHint);
        var writeMethod = ofType is SetOfType ? "write_set_of" : "write_sequence_of";
        var tag = forceTag ?? TagExpr(document, module, ofType);
        var element = ofType is SetOfType setOf ? setOf.Element : ((SequenceOfType)ofType).Element;
        if (CanUseTypeDrivenOfItem(document, module, element))
        {
            sb.AppendLine($"{indent}{writer}.{writeMethod}({tag}, {expr});");
            return;
        }

        sb.AppendLine($"{indent}{writer}.{writeMethod}({tag}, {expr}, [&](asn1kit::writer& inner, const auto& item) {{");
        EmitEncodeValue(
            sb,
            document,
            module,
            itemOwner,
            itemHint,
            element,
            indent + "    ",
            "inner",
            "item");
        sb.AppendLine($"{indent}}});");
    }

    private void EmitDecodeField(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string owner,
        IrComponent field,
        IReadOnlyList<IrComponent> ownerComponents,
        string indent,
        string reader,
        string target)
    {
        _ = ownerComponents;
        var prop = FieldName(field);
        if (field.Optional || field.ExtensionAddition == true || field.Default is not null)
        {
            if (IsUntaggedAny(document, module, field.Type))
            {
                sb.AppendLine($"{indent}if (!{reader}.eof()) {{");
                EmitDecodeAssign(
                    sb,
                    document,
                    module,
                    owner,
                    field.Name,
                    field.Type,
                    indent + "    ",
                    reader,
                    $"{target}.{prop}",
                    fieldOptions: field.Options);
                sb.AppendLine($"{indent}}}");
                if (field.Default is not null)
                {
                    sb.AppendLine($"{indent}else {{");
                    sb.AppendLine(
                        $"{indent}    {target}.{prop} = {DefaultExpression(document, module, owner, field)};");
                    sb.AppendLine($"{indent}}}");
                }
            }
            else if (TryGetNextMatchesTypeName(
                         document, module, owner, field.Name, field.Type, out var matchTypeName))
            {
                sb.AppendLine($"{indent}if ({reader}.next_matches<{matchTypeName}>()) {{");
                EmitDecodeAssign(
                    sb,
                    document,
                    module,
                    owner,
                    field.Name,
                    field.Type,
                    indent + "    ",
                    reader,
                    $"{target}.{prop}",
                    fieldOptions: field.Options);
                sb.AppendLine($"{indent}}}");
                if (field.Default is not null)
                {
                    sb.AppendLine($"{indent}else {{");
                    sb.AppendLine(
                        $"{indent}    {target}.{prop} = {DefaultExpression(document, module, owner, field)};");
                    sb.AppendLine($"{indent}}}");
                }
            }
            else
            {
                sb.AppendLine($"{indent}if ({reader}.next_is({TagExpr(document, module, field.Type)})) {{");
                EmitDecodeAssign(
                    sb,
                    document,
                    module,
                    owner,
                    field.Name,
                    field.Type,
                    indent + "    ",
                    reader,
                    $"{target}.{prop}",
                    fieldOptions: field.Options);
                sb.AppendLine($"{indent}}}");
                if (field.Default is not null)
                {
                    sb.AppendLine($"{indent}else {{");
                    sb.AppendLine(
                        $"{indent}    {target}.{prop} = {DefaultExpression(document, module, owner, field)};");
                    sb.AppendLine($"{indent}}}");
                }
            }
        }
        else
        {
            EmitDecodeAssign(
                sb,
                document,
                module,
                owner,
                field.Name,
                field.Type,
                indent,
                reader,
                $"{target}.{prop}",
                fieldOptions: field.Options);
        }
    }

    private string PeekMatchExpr(IrDocument document, IrModule module, TypeExpr type, string peekedVar)
    {
        if (TryGetUntaggedMultiAltChoice(document, module, type, out var choice))
        {
            var alts = choice.Components
                .Select(c => $"{peekedVar}.matches_ignore_constructed({TagExpr(document, module, c.Type)})");
            return "(" + string.Join(" || ", alts) + ")";
        }

        return $"{peekedVar}.matches_ignore_constructed({TagExpr(document, module, type)})";
    }

    /// <summary>
    /// True when OF item codec equals natural <c>read&lt;T&gt;()</c> / <c>write(item)</c>
    /// (no per-element tag override, nested OF, ENUMERATED free functions, or string/time forms).
    /// </summary>
    private bool CanUseTypeDrivenOfItem(IrDocument document, IrModule module, TypeExpr element)
    {
        if (element.Tag is not null)
        {
            return false;
        }

        var cursor = UnwrapAliases(document, module, element);
        if (cursor.Tag is not null)
        {
            return false;
        }

        if (cursor is SequenceOfType or SetOfType)
        {
            return false;
        }

        if (cursor is SequenceType or SetType or ChoiceType)
        {
            return true;
        }

        if (IsNamedBitString(cursor))
        {
            return true;
        }

        if (cursor is RefType reference)
        {
            var found = Find(document, module, reference)?.Type;
            if (found is SequenceType or SetType or ChoiceType)
            {
                return found.Tag is null;
            }

            if (found is BitStringType { NamedBits.Count: > 0 })
            {
                return found.Tag is null;
            }

            if (found is EnumeratedType)
            {
                return false;
            }

            if (found is not null)
            {
                cursor = found;
            }
        }

        if (IsEnumeratedRefOrType(document, module, element))
        {
            return false;
        }

        return cursor switch
        {
            BooleanType => true,
            IntegerType => true,
            OidType => true,
            OctetStringType { Containing: null } => true,
            BitStringType { Containing: null, NamedBits: null } => true,
            BitStringType { Containing: null, NamedBits.Count: 0 } => true,
            _ => false
        };
    }

    /// <summary>
    /// Named generated type usable with <c>reader::next_matches&lt;T&gt;()</c> for OPTIONAL/DEFAULT.
    /// Field-level tag overrides still use <c>next_is</c>. Tagged CHOICE typedefs are allowed:
    /// <c>match_tag</c> already matches the CHOICE wrapper (e.g. APPLICATION).
    /// </summary>
    private bool TryGetNextMatchesTypeName(
        IrDocument document,
        IrModule module,
        string owner,
        string hint,
        TypeExpr type,
        out string typeName)
    {
        typeName = "";
        // Per-use-site tag (context/EXPLICIT on the field) → next_is(that tag).
        if (type.Tag is not null)
        {
            return false;
        }

        var cursor = UnwrapAliases(document, module, type);

        if (cursor is RefType reference)
        {
            var found = FindWithModule(document, module, reference);
            var defType = found?.Def.Type;
            if (defType is ChoiceType)
            {
                // Emit relative to the owning module so cross-module refs stay qualified.
                typeName = CppType(document, module, owner, hint, type, optional: false);
                return true;
            }

            if (defType is SequenceType or SetType or BitStringType { NamedBits.Count: > 0 })
            {
                // SEQUENCE/SET match_tag is the universal tag only; typedef-level tags unsupported.
                if (defType.Tag is not null)
                {
                    return false;
                }

                typeName = CppType(document, module, owner, hint, type, optional: false);
                return true;
            }

            return false;
        }

        if (cursor is ChoiceType)
        {
            typeName = CppType(document, module, owner, hint, type, optional: false);
            return true;
        }

        if (cursor.Tag is not null)
        {
            return false;
        }

        if (cursor is SequenceType or SetType || IsNamedBitString(cursor))
        {
            typeName = CppType(document, module, owner, hint, type, optional: false);
            return true;
        }

        return false;
    }

    private bool TryGetUntaggedMultiAltChoice(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        out ChoiceType choice)
    {
        choice = null!;
        if (type.Tag is not null)
        {
            return false;
        }

        var cursor = UnwrapAliases(document, module, type);
        if (cursor.Tag is not null)
        {
            return false;
        }

        if (cursor is RefType reference)
        {
            var found = Find(document, module, reference);
            if (found?.Type is ChoiceType named &&
                !IsSingleAlternativeChoice(named) &&
                named.Tag is null)
            {
                choice = named;
                return true;
            }

            return false;
        }

        if (cursor is ChoiceType inline &&
            !IsSingleAlternativeChoice(inline) &&
            inline.Tag is null)
        {
            choice = inline;
            return true;
        }

        return false;
    }

    private bool IsUntaggedAny(IrDocument document, IrModule module, TypeExpr type)
    {
        if (type.Tag is not null)
        {
            return false;
        }

        var unwrapped = UnwrapAliases(document, module, type);
        return unwrapped is AnyType { Tag: null };
    }

    private void EmitDecodeAssign(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        string owner,
        string hint,
        TypeExpr type,
        string indent,
        string reader,
        string target,
        string? forceTag = null,
        JsonObject? fieldOptions = null,
        bool allowDeferredWrapper = true)
    {
        if (allowDeferredWrapper &&
            (ShouldEmitLazy(document, module, type, fieldOptions) ||
             ShouldEmitRetainEncoded(document, module, type, fieldOptions)))
        {
            var method = ShouldEmitLazy(document, module, type, fieldOptions)
                ? "read_lazy"
                : "read_with_original_encoding";
            var coreType = CppType(
                document, module, owner, hint, type, optional: false, fieldOptions, allowDeferredWrapper: false);
            sb.AppendLine(
                $"{indent}{target} = {reader}.{method}<{coreType}>([&](asn1kit::reader& deferred) {{");
            sb.AppendLine($"{indent}    {coreType} item{{}};");
            EmitDecodeAssign(
                sb,
                document,
                module,
                owner,
                hint,
                type,
                indent + "    ",
                "deferred",
                "item",
                forceTag,
                fieldOptions: null,
                allowDeferredWrapper: false);
            sb.AppendLine($"{indent}    return item;");
            sb.AppendLine($"{indent}}});");
            return;
        }

        var original = type;
        var resolvedModule = module;
        type = UnwrapAliases(document, ref resolvedModule, type);

        if (type.Tag?.Mode == TagModes.Explicit && forceTag is null)
        {
            var tmp = "tmp_" + CppNames.SanitizeFieldName(hint);
            // Preserve a named/aliased reference while removing only the use-site
            // EXPLICIT tag. Unwrapping first would invent Owner_Field_Item names
            // for aliases such as PollReqContent ::= SEQUENCE OF PollReqContent-Item.
            var untaggedOriginal = CloneUntagged(original);
            var tmpType = CppType(document, module, owner, hint, untaggedOriginal, optional: false);
            sb.AppendLine($"{indent}{tmpType} {tmp}{{}};");
            sb.AppendLine(
                $"{indent}{reader}.with_explicit({TagFromIr(type.Tag, constructed: true)}, [&](asn1kit::reader& scoped) {{");
            EmitDecodeAssign(
                sb,
                document,
                module,
                owner,
                hint,
                untaggedOriginal,
                indent + "    ",
                "scoped",
                tmp);
            sb.AppendLine($"{indent}}});");
            sb.AppendLine($"{indent}{target} = std::move({tmp});");
            return;
        }

        if (type is SequenceOfType or SetOfType)
        {
            EmitOfDecode(
                sb,
                document,
                module,
                original,
                owner,
                hint,
                type,
                indent,
                reader,
                target,
                forceTag);
            return;
        }

        if (ContainedType(type) is { } content)
        {
            ContainedNaming(
                document,
                resolvedModule,
                original,
                owner,
                hint,
                out var contentModule,
                out var contentOwner,
                out var contentHint);
            var containedTag = forceTag ?? TagExpr(document, module, type);
            var resolvedContent = UnwrapAliases(document, contentModule, content);
            if (resolvedContent is AnyType)
            {
                var readMethod = type is BitStringType ? "read_bit_string" : "read_octet_string";
                sb.AppendLine($"{indent}{target} = {reader}.{readMethod}({containedTag});");
                return;
            }
            var contentCppType = CppType(
                document,
                contentModule,
                contentOwner,
                contentHint,
                content,
                optional: false);
            var method = type is BitStringType
                ? "read_bit_string_containing"
                : "read_octet_string_containing";
            sb.AppendLine(
                $"{indent}{target} = {reader}.{method}<{contentCppType}>({containedTag}, [&](asn1kit::reader& inner) {{");
            sb.AppendLine($"{indent}    {contentCppType} item{{}};");
            EmitDecodeAssign(
                sb,
                document,
                contentModule,
                contentOwner,
                contentHint,
                content,
                indent + "    ",
                "inner",
                "item");
            sb.AppendLine($"{indent}    return item;");
            sb.AppendLine($"{indent}}});");
            return;
        }

        if (type is AnyType)
        {
            if (forceTag is not null || type.Tag is not null)
            {
                var anyTag = forceTag ?? TagExpr(document, module, type);
                sb.AppendLine($"{indent}{target} = {reader}.read_any({anyTag});");
            }
            else
            {
                sb.AppendLine($"{indent}{target} = {reader}.read_any();");
            }

            return;
        }

        var decodeTag = forceTag ?? TagExpr(document, module, type);
        if (IsEnumeratedRefOrType(document, module, type))
        {
            var enumName = EnumeratedCodecName(document, module, owner, hint, type, "decode");
            sb.AppendLine($"{indent}{target} = {enumName}({reader}, {decodeTag});");
            return;
        }

        if (type is RefType namedBitRef && Find(document, module, namedBitRef)?.Type is BitStringType { NamedBits.Count: > 0 })
        {
            var name = NamedTypeName(document, resolvedModule, owner, hint, type, module);
            EmitDecodeGenerated(sb, indent, reader, target, name, decodeTag, forceTag, type);
            return;
        }

        if (IsNamedBitString(type))
        {
            var name = owner + "_" + CppNames.SanitizeTypeName(hint);
            EmitDecodeGenerated(sb, indent, reader, target, name, decodeTag, forceTag, type);
            return;
        }

        if (TryGetNamedInteger(document, module, type) is { } namedIntegerDecode)
        {
            var integerRepresentation = TryResolveIntegerRepresentation(
                document, namedIntegerDecode.Module, namedIntegerDecode.Type);
            sb.AppendLine(
                $"{indent}{target} = {ReadCall(reader, decodeTag, namedIntegerDecode.Type, integerRepresentation)};");
            return;
        }

        var primitive = ResolvePrimitive(type);
        if (primitive is not null)
        {
            var integerRepresentation = TryResolveIntegerRepresentation(document, module, original);
            sb.AppendLine(
                $"{indent}{target} = {ReadCall(reader, decodeTag, type, integerRepresentation)};");
            return;
        }

        if (type is ChoiceType || (type is RefType choiceRef && Find(document, module, choiceRef)?.Type is ChoiceType))
        {
            var name = type is RefType
                ? NamedTypeName(document, resolvedModule, owner, hint, type, module)
                : owner + "_" + CppNames.SanitizeTypeName(hint);
            EmitDecodeGenerated(sb, indent, reader, target, name, decodeTag, forceTag, type);
            return;
        }

        var typeName = NamedTypeName(document, resolvedModule, owner, hint, type, module);
        EmitDecodeGenerated(sb, indent, reader, target, typeName, decodeTag, forceTag, type);
    }

    private static void EmitDecodeGenerated(
        StringBuilder sb,
        string indent,
        string reader,
        string target,
        string typeName,
        string decodeTag,
        string? forceTag,
        TypeExpr type)
    {
        if (forceTag is not null || type.Tag is not null)
        {
            sb.AppendLine($"{indent}{target} = {typeName}::decode({reader}, {decodeTag});");
        }
        else
        {
            sb.AppendLine($"{indent}{target} = {typeName}::decode({reader});");
        }
    }

    private void EmitOfDecode(
        StringBuilder sb,
        IrDocument document,
        IrModule module,
        TypeExpr original,
        string owner,
        string hint,
        TypeExpr ofType,
        string indent,
        string reader,
        string target,
        string? forceTag)
    {
        ResolveOfItemNaming(document, module, original, owner, hint, out var itemOwner, out var itemHint);
        var readMethod = ofType is SetOfType ? "read_set_of" : "read_sequence_of";
        var tag = forceTag ?? TagExpr(document, module, ofType);
        var element = ofType is SetOfType setOf ? setOf.Element : ((SequenceOfType)ofType).Element;
        var itemType = CppType(document, module, itemOwner, itemHint, element, optional: false);
        if (CanUseTypeDrivenOfItem(document, module, element))
        {
            var defaultTag = ofType is SetOfType ? "asn1kit::tag::set" : "asn1kit::tag::sequence";
            var tagArgs = tag == defaultTag ? string.Empty : tag;
            sb.AppendLine($"{indent}{target} = {reader}.{readMethod}<{itemType}>({tagArgs});");
            return;
        }

        sb.AppendLine(
            $"{indent}{target} = {reader}.{readMethod}<{itemType}>({tag}, [&](asn1kit::reader& inner) {{");
        sb.AppendLine($"{indent}    {itemType} item{{}};");
        EmitDecodeAssign(
            sb,
            document,
            module,
            itemOwner,
            itemHint,
            element,
            indent + "    ",
            "inner",
            "item");
        sb.AppendLine($"{indent}    return item;");
        sb.AppendLine($"{indent}}});");
    }

    private string WriteCall(
        IrDocument document,
        IrModule module,
        string writer,
        string tag,
        string expr,
        TypeExpr type)
    {
        var integerRepresentation = TryResolveIntegerRepresentation(document, module, type);
        return type switch
        {
            BooleanType => $"{writer}.write_boolean({tag}, {expr})",
            IntegerType => IntegerWriteCall(writer, tag, expr, integerRepresentation),
            BitStringType => $"{writer}.write_bit_string({tag}, {expr})",
            OctetStringType => $"{writer}.write_octet_string({tag}, {expr}.span())",
            NullType => $"{writer}.write_null({tag})",
            OidType => $"{writer}.write_object_identifier({tag}, {expr})",
            StringType stringType =>
                $"{writer}.write_string({tag}, {expr}, {StringFormEnum(stringType.Form)})",
            TimeType timeType => timeType.Form == TimeTypes.Generalized
                ? $"{writer}.write_time({tag}, {expr}, {TimeFormEnum(timeType.Form)}, {timeType.FractionDigits ?? 3})"
                : $"{writer}.write_time({tag}, {expr}, {TimeFormEnum(timeType.Form)})",
            _ => throw new InvalidOperationException(type.Kind)
        };
    }

    private static string IntegerWriteCall(
        string writer,
        string tag,
        string expr,
        string? representation) =>
        (representation ?? IrOptions.IntegerRepresentations.Der) switch
        {
            IrOptions.IntegerRepresentations.Int32 => $"{writer}.write_integer({tag}, {expr})",
            IrOptions.IntegerRepresentations.UInt32 => $"{writer}.write_integer({tag}, {expr})",
            IrOptions.IntegerRepresentations.Int64 => $"{writer}.write_integer({tag}, {expr})",
            IrOptions.IntegerRepresentations.UInt64 => $"{writer}.write_integer({tag}, {expr})",
            IrOptions.IntegerRepresentations.BigInt => $"{writer}.write_integer({tag}, {expr})",
            IrOptions.IntegerRepresentations.Der => $"{writer}.write_integer({tag}, {expr})",
            _ => throw new NotSupportedException($"Unknown integer representation '{representation}'.")
        };

    private static string ReadCall(
        string reader,
        string tag,
        TypeExpr type,
        string? integerRepresentation) => type switch
    {
        BooleanType => $"{reader}.read_boolean({tag})",
        IntegerType => IntegerReadCall(reader, tag, integerRepresentation),
        BitStringType => $"{reader}.read_bit_string({tag})",
        OctetStringType => $"{reader}.read_octet_string({tag})",
        NullType => $"asn1kit::null_value::decode({reader}, {tag})",
        OidType => $"{reader}.read_oid({tag})",
        StringType stringType =>
            $"{reader}.read_string({tag}, {StringFormEnum(stringType.Form)})",
        TimeType timeType => $"{reader}.read_time({tag}, {TimeFormEnum(timeType.Form)})",
        _ => throw new InvalidOperationException(type.Kind)
    };

    private static string IntegerReadCall(string reader, string tag, string? representation) =>
        (representation ?? IrOptions.IntegerRepresentations.Der) switch
        {
            IrOptions.IntegerRepresentations.Int32 => $"{reader}.read_int32({tag})",
            IrOptions.IntegerRepresentations.UInt32 => $"{reader}.read_uint32({tag})",
            IrOptions.IntegerRepresentations.Int64 => $"{reader}.read_int64({tag})",
            IrOptions.IntegerRepresentations.UInt64 => $"{reader}.read_uint64({tag})",
            IrOptions.IntegerRepresentations.BigInt => $"{reader}.read_integer_value({tag})",
            IrOptions.IntegerRepresentations.Der => $"{reader}.read_integer_value({tag})",
            _ => throw new NotSupportedException($"Unknown integer representation '{representation}'.")
        };

    private string DefaultNotEqualExpression(
        IrDocument document,
        IrModule module,
        string owner,
        IrComponent field,
        string valueExpression)
    {
        if (field.Default is null)
        {
            throw new InvalidOperationException("DEFAULT expression requested without default.");
        }

        return ValueNotEqualExpression(
            document, module, owner, field.Name, field.Type, field.Default, valueExpression);
    }

    private string DefaultExpression(
        IrDocument document,
        IrModule module,
        string owner,
        IrComponent field)
    {
        if (field.Default is null)
        {
            throw new InvalidOperationException("DEFAULT expression requested without default.");
        }

        return ValueExpression(document, module, owner, field.Name, field.Type, field.Default);
    }

    private string ValueExpression(
        IrDocument document,
        IrModule module,
        string owner,
        string hint,
        TypeExpr declared,
        IrValue value)
    {
        var (typeModule, type) = ResolveAliasTarget(document, module, declared);
        if (value is IrIntegerValue enumDefault &&
            ResolveEnumeratedDefault(
                document, module, owner, new IrComponent { Name = hint, Type = declared }, type, enumDefault)
                is { } enumExpr)
        {
            return enumExpr;
        }

        if (value is IrStructuredValue structure && type is SequenceType or SetType)
        {
            var components = type is SequenceType seq ? seq.Components : ((SetType)type).Components;
            // Collapsible open-type aliases (e.g. PBKDF2-PRFsAlgorithmIdentifier) are not emitted;
            // construct the underlying C++ type that fields actually use.
            var typeName = CppType(document, module, owner, hint, declared, optional: false);
            var assigns = structure.Fields.Select(pair =>
            {
                var field = components.Single(f => f.Name == pair.Key);
                var fieldExpr = ValueExpression(
                    document, typeModule, typeName, field.Name, field.Type, pair.Value);
                return $"        v.{FieldName(field)} = {fieldExpr};";
            });
            return "([&]{ " + typeName + " v{}; " + string.Join(" ", assigns) + " return v; }())";
        }

        if (value is IrCollectionValue collection && type is SequenceOfType or SetOfType)
        {
            var element = type is SequenceOfType of ? of.Element : ((SetOfType)type).Element;
            ResolveOfItemNaming(document, module, declared, owner, hint, out var itemOwner, out var itemHint);
            var items = collection.Items.Select(item =>
                ValueExpression(document, typeModule, itemOwner, itemHint, element, item));
            return "std::vector<" +
                   CppType(document, module, itemOwner, itemHint, element, optional: false) +
                   ">{" + string.Join(", ", items) + "}";
        }

        if (value is IrChoiceValue choice && type is ChoiceType alternatives)
        {
            var field = alternatives.Components.Single(f => f.Name == choice.Alternative);
            var inner = ValueExpression(
                document, typeModule, owner, field.Name, field.Type, choice.Value);
            if (IsSingleAlternativeChoice(type))
            {
                return inner;
            }

            var typeName = CppType(document, module, owner, hint, declared, optional: false);
            var factory = "from_" + CppNames.SanitizeEnumMember(field.Name);
            return typeName + "::" + factory + "(" + inner + ")";
        }

        if (value is IrTypedValue typed && type is AnyType)
        {
            var nested = ValueExpression(
                document, typeModule, owner, hint, typed.Type, typed.Value);
            // encode_open handles null_value / bytes / generated types (instance encode is not universal).
            return "asn1kit::encode_open(" + nested + ")";
        }

        return (type, value) switch
        {
            (BooleanType, IrBooleanValue b) => b.Value ? "true" : "false",
            (NullType, IrNullValue) => "asn1kit::null_value::value()",
            (OidType, IrOidValue oid) =>
                $"asn1kit::oid_literal<\"{CppNames.EscapeString(oid.Value)}\">().get()",
            (StringType, IrStringValue text) =>
                $"std::string(\"{CppNames.EscapeString(text.Value)}\")",
            (IntegerType, IrIntegerValue integer) => IntegerDefaultExpression(
                document, typeModule, type, integer.Value),
            (BitStringType, IrBitStringValue bits) => BitStringDefaultExpression(bits),
            (OctetStringType, IrOctetStringValue octets) => OctetStringDefaultExpression(octets),
            _ => throw new NotSupportedException(
                $"C++ backend does not support DEFAULT kind '{value.Kind}' for '{hint}'.")
        };
    }

    private string ValueNotEqualExpression(
        IrDocument document,
        IrModule module,
        string owner,
        string hint,
        TypeExpr declared,
        IrValue expected,
        string actual)
    {
        var (typeModule, type) = ResolveAliasTarget(document, module, declared);
        if (expected is IrStructuredValue structure && type is SequenceType or SetType)
        {
            var components = type is SequenceType seq ? seq.Components : ((SetType)type).Components;
            var typeName = CppType(document, module, owner, hint, declared, optional: false);
            var tests = new List<string>();
            foreach (var field in components)
            {
                var access = actual + "." + FieldName(field);
                if (!structure.Fields.TryGetValue(field.Name, out var present))
                {
                    if (field.Optional)
                        tests.Add("!" + access + ".has_value()");
                    continue;
                }

                var fieldActual = field.Optional ? "(*" + access + ")" : access;
                if (field.Optional)
                    tests.Add(access + ".has_value()");
                tests.Add(
                    "!(" + ValueNotEqualExpression(
                        document, typeModule, typeName, field.Name, field.Type, present, fieldActual) +
                    ")");
            }

            // Invert: not-equal when any field differs → !(all equal)
            return "!(" + string.Join(" && ", tests.DefaultIfEmpty("true")) + ")";
        }

        if (expected is IrCollectionValue collection && type is SequenceOfType or SetOfType)
        {
            var element = type is SequenceOfType of ? of.Element : ((SetOfType)type).Element;
            ResolveOfItemNaming(document, module, declared, owner, hint, out var itemOwner, out var itemHint);
            var parts = new List<string>
            {
                actual + ".size() == " +
                collection.Items.Count.ToString(CultureInfo.InvariantCulture)
            };
            for (var index = 0; index < collection.Items.Count; index++)
            {
                parts.Add(
                    "!(" + ValueNotEqualExpression(
                        document, typeModule, itemOwner, itemHint, element, collection.Items[index],
                        actual + "[" + index.ToString(CultureInfo.InvariantCulture) + "]") + ")");
            }

            return "!(" + string.Join(" && ", parts) + ")";
        }

        var defaultValue = ValueExpression(document, module, owner, hint, declared, expected);
        return actual + " != " + defaultValue;
    }

    private string? ResolveEnumeratedDefault(
        IrDocument document,
        IrModule module,
        string owner,
        IrComponent field,
        TypeExpr unwrapped,
        IrIntegerValue enumDefault)
    {
        EnumeratedType? enumerated = null;
        string? enumName = null;
        if (unwrapped is EnumeratedType inline)
        {
            enumerated = inline;
            enumName = owner + "_" + CppNames.SanitizeTypeName(field.Name);
        }
        else if (unwrapped is RefType reference)
        {
            var found = Find(document, module, reference);
            if (found?.Type is EnumeratedType named)
            {
                enumerated = named;
                enumName = IrOptions.CppTypeName(found.Options) ?? CppNames.SanitizeTypeName(found.Name);
            }
        }

        if (enumerated is null || enumName is null)
        {
            return null;
        }

        var member = enumerated.Values.FirstOrDefault(v => v.Value == enumDefault.Value)?.Name;
        if (member is null)
        {
            throw new NotSupportedException(
                $"ENUMERATED DEFAULT value {enumDefault.Value} is not a named member for field '{field.Name}'.");
        }

        return $"{enumName}::{CppNames.SanitizeEnumMember(member)}";
    }

    private string IntegerDefaultExpression(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        long value)
    {
        var representation = TryResolveIntegerRepresentation(document, module, type)
            ?? IrOptions.IntegerRepresentations.Der;
        return representation switch
        {
            IrOptions.IntegerRepresentations.Int32 => IntegerLiteral(value),
            IrOptions.IntegerRepresentations.UInt32 => IntegerLiteral(value) + "u",
            IrOptions.IntegerRepresentations.Int64 => IntegerLiteral(value) + "ll",
            IrOptions.IntegerRepresentations.UInt64 => IntegerLiteral(value) + "ull",
            IrOptions.IntegerRepresentations.BigInt =>
                $"asn1kit::integer::from_decimal(\"{IntegerLiteral(value)}\")",
            IrOptions.IntegerRepresentations.Der =>
                $"asn1kit::integer::from_int64({IntegerLiteral(value)}ll)",
            _ => throw new NotSupportedException($"Unknown integer representation '{representation}'.")
        };
    }

    private static string BitStringDefaultExpression(IrBitStringValue value)
    {
        if (!string.IsNullOrEmpty(value.Hex))
        {
            var bytes = ParseHex(value.Hex);
            return $"asn1kit::bit_string::copy_from(std::vector<std::uint8_t>{{{string.Join(", ", bytes.Select(b => "0x" + b.ToString("X2", CultureInfo.InvariantCulture)))}}}, 0)";
        }

        if (string.IsNullOrEmpty(value.Bits))
        {
            throw new NotSupportedException("BIT STRING DEFAULT requires bits or hex.");
        }

        var bits = value.Bits;
        var data = new List<byte>();
        for (var i = 0; i < bits.Length; i += 8)
        {
            byte b = 0;
            for (var j = 0; j < 8 && i + j < bits.Length; j++)
            {
                if (bits[i + j] == '1')
                {
                    b = (byte)(b | (0x80 >> j));
                }
                else if (bits[i + j] != '0')
                {
                    throw new NotSupportedException(
                        "BIT STRING DEFAULT contains a character other than '0' or '1'.");
                }
            }

            data.Add(b);
        }

        var unused = bits.Length % 8 == 0 ? 0 : 8 - (bits.Length % 8);
        return $"asn1kit::bit_string::copy_from(std::vector<std::uint8_t>{{{string.Join(", ", data.Select(b => "0x" + b.ToString("X2", CultureInfo.InvariantCulture)))}}}, {unused})";
    }

    private static string OctetStringDefaultExpression(IrOctetStringValue value)
    {
        var bytes = string.IsNullOrEmpty(value.Hex)
            ? Array.Empty<byte>()
            : ParseHex(value.Hex);
        return $"asn1kit::bytes::copy_from(std::vector<std::uint8_t>{{{string.Join(", ", bytes.Select(b => "0x" + b.ToString("X2", CultureInfo.InvariantCulture)))}}})";
    }

    private static byte[] ParseHex(string hex)
    {
        var cleaned = hex.Replace(" ", "", StringComparison.Ordinal);
        if (cleaned.Length % 2 != 0)
        {
            throw new NotSupportedException("DEFAULT hex length must be even.");
        }

        var result = new byte[cleaned.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = byte.Parse(cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return result;
    }
}

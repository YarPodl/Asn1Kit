using System.Globalization;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.Cpp;

public sealed partial class CppBackend
{
    private static bool NeedsNamedType(TypeExpr type) =>
        type is SequenceType or SetType
        || (type is ChoiceType && !IsSingleAlternativeChoice(type));

    private static bool IsSingleAlternativeChoice(TypeExpr type) =>
        type is ChoiceType { Components: { Count: 1 } } choice
        && !choice.Extensible
        && (choice.Tag is null || choice.Components[0].Type.Tag is null);

    private static bool IsNamedBitString(TypeExpr type) =>
        type is BitStringType { NamedBits: { Count: > 0 } };

    private static bool IsEnumerated(TypeExpr type) => type is EnumeratedType;

    private static bool IsNamedInteger(TypeExpr type) =>
        type is IntegerType { NamedValues: { Count: > 0 } };

    private static bool IsCollapsibleAlias(TypeExpr type) =>
        !NeedsNamedType(type) && !IsNamedBitString(type) && !IsEnumerated(type) && !IsNamedInteger(type);

    private static TypeExpr? ContainedType(TypeExpr type) => type switch
    {
        OctetStringType octets => octets.Containing,
        BitStringType bits => bits.Containing,
        _ => null
    };

    private void ContainedNaming(
        IrDocument document,
        IrModule module,
        TypeExpr original,
        string owner,
        string hint,
        out IrModule contentModule,
        out string contentOwner,
        out string contentHint)
    {
        contentModule = module;
        contentOwner = owner + "_" + CppNames.SanitizeTypeName(hint);
        contentHint = "Content";
        while (original is RefType reference)
        {
            var found = FindWithModule(document, contentModule, reference)
                ?? throw new NotSupportedException($"Unresolved contained type '{reference.Name}'.");
            contentModule = found.Module;
            original = found.Def.Type;
            contentOwner = IrOptions.CppTypeName(found.Def.Options)
                ?? CppNames.SanitizeTypeName(found.Def.Name);
            contentHint = "Content";
        }
    }

    private void EnsureBackendSupport(IrDocument document, IrModule module, TypeExpr type)
    {
        RejectUnsupportedOptions(type);
        switch (type)
        {
            case AnyType:
                break;
            case OctetStringType { Containing: { } octetContent }:
                EnsureBackendSupport(document, module, octetContent);
                break;
            case BitStringType { Containing: { } bitContent }:
                EnsureBackendSupport(document, module, bitContent);
                break;
            case SequenceType { Extensible: true }:
            case SetType { Extensible: true }:
            case ChoiceType { Extensible: true }:
                throw new NotSupportedException("extensible");
            case SequenceType sequence:
                foreach (var component in sequence.Components)
                {
                    EnsureBackendSupport(document, module, component.Type);
                    RejectUnsupportedOptions(component.Options, component.Type);
                }

                break;
            case SetType set:
                foreach (var component in set.Components)
                {
                    EnsureBackendSupport(document, module, component.Type);
                    RejectUnsupportedOptions(component.Options, component.Type);
                }

                break;
            case ChoiceType choice:
                foreach (var component in choice.Components)
                {
                    EnsureBackendSupport(document, module, component.Type);
                }

                break;
            case SequenceOfType sequenceOf:
                EnsureBackendSupport(document, module, sequenceOf.Element);
                break;
            case SetOfType setOf:
                EnsureBackendSupport(document, module, setOf.Element);
                break;
            case RefType reference:
                var found = Find(document, module, reference);
                if (found?.Type is { } inner)
                {
                    EnsureBackendSupport(document, module, inner);
                }

                break;
            case BooleanType:
            case IntegerType:
            case EnumeratedType:
            case BitStringType:
            case OctetStringType:
            case NullType:
            case OidType:
            case StringType:
            case TimeType:
                break;
            default:
                throw new NotSupportedException(type.Kind);
        }
    }

    private static void RejectUnsupportedOptions(TypeExpr type) =>
        RejectUnsupportedOptions(type.Options, type);

    private static void RejectUnsupportedOptions(System.Text.Json.Nodes.JsonObject? options, TypeExpr type)
    {
        if (IrOptions.IsLazy(options) || IrOptions.IsLazy(type.Options))
        {
            throw new NotSupportedException("lazy");
        }

        if (IrOptions.IsRetainEncoded(options) || IrOptions.IsRetainEncoded(type.Options))
        {
            throw new NotSupportedException("retainEncoded");
        }
    }

    private string CppType(
        IrDocument document,
        IrModule module,
        string owner,
        string hint,
        TypeExpr type,
        bool optional)
    {
        var integerRepresentation = TryResolveIntegerRepresentation(document, module, type);
        var original = type;
        var resolvedModule = module;
        type = UnwrapAliases(document, ref resolvedModule, type);

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
            var contained =
                $"asn1kit::contained<{CppType(document, contentModule, contentOwner, contentHint, content, optional: false)}>";
            return optional ? $"std::optional<{contained}>" : contained;
        }

        if (type is SequenceOfType or SetOfType)
        {
            ResolveOfItemNaming(
                document,
                resolvedModule,
                original,
                owner,
                hint,
                out var itemOwner,
                out var itemHint);
            var element = type is SetOfType setOf ? setOf.Element : ((SequenceOfType)type).Element;
            var itemType = CppType(document, resolvedModule, itemOwner, itemHint, element, optional: false);
            var vector = $"std::vector<{itemType}>";
            return optional ? $"std::optional<{vector}>" : vector;
        }

        var primitive = ResolvePrimitive(type);
        if (primitive is not null)
        {
            var mapped = primitive switch
            {
                TypeKinds.Boolean => "bool",
                TypeKinds.Integer => MapIntegerCppType(
                    integerRepresentation ?? IrOptions.IntegerRepresentations.Der),
                TypeKinds.OctetString => "asn1kit::bytes",
                TypeKinds.Null => "asn1kit::null_value",
                TypeKinds.Oid => "asn1kit::oid",
                TypeKinds.BitString => "asn1kit::bit_string",
                TypeKinds.String => "std::string",
                TypeKinds.Time => "asn1kit::utc_date_time",
                TypeKinds.Any => "asn1kit::any",
                _ => throw new NotSupportedException(primitive)
            };
            return optional ? $"std::optional<{mapped}>" : mapped;
        }

        if (IsEnumeratedRefOrType(document, resolvedModule, type))
        {
            var name = NamedTypeName(document, resolvedModule, owner, hint, type);
            return optional ? $"std::optional<{name}>" : name;
        }

        var named = NamedTypeName(document, resolvedModule, owner, hint, type);
        return optional ? $"std::optional<{named}>" : named;
    }

    private static string MapIntegerCppType(string representation) => representation switch
    {
        IrOptions.IntegerRepresentations.Int32 => "std::int32_t",
        IrOptions.IntegerRepresentations.UInt32 => "std::uint32_t",
        IrOptions.IntegerRepresentations.Int64 => "std::int64_t",
        IrOptions.IntegerRepresentations.UInt64 => "std::uint64_t",
        IrOptions.IntegerRepresentations.BigInt => "asn1kit::integer",
        IrOptions.IntegerRepresentations.Der => "asn1kit::integer",
        _ => throw new NotSupportedException($"Unknown integer representation '{representation}'.")
    };

    private string NamedTypeName(
        IrDocument document,
        IrModule module,
        string owner,
        string hint,
        TypeExpr type)
    {
        if (type is RefType reference)
        {
            var found = FindWithModule(document, module, reference);
            if (found is not null)
            {
                var (definingModule, def) = ResolveAliasOfTarget(document, found.Value.Module, found.Value.Def);
                var typeName = IrOptions.CppTypeName(def.Options) ?? CppNames.SanitizeTypeName(def.Name);
                var currentNs = ModuleNamespace(module);
                var definingNs = ModuleNamespace(definingModule);
                if (!string.Equals(currentNs, definingNs, StringComparison.Ordinal))
                {
                    return "::" + definingNs.Replace("::", "::", StringComparison.Ordinal) + "::" + typeName;
                }

                return typeName;
            }

            return CppNames.SanitizeTypeName(reference.Name);
        }

        if (NeedsNamedType(type) || IsEnumerated(type) || IsNamedInteger(type) || IsNamedBitString(type))
        {
            return owner + "_" + CppNames.SanitizeTypeName(hint);
        }

        return CppNames.SanitizeTypeName(hint);
    }

    private void ResolveOfItemNaming(
        IrDocument document,
        IrModule module,
        TypeExpr original,
        string owner,
        string hint,
        out string itemOwner,
        out string itemHint)
    {
        itemHint = "Item";
        var cursor = original;
        var currentModule = module;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (IsSingleAlternativeChoice(cursor))
            {
                cursor = ((ChoiceType)cursor).Components[0].Type;
                continue;
            }

            if (cursor is not RefType reference)
            {
                break;
            }

            var key = (reference.Module ?? currentModule.Name) + "::" + reference.Name;
            if (!visited.Add(key))
            {
                break;
            }

            var found = FindWithModule(document, currentModule, reference);
            if (found is null)
            {
                break;
            }

            var (definingModule, def) = found.Value;
            var inner = def.Type;
            if (inner is SequenceOfType or SetOfType)
            {
                itemOwner = IrOptions.CppTypeName(def.Options) ?? CppNames.SanitizeTypeName(def.Name);
                return;
            }

            if (IsNamedBitString(inner) || NeedsNamedType(inner) || IsEnumerated(inner) || IsNamedInteger(inner))
            {
                break;
            }

            cursor = inner;
            currentModule = definingModule;
        }

        itemOwner = owner + "_" + CppNames.SanitizeTypeName(hint);
    }

    private string? TryResolveIntegerRepresentation(IrDocument document, IrModule module, TypeExpr type)
    {
        string? explicitRepresentation = null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var cursor = type;

        while (true)
        {
            explicitRepresentation ??= IrOptions.IntegerRepresentation(cursor.Options);

            if (IsSingleAlternativeChoice(cursor))
            {
                cursor = ((ChoiceType)cursor).Components[0].Type;
                continue;
            }

            if (cursor is RefType reference)
            {
                var key = (reference.Module ?? module.Name) + "::" + reference.Name;
                if (!visited.Add(key))
                {
                    throw new NotSupportedException($"Circular type alias '{reference.Name}'.");
                }

                var found = FindWithModule(document, module, reference);
                if (found is null)
                {
                    return null;
                }

                var (definingModule, def) = found.Value;
                explicitRepresentation ??= IrOptions.IntegerRepresentation(def.Options);
                var inner = def.Type;
                if (IsNamedBitString(inner) || NeedsNamedType(inner) || IsEnumerated(inner))
                {
                    return null;
                }

                cursor = inner;
                module = definingModule;
                continue;
            }

            if (cursor is not IntegerType integer)
            {
                return null;
            }

            explicitRepresentation ??= IrOptions.IntegerRepresentation(module.Options);
            if (explicitRepresentation is not null)
            {
                return NormalizeIntegerRepresentation(explicitRepresentation);
            }

            if (integer.NamedValues is { Count: > 0 })
            {
                return InferNamedIntegerRepresentation(integer.NamedValues);
            }

            return InferIntegerRepresentation(integer.Constraint);
        }
    }

    private static string NormalizeIntegerRepresentation(string representation) => representation switch
    {
        IrOptions.IntegerRepresentations.Int32 => IrOptions.IntegerRepresentations.Int32,
        IrOptions.IntegerRepresentations.UInt32 => IrOptions.IntegerRepresentations.UInt32,
        IrOptions.IntegerRepresentations.Int64 => IrOptions.IntegerRepresentations.Int64,
        IrOptions.IntegerRepresentations.UInt64 => IrOptions.IntegerRepresentations.UInt64,
        IrOptions.IntegerRepresentations.BigInt => IrOptions.IntegerRepresentations.BigInt,
        IrOptions.IntegerRepresentations.Der => IrOptions.IntegerRepresentations.Der,
        _ => throw new NotSupportedException($"Unknown integer representation '{representation}'.")
    };

    private static string InferNamedIntegerRepresentation(IReadOnlyList<IrNamedNumber> namedValues)
    {
        var min = namedValues.Min(v => v.Value);
        var max = namedValues.Max(v => v.Value);
        if (min >= int.MinValue && max <= int.MaxValue)
        {
            return IrOptions.IntegerRepresentations.Int32;
        }

        return IrOptions.IntegerRepresentations.Int64;
    }

    private static string InferIntegerRepresentation(IrConstraint? constraint)
    {
        if (constraint?.Value is not { } bound || bound.Max is null)
        {
            return IrOptions.IntegerRepresentations.Der;
        }

        var min = bound.Min;
        var max = bound.Max.Value;
        if (min >= int.MinValue && max <= int.MaxValue)
        {
            return IrOptions.IntegerRepresentations.Int32;
        }

        if (min >= 0 && max <= uint.MaxValue)
        {
            return IrOptions.IntegerRepresentations.UInt32;
        }

        if (min >= long.MinValue && max <= long.MaxValue)
        {
            return IrOptions.IntegerRepresentations.Int64;
        }

        if (min >= 0)
        {
            return IrOptions.IntegerRepresentations.UInt64;
        }

        return IrOptions.IntegerRepresentations.Der;
    }

    private string? ResolvePrimitive(TypeExpr type) => type switch
    {
        BooleanType => TypeKinds.Boolean,
        IntegerType => TypeKinds.Integer,
        BitStringType { Containing: null } => TypeKinds.BitString,
        OctetStringType { Containing: null } => TypeKinds.OctetString,
        NullType => TypeKinds.Null,
        OidType => TypeKinds.Oid,
        StringType => TypeKinds.String,
        TimeType => TypeKinds.Time,
        AnyType => TypeKinds.Any,
        _ => null
    };

    private bool IsEnumeratedRefOrType(IrDocument document, IrModule module, TypeExpr type) =>
        type is EnumeratedType ||
        (type is RefType reference && Find(document, module, reference)?.Type is EnumeratedType);

    private TypeExpr UnwrapAliases(IrDocument document, IrModule module, TypeExpr type) =>
        UnwrapAliases(document, ref module, type);

    private TypeExpr UnwrapAliases(IrDocument document, ref IrModule module, TypeExpr type)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (type is RefType reference)
            {
                var key = (reference.Module ?? module.Name) + "::" + reference.Name;
                if (!visited.Add(key))
                {
                    throw new NotSupportedException($"Circular type alias '{reference.Name}'.");
                }

                var found = FindWithModule(document, module, reference);
                if (found is null)
                {
                    return type;
                }

                if (IrOptions.CppAliasOf(found.Value.Def.Options) is { } aliasOf)
                {
                    var (targetModule, targetDef) = ResolveAliasOfTarget(
                        document, found.Value.Module, found.Value.Def);
                    type = new RefType
                    {
                        Name = targetDef.Name,
                        Module = targetModule.Name,
                        Tag = type.Tag,
                        Options = type.Options
                    };
                    module = targetModule;
                    continue;
                }

                var inner = found.Value.Def.Type;
                if (IsNamedBitString(inner) || NeedsNamedType(inner) || IsEnumerated(inner) || IsNamedInteger(inner))
                {
                    return type;
                }

                if (IsSingleAlternativeChoice(inner))
                {
                    var alt = ((ChoiceType)inner).Components[0].Type;
                    type = CloneWithOuterTag(alt, type.Tag);
                    module = found.Value.Module;
                    continue;
                }

                if (IsCollapsibleAlias(inner))
                {
                    type = CloneWithOuterTag(inner, type.Tag);
                    module = found.Value.Module;
                    continue;
                }

                return type;
            }

            if (IsSingleAlternativeChoice(type))
            {
                var alt = ((ChoiceType)type).Components[0].Type;
                type = CloneWithOuterTag(alt, type.Tag);
                continue;
            }

            return type;
        }
    }

    private static TypeExpr CloneWithOuterTag(TypeExpr inner, IrTag? outerTag)
    {
        var clone = CloneUntagged(inner);
        clone.Tag = outerTag ?? inner.Tag;
        return clone;
    }

    private static TypeExpr CloneUntagged(TypeExpr type)
    {
        var clone = type switch
        {
            BooleanType => (TypeExpr)new BooleanType(),
            NullType => new NullType(),
            OctetStringType octets => new OctetStringType { Containing = octets.Containing },
            OidType => new OidType(),
            IntegerType integer => new IntegerType { NamedValues = integer.NamedValues },
            EnumeratedType enumerated => new EnumeratedType
            {
                Values = enumerated.Values,
                Extensible = enumerated.Extensible
            },
            BitStringType bitString => new BitStringType
            {
                NamedBits = bitString.NamedBits,
                Containing = bitString.Containing
            },
            StringType stringType => new StringType { Form = stringType.Form },
            TimeType timeType => new TimeType
            {
                Form = timeType.Form,
                FractionDigits = timeType.FractionDigits
            },
            AnyType any => new AnyType
            {
                DefinedBy = any.DefinedBy,
                Bindings = any.Bindings,
                Selector = any.Selector,
                TableExtensible = any.TableExtensible
            },
            SequenceType sequence => new SequenceType
            {
                Components = sequence.Components,
                Extensible = sequence.Extensible
            },
            SetType set => new SetType { Components = set.Components, Extensible = set.Extensible },
            ChoiceType choice => new ChoiceType
            {
                Components = choice.Components,
                Extensible = choice.Extensible
            },
            SequenceOfType sequenceOf => new SequenceOfType { Element = sequenceOf.Element },
            SetOfType setOf => new SetOfType { Element = setOf.Element },
            RefType reference => new RefType { Name = reference.Name, Module = reference.Module },
            _ => throw new NotSupportedException($"Cannot clone untagged type kind '{type.Kind}'.")
        };
        clone.Options = type.Options;
        clone.Constraint = type.Constraint;
        return clone;
    }

    private string TagExpr(IrDocument document, IrModule module, TypeExpr type)
    {
        type = UnwrapAliases(document, module, type);
        if (type.Tag is not null)
        {
            var constructed = type.Tag.Mode == TagModes.Explicit || IsConstructed(document, module, type);
            return TagFromIr(type.Tag, constructed);
        }

        return UniversalTag(document, module, type);
    }

    private static string TagFromIr(IrTag tag, bool constructed)
    {
        var number = tag.Number.ToString(CultureInfo.InvariantCulture);
        var args = constructed ? $"{number}, true" : number;

        return tag.Class.ToLowerInvariant() switch
        {
            TagClasses.Universal => constructed
                ? $"asn1kit::tag(asn1kit::tag_class::universal, {number}, true)"
                : $"asn1kit::tag(asn1kit::tag_class::universal, {number})",
            TagClasses.Application => $"asn1kit::tag::application({args})",
            TagClasses.Private => $"asn1kit::tag::private_({args})",
            _ => $"asn1kit::tag::context({args})"
        };
    }

    private string UniversalTag(IrDocument document, IrModule module, TypeExpr type) => type switch
    {
        BooleanType => "asn1kit::tag::boolean",
        IntegerType => "asn1kit::tag::integer",
        EnumeratedType => "asn1kit::tag::enumerated",
        BitStringType => "asn1kit::tag::bit_string",
        OctetStringType => "asn1kit::tag::octet_string",
        NullType => "asn1kit::tag::null",
        OidType => "asn1kit::tag::object_identifier",
        StringType stringType => StringFormTag(stringType.Form),
        TimeType timeType => TimeFormTag(timeType.Form),
        SetType or SetOfType => "asn1kit::tag::set",
        SequenceType or SequenceOfType => "asn1kit::tag::sequence",
        RefType reference when Find(document, module, reference)?.Type is { } inner =>
            UniversalTag(document, module, inner),
        ChoiceType => "asn1kit::tag::sequence",
        _ => "asn1kit::tag::sequence"
    };

    private static string StringFormTag(string form) => form switch
    {
        StringTypes.Utf8 => "asn1kit::tag::utf8_string",
        StringTypes.Numeric => "asn1kit::tag::numeric_string",
        StringTypes.Printable => "asn1kit::tag::printable_string",
        StringTypes.Teletex or StringTypes.T61 => "asn1kit::tag::teletex_string",
        StringTypes.Videotex => "asn1kit::tag::videotex_string",
        StringTypes.Ia5 => "asn1kit::tag::ia5_string",
        StringTypes.Graphic => "asn1kit::tag::graphic_string",
        StringTypes.Visible => "asn1kit::tag::visible_string",
        StringTypes.General => "asn1kit::tag::general_string",
        StringTypes.Universal => "asn1kit::tag::universal_string",
        StringTypes.Bmp => "asn1kit::tag::bmp_string",
        _ => throw new NotSupportedException($"Unknown stringType '{form}'.")
    };

    private static string TimeFormTag(string form) => form switch
    {
        TimeTypes.Utc => "asn1kit::tag::utc_time",
        TimeTypes.Generalized => "asn1kit::tag::generalized_time",
        _ => throw new NotSupportedException($"Unknown timeType '{form}'.")
    };

    private static string StringFormEnum(string form) => form switch
    {
        StringTypes.Utf8 => "asn1kit::string_form::utf8",
        StringTypes.Printable => "asn1kit::string_form::printable",
        StringTypes.Teletex => "asn1kit::string_form::teletex",
        StringTypes.T61 => "asn1kit::string_form::t61",
        StringTypes.Ia5 => "asn1kit::string_form::ia5",
        StringTypes.Numeric => "asn1kit::string_form::numeric",
        StringTypes.Visible => "asn1kit::string_form::visible",
        StringTypes.Bmp => "asn1kit::string_form::bmp",
        StringTypes.Universal => "asn1kit::string_form::universal",
        StringTypes.General => "asn1kit::string_form::general",
        StringTypes.Graphic => "asn1kit::string_form::graphic",
        StringTypes.Videotex => "asn1kit::string_form::videotex",
        _ => throw new NotSupportedException($"Unknown stringType '{form}'.")
    };

    private static string TimeFormEnum(string form) => form switch
    {
        TimeTypes.Utc => "asn1kit::time_form::utc",
        TimeTypes.Generalized => "asn1kit::time_form::generalized",
        _ => throw new NotSupportedException($"Unknown timeType '{form}'.")
    };

    private bool IsConstructed(IrDocument document, IrModule module, TypeExpr type) => type switch
    {
        SequenceType or SetType or SequenceOfType or SetOfType or ChoiceType => true,
        RefType reference => Find(document, module, reference)?.Type is { } inner
            && IsConstructed(document, module, inner),
        _ => false
    };

    private IEnumerable<IrComponent> SortedSetComponents(
        IrDocument document,
        IrModule module,
        IReadOnlyList<IrComponent> components) =>
        components
            .Select((component, index) => (component, index, key: ComponentTagSortKey(document, module, component.Type)))
            .OrderBy(x => x.key.TagClass)
            .ThenBy(x => x.key.Number)
            .ThenBy(x => x.index)
            .Select(x => x.component);

    private (int TagClass, int Number) ComponentTagSortKey(IrDocument document, IrModule module, TypeExpr type)
    {
        type = UnwrapAliases(document, module, type);
        if (type.Tag is not null)
        {
            return (TagClassOrdinal(type.Tag.Class), type.Tag.Number);
        }

        return (0, UniversalTagNumber(document, module, type));
    }

    private static int TagClassOrdinal(string tagClass) => tagClass.ToLowerInvariant() switch
    {
        TagClasses.Universal => 0,
        TagClasses.Application => 1,
        TagClasses.Private => 3,
        _ => 2
    };

    private int UniversalTagNumber(IrDocument document, IrModule module, TypeExpr type) => type switch
    {
        BooleanType => 1,
        IntegerType => 2,
        BitStringType => 3,
        OctetStringType => 4,
        NullType => 5,
        OidType => 6,
        EnumeratedType => 10,
        StringType stringType => StringFormTagNumber(stringType.Form),
        TimeType timeType => timeType.Form == TimeTypes.Utc ? 23 : 24,
        SequenceType or SequenceOfType => 16,
        SetType or SetOfType => 17,
        RefType reference when Find(document, module, reference)?.Type is { } inner =>
            UniversalTagNumber(document, module, inner),
        _ => 16
    };

    private static int StringFormTagNumber(string form) => form switch
    {
        StringTypes.Utf8 => 12,
        StringTypes.Numeric => 18,
        StringTypes.Printable => 19,
        StringTypes.Teletex or StringTypes.T61 => 20,
        StringTypes.Videotex => 21,
        StringTypes.Ia5 => 22,
        StringTypes.Graphic => 25,
        StringTypes.Visible => 26,
        StringTypes.General => 27,
        StringTypes.Universal => 28,
        StringTypes.Bmp => 30,
        _ => throw new NotSupportedException($"Unknown stringType '{form}'.")
    };

    private static IrTypeDef? Find(IrDocument document, IrModule module, RefType reference) =>
        FindWithModule(document, module, reference)?.Def;

    private static (IrModule Module, IrTypeDef Def)? FindWithModule(
        IrDocument document,
        IrModule module,
        RefType reference)
    {
        var modules = string.IsNullOrEmpty(reference.Module)
            ? new[] { module }.Concat(document.Modules)
            : document.Modules.Where(m => m.Name == reference.Module);
        foreach (var candidate in modules)
        {
            var match = candidate.Types.FirstOrDefault(t => t.Name == reference.Name);
            if (match is not null)
            {
                return (candidate, match);
            }
        }

        return null;
    }

    private static (IrModule Module, IrTypeDef Def) ResolveAliasOfTarget(
        IrDocument document,
        IrModule module,
        IrTypeDef definition)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentModule = module;
        var current = definition;
        while (true)
        {
            var key = currentModule.Name + "::" + current.Name;
            if (!visited.Add(key))
            {
                throw new NotSupportedException(
                    $"Circular options.cpp.aliasOf at '{current.Name}'.");
            }

            var aliasOf = IrOptions.CppAliasOf(current.Options);
            if (aliasOf is null)
            {
                return (currentModule, current);
            }

            if (string.IsNullOrWhiteSpace(aliasOf))
            {
                throw new NotSupportedException(
                    $"Type '{currentModule.Name}.{current.Name}' has empty options.cpp.aliasOf.");
            }

            var found = FindWithModule(document, currentModule, new RefType { Name = aliasOf });
            if (found is null)
            {
                throw new NotSupportedException(
                    $"Type '{currentModule.Name}.{current.Name}' options.cpp.aliasOf '{aliasOf}' does not resolve.");
            }

            currentModule = found.Value.Module;
            current = found.Value.Def;
        }
    }

    private static string FieldName(IrComponent field) =>
        IrOptions.CppFieldName(field.Options) ?? CppNames.SanitizeFieldName(field.Name);

    private static string IntegerLiteral(long value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

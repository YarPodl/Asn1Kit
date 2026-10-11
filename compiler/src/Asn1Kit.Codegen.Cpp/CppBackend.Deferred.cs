using System.Text.Json.Nodes;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.Cpp;

public sealed partial class CppBackend
{
    /// <summary>
    /// True when this type usage should be wrapped in <c>asn1kit::lazy&lt;T&gt;</c> (SEQUENCE/SET/OF only).
    /// </summary>
    private bool ShouldEmitLazy(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        JsonObject? fieldOptions)
    {
        if (!IsDeferredEligible(document, module, type))
        {
            return false;
        }

        return ResolveLazyFlag(document, module, type, fieldOptions);
    }

    /// <summary>
    /// True when this type usage should be wrapped in <c>asn1kit::value&lt;T&gt;</c> (SEQUENCE/SET/OF only).
    /// Lazy takes precedence when both flags are set.
    /// </summary>
    private bool ShouldEmitRetainEncoded(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        JsonObject? fieldOptions)
    {
        if (!IsDeferredEligible(document, module, type))
        {
            return false;
        }

        if (ShouldEmitLazy(document, module, type, fieldOptions))
        {
            return false;
        }

        return ResolveRetainEncodedFlag(document, module, type, fieldOptions);
    }

    private bool IsDeferredEligible(IrDocument document, IrModule module, TypeExpr type)
    {
        var unwrapped = UnwrapAliases(document, module, type);
        if (unwrapped is SequenceType or SetType or SequenceOfType or SetOfType)
        {
            return true;
        }

        if (unwrapped is RefType reference)
        {
            var def = Find(document, module, reference);
            return def?.Type is SequenceType or SetType or SequenceOfType or SetOfType;
        }

        return false;
    }

    private bool ResolveLazyFlag(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        JsonObject? fieldOptions) =>
        ResolvePropagatedOptionFlag(document, module, type, fieldOptions, IrOptions.IsLazy);

    private bool ResolveRetainEncodedFlag(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        JsonObject? fieldOptions) =>
        ResolvePropagatedOptionFlag(document, module, type, fieldOptions, IrOptions.IsRetainEncoded);

    private bool ResolvePropagatedOptionFlag(
        IrDocument document,
        IrModule module,
        TypeExpr type,
        JsonObject? fieldOptions,
        Func<JsonObject?, bool> isSet)
    {
        if (isSet(fieldOptions) || isSet(type.Options))
        {
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var cursor = type;
        var currentModule = module;
        while (true)
        {
            if (IsSingleAlternativeChoice(cursor))
            {
                cursor = ((ChoiceType)cursor).Components[0].Type;
                if (isSet(cursor.Options))
                {
                    return true;
                }

                continue;
            }

            if (cursor is not RefType reference)
            {
                return false;
            }

            var key = (reference.Module ?? currentModule.Name) + "::" + reference.Name;
            if (!visited.Add(key))
            {
                return false;
            }

            var found = FindWithModule(document, currentModule, reference);
            if (found is null)
            {
                return false;
            }

            if (isSet(found.Value.Def.Options) || isSet(found.Value.Def.Type.Options))
            {
                return true;
            }

            cursor = found.Value.Def.Type;
            currentModule = found.Value.Module;
        }
    }
}

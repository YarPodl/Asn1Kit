using System.Text;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.CSharp;

public sealed partial class CSharpBackend
{
    private readonly Dictionary<string, (string Owner, IReadOnlyList<IrComponent> Components, string Component)> _nestedParents = new();

    private void RegisterNestedParent(string owner, IReadOnlyList<IrComponent> components, IrComponent field)
    {
        if (field.Type is RefType) return;
        var name = owner + "_" + SanitizeIdentifier(field.Name);
        var type = field.Type;
        while (type is SequenceOfType or SetOfType)
        {
            type = type is SequenceOfType of ? of.Element : ((SetOfType)type).Element;
            name += "_Item";
        }
        if (type is SequenceType or SetType or ChoiceType)
            _nestedParents[name] = (owner, components, field.Name);
    }
    private static string BoolLiteral(bool value) => value ? "true" : "false";
    private static TypeExpr? ContainedType(TypeExpr type) => type switch
    {
        OctetStringType octets => octets.Containing,
        BitStringType bits => bits.Containing,
        _ => null
    };

    private TypeExpr OpenElement(IrDocument document, IrModule module, TypeExpr type)
    {
        type = UnwrapAliases(document, module, type);
        while (true)
        {
            var inner = type switch
            {
                SequenceOfType of => of.Element,
                SetOfType of => of.Element,
                _ => ContainedType(type)
            };
            if (inner is null) return type;
            type = UnwrapAliases(document, module, inner);
        }
    }

    private void ContainedNaming(IrDocument document, IrModule module, TypeExpr original, string owner, string hint,
        out IrModule contentModule, out string contentOwner, out string contentHint)
    {
        contentModule = module; contentOwner = owner + "_" + SanitizeIdentifier(hint); contentHint = "Content";
        while (original is RefType reference)
        {
            var found = FindWithModule(document, contentModule, reference)
                ?? throw new NotSupportedException($"Unresolved contained type '{reference.Name}'.");
            contentModule = found.Module;
            original = found.Def.Type;
            contentOwner = ModuleNamespace(contentModule) + "." + (IrOptions.CSharpTypeName(found.Def.Options) ?? SanitizeIdentifier(found.Def.Name));
            contentHint = "Content";
        }
    }

    private string SelectorKeyExpression(IrDocument document, IrModule module, string owner, IrOpenTypeSelector selector,
        IReadOnlyList<IrComponent>? components, string? target)
    {
        var path = selector.Path;
        if (selector.Levels != 0)
        {
            var route = SelectorRoute(owner, selector.Levels);
            if (path.Count > route.Count && path.Take(route.Count).SequenceEqual(route))
                path = path.Skip(route.Count).ToList();
            else
            {
                target = "parent" + (selector.Levels - 1);
                for (var i = 0; i < selector.Levels; i++)
                {
                    var parent = _nestedParents[owner]; owner = parent.Owner; components = parent.Components;
                }
            }
        }
        if (components is null || target is null) throw new InvalidOperationException($"Selector on '{owner}' requires owner components.");
        TypeExpr? selected = null;
        foreach (var part in path)
        {
            var field = components.Single(c => c.Name == part);
            target += "." + PropertyName(field, owner);
            selected = field.Type;
            if (selected is RefType reference)
            {
                var found = FindWithModule(document, module, reference) ?? throw new NotSupportedException($"Unresolved selector type '{reference.Name}'.");
                module = found.Module; owner = IrOptions.CSharpTypeName(found.Def.Options) ?? SanitizeIdentifier(found.Def.Name);
            }
            else owner += "_" + SanitizeIdentifier(field.Name);
            var (_, unwrapped) = ResolveDefaultTarget(document, module, selected);
            components = unwrapped switch
            {
                SequenceType sequence => sequence.Components,
                SetType set => set.Components,
                _ => Array.Empty<IrComponent>()
            };
        }
        var final = UnwrapAliases(document, module, selected!);
        if (final is OidType) return target;
        if (final is IntegerType)
        {
            var representation = TryResolveIntegerRepresentation(document, module, selected!) ?? IrOptions.IntegerRepresentations.Der;
            return representation == IrOptions.IntegerRepresentations.Der
                ? $"{target}.ToBigInteger().ToString(CultureInfo.InvariantCulture)"
                : $"{target}.ToString(CultureInfo.InvariantCulture)";
        }
        throw new NotSupportedException($"Selector on '{owner}' must select an OID or INTEGER.");
    }
}

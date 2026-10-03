using System.Text;
using Asn1Kit.Ir;

namespace Asn1Kit.Codegen.CSharp;

public sealed partial class CSharpBackend
{
    private readonly Dictionary<string, int> _contextDepth = new(StringComparer.Ordinal);
    private string _activeDecodeOwner = "";
    private IReadOnlyList<string>? _callbackContext;

    private void PlanDecodeContexts(IReadOnlyList<(string Name, TypeExpr Type)> types)
    {
        _contextDepth.Clear();
        foreach (var (owner, type) in types)
        {
            var depth = 0;
            IEnumerable<AnyType> OpenFields(TypeExpr field)
            {
                if (field is AnyType any) yield return any;
                else if (field is SequenceOfType of) foreach (var item in OpenFields(of.Element)) yield return item;
                else if (field is SetOfType set) foreach (var item in OpenFields(set.Element)) yield return item;
                else if (ContainedType(field) is { } content) foreach (var item in OpenFields(content)) yield return item;
            }
            var fields = type switch
            {
                SequenceType sequence => sequence.Components,
                SetType set => set.Components,
                ChoiceType choice => choice.Components,
                _ => new List<IrComponent>()
            };
            foreach (var any in fields.SelectMany(f => OpenFields(f.Type)))
            {
                if (any.Selector is not { Levels: > 0 } selector) continue;
                var route = SelectorRoute(owner, selector.Levels);
                if (selector.Path.Count > route.Count && selector.Path.Take(route.Count).SequenceEqual(route)) continue;
                depth = Math.Max(depth, selector.Levels);
            }
            _contextDepth[owner] = depth;
        }
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (owner, depth) in _contextDepth.ToArray())
            {
                if (depth <= 1 || !_nestedParents.TryGetValue(owner, out var parent)) continue;
                if (_contextDepth.GetValueOrDefault(parent.Owner) >= depth - 1) continue;
                _contextDepth[parent.Owner] = depth - 1; changed = true;
            }
        }
    }

    private List<string> SelectorRoute(string owner, int levels)
    {
        var route = new List<string>();
        for (var i = 0; i < levels; i++)
        {
            if (!_nestedParents.TryGetValue(owner, out var parent))
                throw new NotSupportedException($"Enclosing selector on '{owner}' has no enclosing type.");
            route.Insert(0, parent.Component); owner = parent.Owner;
        }
        return route;
    }

    private void EmitDecodeContextHeader(StringBuilder sb, string owner, bool tagged)
    {
        var depth = _contextDepth.GetValueOrDefault(owner);
        var prefix = $"    public static {owner} Decode(Asn1Reader reader" + (tagged ? ", Asn1Tag tag" : "");
        if (depth == 0) { sb.AppendLine(prefix + ")"); return; }
        sb.AppendLine(prefix + ") => throw new Asn1Exception(\"This nested type requires an enclosing decoding context.\");");
        var parentOwner = owner;
        for (var i = 0; i < depth; i++)
        {
            parentOwner = _nestedParents[parentOwner].Owner;
            prefix += $", {parentOwner} parent{i}";
        }
        sb.AppendLine(prefix + ")");
    }

    private IReadOnlyList<string> ChildContext(string owner)
    {
        var depth = _contextDepth.GetValueOrDefault(owner);
        if (depth == 0) return Array.Empty<string>();
        if (_callbackContext is not null) return _callbackContext.Take(depth).ToArray();
        return Enumerable.Range(0, depth).Select(i => i == 0 ? "value" : "parent" + (i - 1)).ToArray();
    }

    private string ContextSuffix(string owner) => string.Concat(ChildContext(owner).Select(a => ", " + a));
}

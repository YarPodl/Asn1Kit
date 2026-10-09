using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Asn1Kit.Ir;

/// <summary>
/// Sidecar overlay that attaches open-type tables after ASN.1 compile.
/// Keys are <c>Module.Type.field</c> (ASN.1 component name).
/// After apply, tables are normalized to the modern IR shape:
/// <c>any.selector</c> on the field body and <c>ref.openTypes</c> on uses
/// (or definition-scoped <c>any.bindings</c> + <c>tableExtensible</c> when the type has no refs).
/// </summary>
public static class OpenTypeBindings
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static void ApplyFile(IrDocument document, string path)
    {
        if (!File.Exists(path))
        {
            throw new IrException($"Open-type bindings file not found: {path}");
        }

        ApplyJson(document, File.ReadAllText(path));
    }

    public static void ApplyJson(IrDocument document, string json)
    {
        List<BindingTarget> targets;
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new IrException("Open-type bindings JSON root must be an object.");
            }

            targets = new List<BindingTarget>();
            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    targets.Add(new BindingTarget
                    {
                        Path = property.Name,
                        Bindings = property.Value.Deserialize<List<IrOpenTypeBinding>>(JsonOptions)
                    });
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    var descriptor = property.Value.Deserialize<BindingTargetDescriptor>(JsonOptions)
                        ?? throw new IrException(
                            $"Open-type binding target '{property.Name}' is empty.");
                    targets.Add(new BindingTarget
                    {
                        Path = property.Name,
                        DefinedBy = descriptor.DefinedBy,
                        Bindings = descriptor.Bindings
                    });
                    continue;
                }

                throw new IrException(
                    $"Open-type binding target '{property.Name}' must be an array or object.");
            }
        }
        catch (JsonException ex)
        {
            throw new IrException("Open-type bindings JSON is invalid: " + ex.Message);
        }

        ApplyTargets(document, targets);
    }

    public static void Apply(IrDocument document, IReadOnlyDictionary<string, List<IrOpenTypeBinding>> map)
    {
        ApplyTargets(
            document,
            map.Select(entry => new BindingTarget
            {
                Path = entry.Key,
                Bindings = entry.Value
            }));
    }

    /// <summary>
    /// Normalizes legacy inline <c>any.bindings</c> into modern <c>selector</c> + <c>ref.openTypes</c>
    /// (or definition-scoped bindings with <c>tableExtensible</c> when the owner has no refs).
    /// </summary>
    public static void NormalizeLegacy(IrDocument document)
    {
        var changed = false;
        foreach (var module in document.Modules)
        {
            foreach (var typeDef in module.Types)
            {
                if (typeDef.Type is not SequenceType and not SetType) continue;
                var components = typeDef.Type is SequenceType sequence
                    ? sequence.Components
                    : ((SetType)typeDef.Type).Components;
                foreach (var component in components)
                {
                    if (component.Type is not AnyType { Bindings.Count: > 0 } any) continue;
                    // Already has a modern selector (IOC / hand-authored ancestor paths) — leave it alone.
                    if (any.Selector is not null) continue;
                    NormalizeField(document, module, typeDef.Name, component, any);
                    changed = true;
                }
            }
        }

        // Re-validate only when this pass rewrote IR. Hand-authored ancestor selectors
        // (Levels > 0) are checked in their use context by the backend, not at definition.
        if (changed)
        {
            IrValidator.Validate(document);
        }
    }

    private static void ApplyTargets(IrDocument document, IEnumerable<BindingTarget> targets)
    {
        foreach (var target in targets)
        {
            var path = target.Path;
            var bindings = target.Bindings;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new IrException("Open-type binding path is required.");
            }

            var parts = path.Split('.');
            if (parts.Length != 3 ||
                parts.Any(string.IsNullOrWhiteSpace))
            {
                throw new IrException(
                    $"Open-type binding path '{path}' must be Module.Type.field.");
            }

            var moduleName = parts[0];
            var typeName = parts[1];
            var fieldName = parts[2];

            var module = document.Modules.FirstOrDefault(m => m.Name == moduleName);
            if (module is null)
            {
                // Sidecar may list targets from a multi-module set; skip when that module is absent.
                continue;
            }

            var typeDef = module.Types.FirstOrDefault(t => t.Name == typeName)
                ?? throw new IrException($"Open-type binding path '{path}': type '{typeName}' not found.");

            if (typeDef.Type is not SequenceType and not SetType)
            {
                throw new IrException(
                    $"Open-type binding path '{path}': type '{typeName}' is not SEQUENCE/SET.");
            }

            var components = typeDef.Type is SequenceType sequence
                ? sequence.Components
                : ((SetType)typeDef.Type).Components;
            var component = components.FirstOrDefault(c => c.Name == fieldName)
                ?? throw new IrException(
                    $"Open-type binding path '{path}': field '{fieldName}' not found.");

            var any = ResolveAny(document, module, component.Type, path);
            var definedBy = target.DefinedBy ?? any.DefinedBy;
            if (string.IsNullOrWhiteSpace(definedBy))
            {
                throw new IrException(
                    $"Open-type binding path '{path}': ANY has no definedBy; " +
                    "use the object form to specify it.");
            }

            if (target.DefinedBy is not null &&
                any.DefinedBy is not null &&
                !string.Equals(target.DefinedBy, any.DefinedBy, StringComparison.Ordinal))
            {
                throw new IrException(
                    $"Open-type binding path '{path}': definedBy '{target.DefinedBy}' " +
                    $"does not match ASN.1 definedBy '{any.DefinedBy}'.");
            }

            if (bindings is null || bindings.Count == 0)
            {
                throw new IrException($"Open-type binding path '{path}' has no bindings.");
            }

            component.Type = new AnyType
            {
                Tag = CloneTag(component.Type.Tag ?? any.Tag),
                Constraint = CloneConstraint(component.Type.Constraint ?? any.Constraint),
                Options = CloneOptions(component.Type.Options ?? any.Options),
                DefinedBy = definedBy,
                Bindings = bindings
                .Select(b => new IrOpenTypeBinding
                {
                    Key = b.Key,
                    Name = b.Name,
                    Type = b.Type
                })
                .ToList()
            };
        }

        NormalizeLegacy(document);
    }

    private static void NormalizeField(
        IrDocument document,
        IrModule module,
        string typeName,
        IrComponent component,
        AnyType any)
    {
        var definedBy = any.DefinedBy
            ?? throw new IrException(
                $"Open-type field '{module.Name}.{typeName}.{component.Name}' has bindings but no definedBy.");
        var bindings = any.Bindings!
            .Select(b => new IrOpenTypeBinding
            {
                Key = b.Key,
                Name = b.Name,
                Type = CloneType(b.Type)
            })
            .ToList();

        any.Selector = new IrOpenTypeSelector
        {
            Levels = 0,
            Path = new List<string> { definedBy }
        };

        var use = new IrOpenTypeUse
        {
            Path = new List<string> { component.Name },
            Bindings = bindings.Select(b => new IrOpenTypeBinding
            {
                Key = b.Key,
                Name = b.Name,
                Type = CloneType(b.Type)
            }).ToList(),
            TableExtensible = false
        };

        var refCount = AttachOpenTypesToReferences(document, module.Name, typeName, use);
        if (refCount > 0)
        {
            // Shared type body matches IOC compaction: selector only; tables live on uses.
            any.Bindings = null;
            any.Table = null;
            any.TableExtensible = null;
        }
        else
        {
            // Orphan type (no refs): keep definition-scoped bindings; tableExtensible marks modern policy.
            any.Bindings = bindings;
            any.TableExtensible = false;
        }
    }

    private static int AttachOpenTypesToReferences(
        IrDocument document,
        string ownerModule,
        string ownerType,
        IrOpenTypeUse use)
    {
        var count = 0;
        foreach (var module in document.Modules)
        {
            foreach (var definition in module.Types)
                Walk(definition.Type, module);
            foreach (var definition in module.Values)
                Walk(definition.Type, module);
        }

        return count;

        void Walk(TypeExpr type, IrModule context)
        {
            switch (type)
            {
                case RefType reference:
                    if (ResolvesTo(document, context, reference, ownerModule, ownerType))
                    {
                        reference.OpenTypes ??= new List<IrOpenTypeUse>();
                        if (!reference.OpenTypes.Any(existing =>
                                existing.Path.Count == use.Path.Count &&
                                existing.Path.SequenceEqual(use.Path, StringComparer.Ordinal)))
                        {
                            reference.OpenTypes.Add(CloneUse(use));
                            count++;
                        }
                    }
                    if (reference.OpenTypes is not null)
                    {
                        foreach (var nested in reference.OpenTypes)
                            foreach (var binding in nested.Bindings)
                                Walk(binding.Type, context);
                    }
                    break;
                case AnyType open when open.Bindings is not null:
                    foreach (var binding in open.Bindings)
                        Walk(binding.Type, context);
                    break;
                case SequenceType sequence:
                    foreach (var field in sequence.Components)
                        Walk(field.Type, context);
                    break;
                case SetType set:
                    foreach (var field in set.Components)
                        Walk(field.Type, context);
                    break;
                case ChoiceType choice:
                    foreach (var field in choice.Components)
                        Walk(field.Type, context);
                    break;
                case SequenceOfType of:
                    Walk(of.Element, context);
                    break;
                case SetOfType of:
                    Walk(of.Element, context);
                    break;
                case OctetStringType { Containing: { } containing }:
                    Walk(containing, context);
                    break;
                case BitStringType { Containing: { } containing }:
                    Walk(containing, context);
                    break;
            }
        }
    }

    private static bool ResolvesTo(
        IrDocument document,
        IrModule context,
        RefType reference,
        string ownerModule,
        string ownerType)
    {
        if (!string.Equals(reference.Name, ownerType, StringComparison.Ordinal))
            return false;

        if (reference.Module is not null)
            return string.Equals(reference.Module, ownerModule, StringComparison.Ordinal);

        var local = context.Types.FirstOrDefault(t => t.Name == reference.Name);
        if (local is not null)
            return string.Equals(context.Name, ownerModule, StringComparison.Ordinal);

        var import = context.Imports.FirstOrDefault(i => i.Types.Contains(reference.Name));
        if (import is not null)
            return string.Equals(import.Module, ownerModule, StringComparison.Ordinal);

        return string.Equals(context.Name, ownerModule, StringComparison.Ordinal);
    }

    private static IrOpenTypeUse CloneUse(IrOpenTypeUse use) => new()
    {
        Path = use.Path.ToList(),
        Bindings = use.Bindings.Select(b => new IrOpenTypeBinding
        {
            Key = b.Key,
            Name = b.Name,
            Type = CloneType(b.Type)
        }).ToList(),
        Table = use.Table,
        TableExtensible = use.TableExtensible
    };

    private static TypeExpr CloneType(TypeExpr type) =>
        JsonSerializer.Deserialize<TypeExpr>(
            JsonSerializer.Serialize(type, IrSerializer.JsonOptions), IrSerializer.JsonOptions)!;

    private static AnyType ResolveAny(
        IrDocument document,
        IrModule startModule,
        TypeExpr type,
        string path)
    {
        var module = startModule;
        var current = type;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current is RefType reference)
        {
            var resolved = ResolveType(document, module, reference, path);
            module = resolved.Module;
            var identity = module.Name + "." + resolved.Type.Name;
            if (!visited.Add(identity))
            {
                throw new IrException(
                    $"Open-type binding path '{path}': alias cycle at '{identity}'.");
            }

            current = resolved.Type.Type;
        }

        return current as AnyType
            ?? throw new IrException(
                $"Open-type binding path '{path}': field does not resolve to ANY.");
    }

    private static (IrModule Module, IrTypeDef Type) ResolveType(
        IrDocument document,
        IrModule module,
        RefType reference,
        string path)
    {
        IrModule? targetModule;
        if (reference.Module is not null)
        {
            targetModule = document.Modules.FirstOrDefault(candidate => candidate.Name == reference.Module);
        }
        else
        {
            var local = module.Types.FirstOrDefault(candidate => candidate.Name == reference.Name);
            if (local is not null)
            {
                return (module, local);
            }

            var import = module.Imports.FirstOrDefault(candidate => candidate.Types.Contains(reference.Name));
            targetModule = import is null
                ? null
                : document.Modules.FirstOrDefault(candidate => candidate.Name == import.Module);
        }

        var type = targetModule?.Types.FirstOrDefault(candidate => candidate.Name == reference.Name);
        if (targetModule is null || type is null)
        {
            throw new IrException(
                $"Open-type binding path '{path}': type reference '{reference.Name}' cannot be resolved.");
        }

        return (targetModule, type);
    }

    private static IrTag? CloneTag(IrTag? tag) => tag is null
        ? null
        : new IrTag { Class = tag.Class, Number = tag.Number, Mode = tag.Mode };

    private static IrConstraint? CloneConstraint(IrConstraint? constraint) => constraint is null
        ? null
        : new IrConstraint
        {
            Size = CloneBound(constraint.Size),
            Value = CloneBound(constraint.Value),
            Unsupported = constraint.Unsupported
        };

    private static IrBound? CloneBound(IrBound? bound) => bound is null
        ? null
        : new IrBound { Min = bound.Min, Max = bound.Max };

    private static JsonObject? CloneOptions(JsonObject? options) => options is null
        ? null
        : JsonNode.Parse(options.ToJsonString())!.AsObject();

    private sealed class BindingTarget
    {
        public string Path { get; init; } = "";

        public string? DefinedBy { get; init; }

        public List<IrOpenTypeBinding>? Bindings { get; init; }
    }

    private sealed class BindingTargetDescriptor
    {
        public string? DefinedBy { get; set; }

        public List<IrOpenTypeBinding>? Bindings { get; set; }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new TypeExprConverter());
        options.Converters.Add(new IrValueConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

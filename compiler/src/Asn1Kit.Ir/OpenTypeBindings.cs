using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Asn1Kit.Ir;

/// <summary>
/// Sidecar overlay that attaches open-type <see cref="AnyType.Bindings"/> after ASN.1 compile.
/// Keys are <c>Module.Type.field</c> (ASN.1 component name).
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

        IrValidator.Validate(document);
    }

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

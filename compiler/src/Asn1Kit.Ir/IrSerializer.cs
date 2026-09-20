using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace Asn1Kit.Ir;

public static class IrSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private static JsonSchema? _schema;

    public static string ToJson(IrDocument document)
    {
        Normalize(document);
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static IrDocument FromJson(string json, bool validateSchema = true)
    {
        if (validateSchema)
        {
            ValidateSchema(json);
        }

        var document = JsonSerializer.Deserialize<IrDocument>(json, JsonOptions)
                       ?? throw new IrException("JSON did not contain an IR document.");
        Normalize(document);
        IrValidator.Validate(document);
        return document;
    }

    public static IrDocument Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

    public static void Save(IrDocument document, string path)
    {
        var json = ToJson(document);
        File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void ValidateSchema(string json)
    {
        var schema = GetSchema();
        var node = JsonNode.Parse(json) ?? throw new IrException("IR JSON is empty.");
        var result = schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = false
        });
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Details
            .Where(d => !d.IsValid && d.HasErrors)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
            .Take(8);
        throw new IrException("IR JSON does not match schema v1. " + string.Join("; ", errors));
    }

    public static JsonSchema GetSchema()
    {
        if (_schema is not null)
        {
            return _schema;
        }

        var text = LoadSchemaText();
        _schema = JsonSchema.FromText(text);
        return _schema;
    }

    private static string LoadSchemaText()
    {
        var assembly = typeof(IrSerializer).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("asn1kit-ir-v1.json", StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                               ?? throw new IrException("Embedded IR schema is missing.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        var fromFile = FindSchemaFile();
        if (fromFile is not null)
        {
            return File.ReadAllText(fromFile, Encoding.UTF8);
        }

        throw new IrException("Could not load schemas/asn1kit-ir-v1.json.");
    }

    private static string? FindSchemaFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "schemas", "asn1kit-ir-v1.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static void Normalize(IrDocument document)
    {
        document.Modules ??= new List<IrModule>();
        foreach (var module in document.Modules)
        {
            module.Imports ??= new List<IrImport>();
            module.Types ??= new List<IrTypeDef>();
            module.Values ??= new List<IrValueDef>();
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new TypeExprConverter());
        options.Converters.Add(new IrValueConverter());
        return options;
    }
}

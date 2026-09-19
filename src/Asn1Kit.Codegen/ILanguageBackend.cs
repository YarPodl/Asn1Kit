using Asn1Kit.Ir;

namespace Asn1Kit.Codegen;

public sealed record GeneratedFile(string RelativePath, string Contents);

public interface ILanguageBackend
{
    string LanguageId { get; }

    IReadOnlyList<GeneratedFile> Generate(IrModule module);
}

public sealed class CodeGenerator
{
    private readonly IReadOnlyDictionary<string, ILanguageBackend> _backends;

    public CodeGenerator(IEnumerable<ILanguageBackend> backends)
    {
        _backends = backends.ToDictionary(b => b.LanguageId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<GeneratedFile> Generate(IrModule module, string language)
    {
        IrValidator.Validate(module);
        if (!_backends.TryGetValue(language, out var backend))
        {
            throw new InvalidOperationException($"Unsupported language '{language}'.");
        }

        return backend.Generate(module);
    }
}

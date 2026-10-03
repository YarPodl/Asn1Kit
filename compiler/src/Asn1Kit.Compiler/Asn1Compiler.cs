using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

public sealed class Asn1Compiler
{
    public IrDocument CompileText(string text, string? fileName = null) =>
        CompileTexts(new[] { (text, fileName) });

    public IrDocument CompileFiles(IEnumerable<string> paths)
    {
        var texts = paths.Select(path => (File.ReadAllText(path), (string?)Path.GetFileName(path)));
        return CompileTexts(texts);
    }

    public IrDocument CompileTexts(IEnumerable<(string Text, string? FileName)> inputs)
    {
        var modules = new List<ModuleAst>();
        foreach (var (text, fileName) in inputs)
        {
            try { modules.Add(Asn1Parser.Parse(text, fileName)); }
            catch (CompileException error) when (fileName is not null && error.SourceFile is null)
            { throw new CompileException(error.Detail, error.Line, error.Column, fileName); }
        }

        var document = new IrBuilder(new InformationResolver(modules).Resolve()).Build();
        SpecializationCompactor.Compact(document);
        IrValidator.Validate(document);
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
        return document;
    }
}

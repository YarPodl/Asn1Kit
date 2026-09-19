using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

public sealed class Asn1Compiler
{
    public IrModule CompileText(string text, string? fileName = null)
    {
        return CompileTexts(new[] { (text, fileName) })[0];
    }

    public IReadOnlyList<IrModule> CompileFiles(IEnumerable<string> paths)
    {
        var texts = paths.Select(path => (File.ReadAllText(path), (string?)Path.GetFileName(path)));
        return CompileTexts(texts);
    }

    public IReadOnlyList<IrModule> CompileTexts(IEnumerable<(string Text, string? FileName)> inputs)
    {
        var modules = new List<ModuleAst>();
        foreach (var (text, fileName) in inputs)
        {
            modules.Add(Asn1Parser.Parse(text, fileName));
        }

        var ir = new IrBuilder(modules).Build();
        foreach (var module in ir)
        {
            IrValidator.Validate(module);
        }

        return ir;
    }
}

using System.CommandLine;
using Asn1Kit.Codegen;
using Asn1Kit.Codegen.CSharp;
using Asn1Kit.Compiler;
using Asn1Kit.Ir;

namespace Asn1Kit.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        var compileInputs = new Option<FileInfo[]>(new[] { "--input", "-i" }, "ASN.1 module files")
        {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore
        };
        var compileOutput = new Option<FileInfo>(new[] { "--output", "-o" }, "JSON IR output path")
        {
            IsRequired = true
        };

        var compile = new Command("compile", "Compile ASN.1 modules to JSON IR")
        {
            compileInputs,
            compileOutput
        };
        compile.SetHandler(Compile, compileInputs, compileOutput);

        var generateInput = new Option<FileInfo>(new[] { "--input", "-i" }, "JSON IR or ASN.1 module")
        {
            IsRequired = true
        };
        var language = new Option<string>("--lang", "Target language (csharp)")
        {
            IsRequired = true
        };
        var generateOutput = new Option<DirectoryInfo>(new[] { "--output", "-o" }, "Directory for generated sources")
        {
            IsRequired = true
        };

        var generate = new Command("generate", "Generate code from JSON IR or ASN.1")
        {
            generateInput,
            language,
            generateOutput
        };
        generate.SetHandler(Generate, generateInput, language, generateOutput);

        var root = new RootCommand("Asn1Kit — compile ASN.1 and generate codecs")
        {
            compile,
            generate
        };

        return root.Invoke(args);
    }

    private static void Compile(FileInfo[] inputs, FileInfo output)
    {
        var missing = inputs.FirstOrDefault(i => !i.Exists);
        if (missing is not null)
        {
            throw new FileNotFoundException($"Input not found: {missing.FullName}");
        }

        var document = new Asn1Compiler().CompileFiles(inputs.Select(i => i.FullName));
        output.Directory?.Create();
        IrSerializer.Save(document, output.FullName);
        Console.WriteLine($"Wrote {output.FullName}");
    }

    private static void Generate(FileInfo input, string language, DirectoryInfo output)
    {
        if (!input.Exists)
        {
            throw new FileNotFoundException($"Input not found: {input.FullName}");
        }

        var document = LoadDocument(input);
        var generator = new CodeGenerator(new ILanguageBackend[] { new CSharpBackend() });
        var files = generator.Generate(document, language);
        output.Create();
        foreach (var file in files)
        {
            var path = Path.Combine(output.FullName, file.RelativePath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, file.Contents);
            Console.WriteLine($"Wrote {path}");
        }
    }

    private static IrDocument LoadDocument(FileInfo input)
    {
        return input.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? IrSerializer.Load(input.FullName)
            : new Asn1Compiler().CompileFiles(new[] { input.FullName });
    }
}

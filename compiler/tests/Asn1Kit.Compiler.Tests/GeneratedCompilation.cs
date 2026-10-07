using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Asn1Kit.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Asn1Kit.Tests;

internal static class GeneratedCompilation
{
    public static IReadOnlyList<MetadataReference> References { get; } =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(File.Exists)
        .Select(path => MetadataReference.CreateFromFile(path))
        .Append(MetadataReference.CreateFromFile(typeof(Asn1Writer).Assembly.Location))
        .ToArray();

    private static readonly ConcurrentDictionary<string, Lazy<CachedModule>> Modules =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Assembly> LoadedByName =
        new(StringComparer.Ordinal);

    static GeneratedCompilation()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            return name is not null && LoadedByName.TryGetValue(name, out var assembly)
                ? assembly
                : null;
        };
    }

    public static Assembly CompileSources(params string[] sources)
    {
        var pe = Emit(sources, References, out var assemblyName);
        var assembly = Assembly.Load(pe);
        LoadedByName[assemblyName] = assembly;
        return assembly;
    }

    /// <summary>
    /// Emits <paramref name="probeSource"/> against a module compiled once per <paramref name="cacheKey"/>.
    /// <paramref name="moduleSourcesFactory"/> runs only on the first miss for that key.
    /// </summary>
    public static Assembly CompileProbeAgainstCachedModule(
        string cacheKey,
        Func<string[]> moduleSourcesFactory,
        string probeSource)
    {
        var module = Modules.GetOrAdd(
            cacheKey,
            static (_, factory) => new Lazy<CachedModule>(
                () => CachedModule.Compile(factory()),
                LazyThreadSafetyMode.ExecutionAndPublication),
            moduleSourcesFactory).Value;
        var pe = Emit(
            new[] { probeSource },
            References.Append(module.Reference).ToArray(),
            out var probeName);
        var assembly = Assembly.Load(pe);
        LoadedByName[probeName] = assembly;
        return assembly;
    }

    private static byte[] Emit(
        string[] sources,
        IEnumerable<MetadataReference> references,
        out string assemblyName)
    {
        assemblyName = "Generated" + Guid.NewGuid().ToString("N");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(
                "\n",
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        return stream.ToArray();
    }

    private sealed class CachedModule
    {
        private CachedModule(MetadataReference reference, Assembly assembly)
        {
            Reference = reference;
            Assembly = assembly;
        }

        public MetadataReference Reference { get; }
        public Assembly Assembly { get; }

        public static CachedModule Compile(string[] sources)
        {
            var pe = Emit(sources, References, out var assemblyName);
            var assembly = Assembly.Load(pe);
            LoadedByName[assemblyName] = assembly;
            var reference = MetadataReference.CreateFromImage(ImmutableArray.Create(pe));
            return new CachedModule(reference, assembly);
        }
    }
}

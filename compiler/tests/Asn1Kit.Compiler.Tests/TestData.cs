using System.Collections.Concurrent;
using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

internal static class TestData
{
    private static readonly ConcurrentDictionary<string, Lazy<string>> ValidatedIrJson =
        new(StringComparer.OrdinalIgnoreCase);

    public static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Asn1Kit.sln")))
            {
                return Path.Combine(dir.FullName, relative);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    public static IrDocument LoadIr(string relative)
    {
        var path = RepoPath(relative);
        var json = ValidatedIrJson.GetOrAdd(
            path,
            static p => new Lazy<string>(
                () => ReadAndValidateIr(p),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return IrSerializer.FromJson(json, validateSchema: false);
    }

    private static string ReadAndValidateIr(string path)
    {
        var json = File.ReadAllText(path);
        IrSerializer.ValidateSchema(json);
        return json;
    }
}

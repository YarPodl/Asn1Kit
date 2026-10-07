using Asn1Kit.Runtime;
using Microsoft.CodeAnalysis;

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
}

namespace Asn1Kit.Pkix.Benchmarks;

internal static class FixtureLoader
{
    public static byte[] ReadPkix(string fileName) =>
        File.ReadAllBytes(Path.Combine(FindRepoRoot(), "runtime-csharp", "fixtures", "pkix", fileName));

    public static byte[] ReadCms(string fileName) =>
        File.ReadAllBytes(Path.Combine(FindRepoRoot(), "runtime-csharp", "fixtures", "cms", fileName));

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Asn1Kit.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate Asn1Kit.sln from " + AppContext.BaseDirectory);
    }
}

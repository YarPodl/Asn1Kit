using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Asn1Kit.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        Smoke.VerifyFixtures();
        if (args.Any(static a => string.Equals(a, "--alloc-profile", StringComparison.OrdinalIgnoreCase)))
        {
            return AllocProfile.Run();
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance);
        return 0;
    }
}

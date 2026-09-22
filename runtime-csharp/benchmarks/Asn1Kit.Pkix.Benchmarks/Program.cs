using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Asn1Kit.Pkix.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        Smoke.VerifyFixtures();
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance);
        return 0;
    }
}

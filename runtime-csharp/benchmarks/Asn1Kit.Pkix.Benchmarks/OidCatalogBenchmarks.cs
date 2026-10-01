using Asn1Kit.Pkix;
using Asn1Kit.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Asn1Kit.Benchmarks;

/// <summary>Compares repeated OID conversion with generated module constants.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class OidCatalogBenchmarks
{
    private const string Dotted = "1.3.6.1.5.5.7";
    private Asn1Oid _decoded;

    [GlobalSetup]
    public void Setup() => _decoded = Asn1Oid.Parse(Dotted);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("OID", "DER")]
    public Asn1Oid ParseEachTime() => Asn1Oid.Parse(Dotted);

    [Benchmark]
    [BenchmarkCategory("OID", "DER")]
    public Asn1Oid CachedDerContents() => PKIX1Explicit88Oids.IdPkix;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("OID", "String")]
    public string FormatEachTime() => _decoded.ToString();

    [Benchmark]
    [BenchmarkCategory("OID", "String")]
    public string CachedString() => PKIX1Explicit88Oids.IdPkixString;
}

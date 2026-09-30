# Writer length / small INTEGER optimizations

## Environment

- Git before production changes: `eb0984b` (benchmark class added in working tree).
- Windows 11, .NET SDK 6.0.402, .NET 6.0.10 x64 RyuJIT AVX2.
- BenchmarkDotNet `ShortRun`: 1 launch, 3 warmup iterations, 3 measurement iterations.
- Baseline and optimized runs used the same machine and benchmark sources. Production changes were temporarily reverted for the end-to-end baseline and restored afterward.

## Microbenchmarks

| Method | Baseline | Optimized | Change | Allocated before → after |
| --- | ---: | ---: | ---: | ---: |
| Sequence128 | 112.1 ns | 92.31 ns | −17.7% | 0 → 0 B |
| Sequence256 | 110.9 ns | 91.08 ns | −17.9% | 0 → 0 B |
| Sequence64K | 51.174 µs | 6.277 µs | −87.7% | 0 → 0 B |
| ShortSiblingSequences | 3.467 µs | 2.351 µs | −32.2% | 0 → 0 B |
| DeepShortSequence | 628.3 ns | 350.0 ns | −44.3% | 0 → 0 B |
| CachedIntegerFactories | 76.019 µs | 26.152 µs | −65.6% | 32608 → 0 B |
| UncachedIntegerFactories | 96.504 µs | 73.834 µs | −23.5% | 40960 → 40960 B |

`Sequence64K` and several ShortRun confidence intervals are wide; the result is retained as directional evidence, while the stable short/nested and end-to-end cases are the primary timing signal. Allocation measurements are stable.

## End-to-end encode

| Method | Baseline | Optimized | Change | Allocated |
| --- | ---: | ---: | ---: | ---: |
| Certificate Asn1Kit_Encode | 4.878 µs | 4.043 µs | −17.1% | 4.13 KB |
| CRL Asn1Kit_Encode | 4.236 µs | 3.548 µs | −16.2% | 2.82 KB |
| CMS Asn1Kit_Lazy_Encode | 2.523 µs | 2.198 µs | −12.9% | 2.96 KB |
| CMS Asn1Kit_Eager_Encode | 5.856 µs | 4.548 µs | −22.3% | 4.45 KB |

## Raw reports

- `WriterOptimizationBenchmarks-baseline.*` / `WriterOptimizationBenchmarks-optimized.*`
- `EncodeBenchmarks-baseline.*` / `EncodeBenchmarks-optimized.*`

CSV and GitHub Markdown reports are checked in alongside this summary. The full HTML and BenchmarkDotNet logs remain generated artifacts.

```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]   : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  ShortRun : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Type                      | Method               | Categories         | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------- |--------------------- |------------------- |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| CmsBenchmarks             | Asn1Kit_Lazy_Encode  | Encode,CMS         | 2.198 μs | 0.0538 μs | 0.0029 μs |  1.00 |    0.00 | 0.3214 |      - |   2.96 KB |        1.00 |
| CmsBenchmarks             | Asn1Kit_Eager_Encode | Encode,CMS         | 4.548 μs | 7.6115 μs | 0.4172 μs |  2.07 |    0.19 | 0.4807 |      - |   4.45 KB |        1.50 |
| CmsBenchmarks             | Bcl_Encode           | Encode,CMS         | 4.042 μs | 0.8119 μs | 0.0445 μs |  1.84 |    0.02 | 0.9632 | 0.0095 |   8.85 KB |        2.99 |
| CmsBenchmarks             | BouncyCastle_Encode  | Encode,CMS         | 5.419 μs | 8.8918 μs | 0.4874 μs |  2.47 |    0.22 | 0.5531 | 0.0038 |   5.11 KB |        1.73 |
|                           |                      |                    |          |           |           |       |         |        |        |           |             |
| CertificateListBenchmarks | Asn1Kit_Encode       | Encode,CRL         | 3.548 μs | 0.1560 μs | 0.0085 μs |  1.00 |    0.00 | 0.3052 |      - |   2.82 KB |        1.00 |
| CertificateListBenchmarks | BouncyCastle_Encode  | Encode,CRL         | 4.720 μs | 0.9359 μs | 0.0513 μs |  1.33 |    0.02 | 0.4349 | 0.0038 |   4.01 KB |        1.42 |
|                           |                      |                    |          |           |           |       |         |        |        |           |             |
| CertificateBenchmarks     | Asn1Kit_Encode       | Encode,Certificate | 4.043 μs | 0.4103 μs | 0.0225 μs |  1.00 |    0.00 | 0.4482 |      - |   4.13 KB |        1.00 |
| CertificateBenchmarks     | BouncyCastle_Encode  | Encode,Certificate | 5.718 μs | 1.0439 μs | 0.0572 μs |  1.41 |    0.02 | 0.5074 | 0.0038 |   4.68 KB |        1.13 |

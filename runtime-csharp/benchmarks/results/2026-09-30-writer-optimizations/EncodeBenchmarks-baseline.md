```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]   : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  ShortRun : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Type                      | Method               | Categories         | Mean     | Error      | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------- |--------------------- |------------------- |---------:|-----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| CmsBenchmarks             | Asn1Kit_Lazy_Encode  | Encode,CMS         | 2.523 μs |  0.5452 μs | 0.0299 μs |  1.00 |    0.00 | 0.3204 |      - |   2.96 KB |        1.00 |
| CmsBenchmarks             | Asn1Kit_Eager_Encode | Encode,CMS         | 5.856 μs |  0.7271 μs | 0.0399 μs |  2.32 |    0.01 | 0.4807 |      - |   4.45 KB |        1.50 |
| CmsBenchmarks             | Bcl_Encode           | Encode,CMS         | 2.842 μs | 23.8702 μs | 1.3084 μs |  1.12 |    0.50 | 0.9632 | 0.0095 |   8.85 KB |        2.99 |
| CmsBenchmarks             | BouncyCastle_Encode  | Encode,CMS         | 2.123 μs |  0.5856 μs | 0.0321 μs |  0.84 |    0.02 | 0.5531 | 0.0038 |   5.11 KB |        1.73 |
|                           |                      |                    |          |            |           |       |         |        |        |           |             |
| CertificateListBenchmarks | Asn1Kit_Encode       | Encode,CRL         | 4.236 μs |  0.4847 μs | 0.0266 μs |  1.00 |    0.00 | 0.3052 |      - |   2.82 KB |        1.00 |
| CertificateListBenchmarks | BouncyCastle_Encode  | Encode,CRL         | 4.655 μs |  0.3545 μs | 0.0194 μs |  1.10 |    0.01 | 0.4349 | 0.0038 |   4.01 KB |        1.42 |
|                           |                      |                    |          |            |           |       |         |        |        |           |             |
| CertificateBenchmarks     | Asn1Kit_Encode       | Encode,Certificate | 4.878 μs |  1.0589 μs | 0.0580 μs |  1.00 |    0.00 | 0.4463 |      - |   4.13 KB |        1.00 |
| CertificateBenchmarks     | BouncyCastle_Encode  | Encode,Certificate | 5.958 μs |  0.9556 μs | 0.0524 μs |  1.22 |    0.00 | 0.5074 | 0.0038 |   4.68 KB |        1.13 |

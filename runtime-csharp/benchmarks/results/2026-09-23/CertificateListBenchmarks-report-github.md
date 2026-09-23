```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]     : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2


```
| Method              | Categories | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------- |----------- |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Asn1Kit_Decode      | Decode,CRL | 2.634 μs | 0.0527 μs | 0.0585 μs |  1.00 |    0.00 | 0.2480 |      - |   2.31 KB |        1.00 |
| BouncyCastle_Decode | Decode,CRL | 7.325 μs | 0.1295 μs | 0.1211 μs |  2.78 |    0.09 | 1.1368 | 0.0076 |  10.45 KB |        4.52 |
|                     |            |          |           |           |       |         |        |        |           |             |
| Asn1Kit_Encode      | Encode,CRL | 2.089 μs | 0.0393 μs | 0.0678 μs |  1.00 |    0.00 | 0.3052 |      - |   2.82 KB |        1.00 |
| BouncyCastle_Encode | Encode,CRL | 2.169 μs | 0.0410 μs | 0.0488 μs |  1.05 |    0.04 | 0.4349 |      - |   4.01 KB |        1.42 |

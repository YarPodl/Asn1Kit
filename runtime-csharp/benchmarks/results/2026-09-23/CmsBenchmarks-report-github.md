```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]     : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2


```
| Method                          | Categories | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------------- |----------- |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Asn1Kit_Lazy_Decode             | Decode,CMS | 1.649 μs | 0.0330 μs | 0.0551 μs |  1.00 |    0.00 | 0.2270 |      - |    2.1 KB |        1.00 |
| Asn1Kit_Lazy_Materialize_Decode | Decode,CMS | 4.059 μs | 0.0679 μs | 0.0667 μs |  2.48 |    0.09 | 0.4349 |      - |   4.05 KB |        1.93 |
| Asn1Kit_Eager_Decode            | Decode,CMS | 3.710 μs | 0.0731 μs | 0.1116 μs |  2.25 |    0.12 | 0.4196 |      - |   3.88 KB |        1.85 |
| Bcl_Decode                      | Decode,CMS | 1.599 μs | 0.0315 μs | 0.0363 μs |  0.97 |    0.05 | 0.3738 | 0.0019 |   3.45 KB |        1.64 |
| Bcl_Materialize_Decode          | Decode,CMS | 8.676 μs | 0.1721 μs | 0.1912 μs |  5.28 |    0.20 | 0.5035 | 0.0916 |   4.68 KB |        2.23 |
| BouncyCastle_Decode             | Decode,CMS | 7.538 μs | 0.1397 μs | 0.2589 μs |  4.58 |    0.21 | 1.4648 | 0.0153 |  13.59 KB |        6.46 |
|                                 |            |          |           |           |       |         |        |        |           |             |
| Asn1Kit_Lazy_Encode             | Encode,CMS | 1.285 μs | 0.0254 μs | 0.0490 μs |  1.00 |    0.00 | 0.3204 |      - |   2.96 KB |        1.00 |
| Asn1Kit_Eager_Encode            | Encode,CMS | 2.764 μs | 0.0542 μs | 0.0533 μs |  2.13 |    0.11 | 0.4807 |      - |   4.45 KB |        1.50 |
| Bcl_Encode                      | Encode,CMS | 1.967 μs | 0.0382 μs | 0.0440 μs |  1.52 |    0.07 | 0.9613 | 0.0076 |   8.85 KB |        2.99 |
| BouncyCastle_Encode             | Encode,CMS | 2.647 μs | 0.0530 μs | 0.0981 μs |  2.06 |    0.10 | 0.5531 | 0.0038 |   5.11 KB |        1.73 |

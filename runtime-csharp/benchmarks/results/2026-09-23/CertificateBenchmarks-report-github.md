```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]     : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2


```
| Method              | Categories         | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------- |------------------- |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Asn1Kit_Decode      | Decode,Certificate | 3.352 μs | 0.0658 μs | 0.1252 μs |  1.00 |    0.00 | 0.3090 |      - |    2912 B |        1.00 |
| Bcl_Decode          | Decode,Certificate | 2.624 μs | 0.0524 μs | 0.0751 μs |  0.79 |    0.04 | 0.0343 | 0.0153 |     328 B |        0.11 |
| BouncyCastle_Decode | Decode,Certificate | 9.521 μs | 0.1780 μs | 0.2974 μs |  2.86 |    0.14 | 1.4038 | 0.0153 |   13280 B |        4.56 |
|                     |                    |          |           |           |       |         |        |        |           |             |
| Asn1Kit_Encode      | Encode,Certificate | 2.353 μs | 0.0417 μs | 0.0446 μs |  1.00 |    0.00 | 0.4463 |      - |    4232 B |        1.00 |
| BouncyCastle_Encode | Encode,Certificate | 2.679 μs | 0.0522 μs | 0.0732 μs |  1.14 |    0.04 | 0.5074 | 0.0038 |    4792 B |        1.13 |

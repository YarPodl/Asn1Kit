```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]     : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2


```
| Method         | Categories         | Mean     | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------- |------------------- |---------:|----------:|----------:|------:|-------:|----------:|------------:|
| Asn1Kit_Decode | Decode,Certificate | 4.841 μs | 0.0544 μs | 0.0482 μs |  1.00 | 0.1678 |   1.57 KB |        1.00 |
|                |                    |          |           |           |       |        |           |             |
| Asn1Kit_Encode | Encode,Certificate | 2.628 μs | 0.0499 μs | 0.0534 μs |  1.00 | 0.1831 |    1.7 KB |        1.00 |

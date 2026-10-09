```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]     : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2


```
| Method              | Categories | Mean     | Error     | StdDev    | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------- |----------- |---------:|----------:|----------:|------:|-------:|-------:|----------:|------------:|
| Asn1Kit_Lazy_Decode | Decode,CMS | 2.047 μs | 0.0401 μs | 0.1063 μs |  1.00 | 0.1163 |      - |   1.08 KB |        1.00 |
|                     |            |          |           |           |       |        |        |           |             |
| Asn1Kit_Lazy_Encode | Encode,CMS | 1.555 μs | 0.0311 μs | 0.0466 μs |  1.00 | 0.6294 | 0.0038 |   5.79 KB |        1.00 |

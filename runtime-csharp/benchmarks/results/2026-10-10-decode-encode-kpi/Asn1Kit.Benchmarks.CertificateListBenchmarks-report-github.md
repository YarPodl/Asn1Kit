```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]     : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2


```
| Method         | Categories | Mean     | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------- |----------- |---------:|----------:|----------:|------:|-------:|----------:|------------:|
| Asn1Kit_Decode | Decode,CRL | 3.799 μs | 0.0293 μs | 0.0245 μs |  1.00 | 0.1297 |    1288 B |        1.00 |
|                |            |          |           |           |       |        |           |             |
| Asn1Kit_Encode | Encode,CRL | 2.244 μs | 0.0203 μs | 0.0170 μs |  1.00 | 0.1030 |     976 B |        1.00 |

```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]     : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2

Categories=Decode,Certificate  

```
| Method         | Mean     | Error     | StdDev    | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|----------:|------:|-------:|-------:|----------:|------------:|
| Asn1Kit_Decode | 6.773 μs | 0.0472 μs | 0.0394 μs |  1.00 | 0.3128 | 0.0076 |   2.91 KB |        1.00 |

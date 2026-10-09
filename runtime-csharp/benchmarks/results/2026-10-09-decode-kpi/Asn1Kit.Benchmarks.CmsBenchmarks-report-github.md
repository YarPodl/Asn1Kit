```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]     : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2

Categories=Decode,CMS  

```
| Method              | Mean     | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------- |---------:|----------:|----------:|------:|-------:|----------:|------------:|
| Asn1Kit_Lazy_Decode | 2.639 μs | 0.0336 μs | 0.0281 μs |  1.00 | 0.1297 |   1.21 KB |        1.00 |

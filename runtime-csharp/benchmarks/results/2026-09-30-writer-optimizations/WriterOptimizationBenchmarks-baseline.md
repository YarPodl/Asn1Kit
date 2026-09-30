```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]   : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  ShortRun : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                   | Categories        | Mean        | Error        | StdDev      | Gen0   | Allocated |
|------------------------- |------------------ |------------:|-------------:|------------:|-------:|----------:|
| Sequence128              | Constructed,Long  |    112.1 ns |     15.49 ns |     0.85 ns |      - |         - |
| Sequence256              | Constructed,Long  |    110.9 ns |     43.84 ns |     2.40 ns |      - |         - |
| Sequence64K              | Constructed,Long  | 51,174.0 ns |  5,354.34 ns |   293.49 ns |      - |         - |
|                          |                   |             |              |             |        |           |
| ShortSiblingSequences    | Constructed,Short |  3,467.1 ns |  4,015.58 ns |   220.11 ns |      - |         - |
| DeepShortSequence        | Constructed,Short |    628.3 ns |     35.00 ns |     1.92 ns |      - |         - |
|                          |                   |             |              |             |        |           |
| CachedIntegerFactories   | Integer,Cached    | 76,019.0 ns | 24,454.22 ns | 1,340.42 ns | 3.4180 |   32608 B |
|                          |                   |             |              |             |        |           |
| UncachedIntegerFactories | Integer,Uncached  | 96,504.1 ns | 10,639.29 ns |   583.18 ns | 4.3335 |   40960 B |

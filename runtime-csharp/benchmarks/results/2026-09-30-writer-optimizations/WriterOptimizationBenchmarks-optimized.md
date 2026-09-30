```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.9445)
Unknown processor
.NET SDK 6.0.402
  [Host]   : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2
  ShortRun : .NET 6.0.10 (6.0.1022.47605), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                   | Categories        | Mean         | Error         | StdDev       | Gen0   | Allocated |
|------------------------- |------------------ |-------------:|--------------:|-------------:|-------:|----------:|
| Sequence128              | Constructed,Long  |     92.31 ns |     91.305 ns |     5.005 ns |      - |         - |
| Sequence256              | Constructed,Long  |     91.08 ns |      3.049 ns |     0.167 ns |      - |         - |
| Sequence64K              | Constructed,Long  |  6,277.00 ns | 30,859.133 ns | 1,691.493 ns |      - |         - |
|                          |                   |              |               |              |        |           |
| ShortSiblingSequences    | Constructed,Short |  2,350.69 ns |    124.773 ns |     6.839 ns |      - |         - |
| DeepShortSequence        | Constructed,Short |    349.97 ns |    478.651 ns |    26.236 ns |      - |         - |
|                          |                   |              |               |              |        |           |
| CachedIntegerFactories   | Integer,Cached    | 26,152.18 ns |  2,530.010 ns |   138.678 ns |      - |         - |
|                          |                   |              |               |              |        |           |
| UncachedIntegerFactories | Integer,Uncached  | 73,833.80 ns | 10,205.801 ns |   559.414 ns | 4.3335 |   40960 B |

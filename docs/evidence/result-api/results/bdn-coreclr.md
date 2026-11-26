```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26320.9877)
13th Gen Intel Core i9-13900K 3.00GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 11.0.100
  [Host]  : .NET 11.0.0 (11.0.0, 11.0.26.53018), X64 RyuJIT x86-64-v3
  CoreCLR : .NET 11.0.0 (11.0.0, 11.0.26.53018), X64 RyuJIT x86-64-v3

Job=CoreCLR  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
Toolchain=.NET 11.0  IterationCount=15  LaunchCount=2  
WarmupCount=5  

```
| Method                 | Categories | Mean       | Error     | StdDev    | Median     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |----------- |-----------:|----------:|----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| ClassError             | error      |  3.5195 ns | 0.1791 ns | 0.2680 ns |  3.4104 ns | 1.005 |    0.10 | 0.0017 |      32 B |        1.00 |
| StructError            | error      |  0.7638 ns | 0.0295 ns | 0.0423 ns |  0.7492 ns | 0.218 |    0.02 |      - |         - |        0.00 |
| UnionClassError        | error      |  3.6935 ns | 0.1860 ns | 0.2667 ns |  3.7047 ns | 1.055 |    0.11 | 0.0021 |      40 B |        1.25 |
| UnionStructError       | error      |  0.7508 ns | 0.0254 ns | 0.0364 ns |  0.7389 ns | 0.214 |    0.02 |      - |         - |        0.00 |
| UnionStructErrorSwitch | error      |  0.0092 ns | 0.0099 ns | 0.0133 ns |  0.0023 ns | 0.003 |    0.00 |      - |         - |        0.00 |
|                        |            |            |           |           |            |       |         |        |           |             |
| ClassSuccess           | success    |  0.0000 ns | 0.0000 ns | 0.0001 ns |  0.0000 ns |     ? |       ? |      - |         - |           ? |
| StructSuccess          | success    |  0.0033 ns | 0.0051 ns | 0.0073 ns |  0.0000 ns |     ? |       ? |      - |         - |           ? |
| UnionClassSuccess      | success    |  0.0122 ns | 0.0133 ns | 0.0199 ns |  0.0000 ns |     ? |       ? |      - |         - |           ? |
| UnionStructSuccess     | success    |  0.0024 ns | 0.0056 ns | 0.0079 ns |  0.0000 ns |     ? |       ? |      - |         - |           ? |
|                        |            |            |           |           |            |       |         |        |           |             |
| ClassAwait             | valuetask  | 11.0414 ns | 0.5355 ns | 0.7507 ns | 10.8031 ns |  1.00 |    0.09 | 0.0017 |      32 B |        1.00 |
| StructAwait            | valuetask  | 15.7308 ns | 0.2058 ns | 0.2951 ns | 15.6561 ns |  1.43 |    0.09 |      - |         - |        0.00 |
| UnionClassAwait        | valuetask  | 11.3851 ns | 0.1767 ns | 0.2589 ns | 11.3792 ns |  1.04 |    0.07 | 0.0021 |      40 B |        1.25 |
| UnionStructAwait       | valuetask  | 18.5999 ns | 2.1246 ns | 3.0471 ns | 18.6486 ns |  1.69 |    0.29 |      - |         - |        0.00 |

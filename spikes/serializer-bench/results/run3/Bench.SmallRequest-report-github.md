```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26320.9877)
13th Gen Intel Core i9-13900K 3.00GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 11.0.100
  [Host]    : .NET 11.0.0 (11.0.0, 11.0.26.53018), X64 RyuJIT x86-64-v3
  CoreCLR   : .NET 11.0.0 (11.0.0, 11.0.26.53018), X64 RyuJIT x86-64-v3
  NativeAOT : .NET 11.0.0, X64 NativeAOT x86-64-v3

PowerPlanMode=00000000-0000-0000-0000-000000000000  IterationCount=15  IterationTime=250ms  
LaunchCount=3  WarmupCount=6  

```
| Method                 | Job       | Runtime        | Categories                                | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------- |---------- |--------------- |------------------------------------------ |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| A_SerializeReusedCopy  | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 528.0 ns | 1.73 ns | 3.17 ns |  1.03 | 0.0212 |     400 B |        1.00 |
| A2_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 446.0 ns | 3.18 ns | 5.89 ns |  0.87 | 0.0161 |     336 B |        0.84 |
| B_SerializeReusedCopy  | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 510.5 ns | 1.90 ns | 3.61 ns |  1.00 | 0.0206 |     400 B |        1.00 |
|                        |           |                |                                           |          |         |         |       |        |           |             |
| A_SerializeReusedCopy  | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 692.7 ns | 3.38 ns | 6.43 ns |  1.02 | 0.0194 |     400 B |        1.00 |
| A2_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 604.2 ns | 4.84 ns | 8.85 ns |  0.89 | 0.0172 |     336 B |        0.84 |
| B_SerializeReusedCopy  | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 680.9 ns | 2.39 ns | 4.48 ns |  1.00 | 0.0191 |     400 B |        1.00 |
|                        |           |                |                                           |          |         |         |       |        |           |             |
| A_SerializeReused      | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 500.4 ns | 0.65 ns | 1.22 ns |  1.04 | 0.0020 |      64 B |        1.00 |
| A2_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 410.0 ns | 1.00 ns | 1.89 ns |  0.85 |      - |         - |        0.00 |
| B_SerializeReused      | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 482.7 ns | 0.98 ns | 1.85 ns |  1.00 | 0.0019 |      64 B |        1.00 |
|                        |           |                |                                           |          |         |         |       |        |           |             |
| A_SerializeReused      | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 659.6 ns | 1.34 ns | 2.52 ns |  1.02 | 0.0026 |      64 B |        1.00 |
| A2_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 561.1 ns | 3.78 ns | 7.10 ns |  0.87 |      - |         - |        0.00 |
| B_SerializeReused      | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 647.8 ns | 1.15 ns | 2.16 ns |  1.00 | 0.0026 |      64 B |        1.00 |

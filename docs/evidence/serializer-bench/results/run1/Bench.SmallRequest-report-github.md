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
| Method                | Job       | Runtime        | Categories                                | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------- |---------- |--------------- |------------------------------------------ |-----------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| A_Deserialize         | CoreCLR   | .NET 10.0      | 1 Deserialize                             |   801.4 ns |  5.08 ns |  9.29 ns |  0.57 |    0.01 | 0.0419 |      - |     816 B |        0.52 |
| B_Deserialize         | CoreCLR   | .NET 10.0      | 1 Deserialize                             | 1,398.6 ns | 12.46 ns | 23.70 ns |  1.00 |    0.02 | 0.0828 |      - |    1568 B |        1.00 |
|                       |           |                |                                           |            |          |          |       |         |        |        |           |             |
| A_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 1,020.1 ns |  5.58 ns | 10.35 ns |  0.48 |    0.01 | 0.0407 |      - |     816 B |        0.52 |
| B_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 2,139.5 ns | 13.97 ns | 26.58 ns |  1.00 |    0.02 | 0.0756 |      - |    1568 B |        1.00 |
|                       |           |                |                                           |            |          |          |       |         |        |        |           |             |
| A_Serialize           | CoreCLR   | .NET 10.0      | 2 Serialize, as shipped (byte[])          |   876.6 ns | 10.51 ns | 19.48 ns |  1.62 |    0.04 | 0.2750 | 0.0035 |    5224 B |       13.06 |
| B_Serialize           | CoreCLR   | .NET 10.0      | 2 Serialize, as shipped (byte[])          |   540.4 ns |  2.02 ns |  3.79 ns |  1.00 |    0.01 | 0.0196 |      - |     400 B |        1.00 |
|                       |           |                |                                           |            |          |          |       |         |        |        |           |             |
| A_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 1,066.0 ns | 15.56 ns | 29.23 ns |  1.50 |    0.04 | 0.2737 | 0.0042 |    5224 B |       13.06 |
| B_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          |   709.9 ns |  2.16 ns |  4.01 ns |  1.00 |    0.01 | 0.0197 |      - |     400 B |        1.00 |
|                       |           |                |                                           |            |          |          |       |         |        |        |           |             |
| A_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] |   538.4 ns |  1.92 ns |  3.36 ns |  1.03 |    0.02 | 0.0196 |      - |     400 B |        1.00 |
| B_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] |   522.8 ns |  4.86 ns |  9.25 ns |  1.00 |    0.03 | 0.0209 |      - |     400 B |        1.00 |
|                       |           |                |                                           |            |          |          |       |         |        |        |           |             |
| A_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] |   699.7 ns |  2.63 ns |  4.93 ns |  1.02 |    0.01 | 0.0196 |      - |     400 B |        1.00 |
| B_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] |   687.2 ns |  3.94 ns |  7.50 ns |  1.00 |    0.02 | 0.0193 |      - |     400 B |        1.00 |
|                       |           |                |                                           |            |          |          |       |         |        |        |           |             |
| A_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       |   502.0 ns |  0.88 ns |  1.62 ns |  1.04 |    0.01 | 0.0020 |      - |      64 B |        1.00 |
| B_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       |   482.6 ns |  1.71 ns |  3.09 ns |  1.00 |    0.01 | 0.0019 |      - |      64 B |        1.00 |
|                       |           |                |                                           |            |          |          |       |         |        |        |           |             |
| A_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       |   659.2 ns |  1.83 ns |  3.47 ns |  1.02 |    0.01 | 0.0026 |      - |      64 B |        1.00 |
| B_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       |   648.2 ns |  2.70 ns |  4.80 ns |  1.00 |    0.01 | 0.0026 |      - |      64 B |        1.00 |

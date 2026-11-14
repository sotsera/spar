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
| Method                | Job       | Runtime        | Categories                                | Mean       | Error    | StdDev   | Median     | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------- |---------- |--------------- |------------------------------------------ |-----------:|---------:|---------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| A_Deserialize         | CoreCLR   | .NET 10.0      | 1 Deserialize                             |   778.2 ns |  4.25 ns |  7.99 ns |   776.2 ns |  0.58 |    0.01 | 0.0404 |      - |     816 B |        0.52 |
| B_Deserialize         | CoreCLR   | .NET 10.0      | 1 Deserialize                             | 1,347.5 ns |  5.27 ns |  9.89 ns | 1,348.1 ns |  1.00 |    0.01 | 0.0805 |      - |    1568 B |        1.00 |
|                       |           |                |                                           |            |          |          |            |       |         |        |        |           |             |
| A_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             |   996.9 ns |  5.00 ns |  9.39 ns |   995.5 ns |  0.47 |    0.01 | 0.0398 |      - |     816 B |        0.52 |
| B_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 2,114.6 ns | 14.82 ns | 27.83 ns | 2,106.4 ns |  1.00 |    0.02 | 0.0787 |      - |    1568 B |        1.00 |
|                       |           |                |                                           |            |          |          |            |       |         |        |        |           |             |
| A_Serialize           | CoreCLR   | .NET 10.0      | 2 Serialize, as shipped (byte[])          |   823.0 ns |  9.17 ns | 16.99 ns |   818.9 ns |  1.55 |    0.03 | 0.2771 | 0.0033 |    5224 B |       13.06 |
| B_Serialize           | CoreCLR   | .NET 10.0      | 2 Serialize, as shipped (byte[])          |   531.3 ns |  2.53 ns |  4.50 ns |   530.8 ns |  1.00 |    0.01 | 0.0192 |      - |     400 B |        1.00 |
|                       |           |                |                                           |            |          |          |            |       |         |        |        |           |             |
| A_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          |   997.5 ns |  9.69 ns | 17.97 ns |   991.9 ns |  1.41 |    0.04 | 0.2749 | 0.0039 |    5224 B |       13.06 |
| B_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          |   709.4 ns |  7.87 ns | 14.59 ns |   702.9 ns |  1.00 |    0.03 | 0.0195 |      - |     400 B |        1.00 |
|                       |           |                |                                           |            |          |          |            |       |         |        |        |           |             |
| A_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] |   527.5 ns |  5.08 ns |  9.55 ns |   528.0 ns |  1.02 |    0.02 | 0.0213 |      - |     400 B |        1.00 |
| B_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] |   516.5 ns |  3.08 ns |  5.55 ns |   515.8 ns |  1.00 |    0.01 | 0.0210 |      - |     400 B |        1.00 |
|                       |           |                |                                           |            |          |          |            |       |         |        |        |           |             |
| A_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] |   684.7 ns |  2.86 ns |  5.29 ns |   684.7 ns |  1.01 |    0.02 | 0.0191 |      - |     400 B |        1.00 |
| B_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] |   675.3 ns |  4.73 ns |  8.76 ns |   674.7 ns |  1.00 |    0.02 | 0.0188 |      - |     400 B |        1.00 |
|                       |           |                |                                           |            |          |          |            |       |         |        |        |           |             |
| A_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       |   497.3 ns |  7.67 ns | 14.21 ns |   499.6 ns |  1.03 |    0.05 | 0.0020 |      - |      64 B |        1.00 |
| B_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       |   481.5 ns |  8.35 ns | 15.89 ns |   481.6 ns |  1.00 |    0.05 | 0.0020 |      - |      64 B |        1.00 |
|                       |           |                |                                           |            |          |          |            |       |         |        |        |           |             |
| A_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       |   654.8 ns |  1.78 ns |  3.34 ns |   655.3 ns |  1.02 |    0.01 | 0.0026 |      - |      64 B |        1.00 |
| B_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       |   639.9 ns |  2.56 ns |  4.81 ns |   640.9 ns |  1.00 |    0.01 | 0.0025 |      - |      64 B |        1.00 |

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
| Method                 | Job       | Runtime        | Categories                                | Count | Mean        | Error     | StdDev    | Median      | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|----------------------- |---------- |--------------- |------------------------------------------ |------ |------------:|----------:|----------:|------------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| **A_SerializeReusedCopy**  | **CoreCLR**   | **.NET 10.0**      | **3 Serialize, reused writer + exact byte[]** | **50**    |    **18.21 μs** |  **0.091 μs** |  **0.173 μs** |    **18.20 μs** |  **1.02** |    **0.02** |   **0.6458** |        **-** |        **-** |   **12648 B** |        **1.00** |
| A2_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 50    |    15.16 μs |  0.172 μs |  0.326 μs |    15.07 μs |  0.85 |    0.02 |   0.5542 |        - |        - |   10824 B |        0.86 |
| B_SerializeReusedCopy  | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 50    |    17.80 μs |  0.141 μs |  0.268 μs |    17.84 μs |  1.00 |    0.02 |   0.6370 |        - |        - |   12648 B |        1.00 |
|                        |           |                |                                           |       |             |           |           |             |       |         |          |          |          |           |             |
| A_SerializeReusedCopy  | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 50    |    24.73 μs |  0.097 μs |  0.185 μs |    24.70 μs |  1.03 |    0.01 |   0.5906 |        - |        - |   12648 B |        1.00 |
| A2_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 50    |    21.28 μs |  0.193 μs |  0.357 μs |    21.25 μs |  0.89 |    0.02 |   0.5034 |        - |        - |   10824 B |        0.86 |
| B_SerializeReusedCopy  | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 50    |    24.04 μs |  0.074 μs |  0.138 μs |    24.01 μs |  1.00 |    0.01 |   0.6700 |        - |        - |   12648 B |        1.00 |
|                        |           |                |                                           |       |             |           |           |             |       |         |          |          |          |           |             |
| **A_SerializeReusedCopy**  | **CoreCLR**   | **.NET 10.0**      | **3 Serialize, reused writer + exact byte[]** | **2000**  |   **939.15 μs** |  **9.415 μs** | **17.450 μs** |   **937.22 μs** |  **1.06** |    **0.03** |  **93.7500** |  **93.7500** |  **93.7500** |  **506865 B** |        **1.00** |
| A2_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 2000  |   795.81 μs |  6.740 μs | 12.823 μs |   794.19 μs |  0.90 |    0.03 |  93.7500 |  93.7500 |  93.7500 |  434092 B |        0.86 |
| B_SerializeReusedCopy  | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 2000  |   889.05 μs | 11.457 μs | 21.519 μs |   888.80 μs |  1.00 |    0.03 |  78.1250 |  78.1250 |  78.1250 |  506685 B |        1.00 |
|                        |           |                |                                           |       |             |           |           |             |       |         |          |          |          |           |             |
| A_SerializeReusedCopy  | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 2000  | 1,138.46 μs |  6.530 μs | 11.940 μs | 1,138.41 μs |  1.01 |    0.02 | 102.6786 | 102.6786 | 102.6786 |  506942 B |        1.00 |
| A2_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 2000  |   996.70 μs |  6.877 μs | 13.083 μs |   997.72 μs |  0.88 |    0.01 | 101.5625 | 101.5625 | 101.5625 |  434139 B |        0.86 |
| B_SerializeReusedCopy  | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 2000  | 1,128.74 μs |  6.370 μs | 12.120 μs | 1,129.07 μs |  1.00 |    0.02 | 102.6786 | 102.6786 | 102.6786 |  506896 B |        1.00 |
|                        |           |                |                                           |       |             |           |           |             |       |         |          |          |          |           |             |
| **A_SerializeReused**      | **CoreCLR**   | **.NET 10.0**      | **4 Serialize, reused writer, no copy**       | **50**    |    **16.79 μs** |  **0.018 μs** |  **0.032 μs** |    **16.80 μs** |  **1.02** |    **0.01** |   **0.0675** |        **-** |        **-** |    **1824 B** |        **1.00** |
| A2_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 50    |    13.92 μs |  0.021 μs |  0.038 μs |    13.93 μs |  0.85 |    0.01 |        - |        - |        - |         - |        0.00 |
| B_SerializeReused      | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 50    |    16.48 μs |  0.085 μs |  0.161 μs |    16.55 μs |  1.00 |    0.01 |   0.0650 |        - |        - |    1824 B |        1.00 |
|                        |           |                |                                           |       |             |           |           |             |       |         |          |          |          |           |             |
| A_SerializeReused      | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 50    |    23.52 μs |  0.217 μs |  0.407 μs |    23.57 μs |  1.02 |    0.02 |   0.0948 |        - |        - |    1824 B |        1.00 |
| A2_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 50    |    19.90 μs |  0.044 μs |  0.081 μs |    19.89 μs |  0.86 |    0.01 |        - |        - |        - |         - |        0.00 |
| B_SerializeReused      | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 50    |    23.13 μs |  0.122 μs |  0.233 μs |    23.08 μs |  1.00 |    0.01 |   0.0919 |        - |        - |    1824 B |        1.00 |
|                        |           |                |                                           |       |             |           |           |             |       |         |          |          |          |           |             |
| **A_SerializeReused**      | **CoreCLR**   | **.NET 10.0**      | **4 Serialize, reused writer, no copy**       | **2000**  |   **780.28 μs** |  **6.165 μs** | **11.580 μs** |   **782.12 μs** |  **1.03** |    **0.02** |   **3.1250** |        **-** |        **-** |   **72544 B** |        **1.00** |
| A2_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 2000  |   659.89 μs |  1.943 μs |  3.453 μs |   659.16 μs |  0.87 |    0.01 |        - |        - |        - |         - |        0.00 |
| B_SerializeReused      | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 2000  |   761.06 μs |  4.520 μs |  8.264 μs |   761.56 μs |  1.00 |    0.02 |        - |        - |        - |   72544 B |        1.00 |
|                        |           |                |                                           |       |             |           |           |             |       |         |          |          |          |           |             |
| A_SerializeReused      | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 2000  |   998.98 μs |  2.535 μs |  4.762 μs |   997.67 μs |  1.02 |    0.01 |        - |        - |        - |   72549 B |       1.000 |
| A2_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 2000  |   873.93 μs |  4.776 μs |  9.086 μs |   869.01 μs |  0.89 |    0.01 |        - |        - |        - |       5 B |       0.000 |
| B_SerializeReused      | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 2000  |   982.83 μs |  2.678 μs |  5.030 μs |   983.03 μs |  1.00 |    0.01 |        - |        - |        - |   72549 B |       1.000 |

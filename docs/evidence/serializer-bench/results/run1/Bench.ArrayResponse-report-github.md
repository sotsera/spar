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
| Method                | Job       | Runtime        | Categories                                | Count | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|---------------------- |---------- |--------------- |------------------------------------------ |------ |------------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|-----------:|------------:|
| **A_Deserialize**         | **CoreCLR**   | **.NET 10.0**      | **1 Deserialize**                             | **50**    |    **28.71 μs** |  **0.146 μs** |  **0.271 μs** |  **0.57** |    **0.03** |   **1.4130** |        **-** |        **-** |   **27.52 KB** |        **0.73** |
| B_Deserialize         | CoreCLR   | .NET 10.0      | 1 Deserialize                             | 50    |    50.26 μs |  1.630 μs |  3.022 μs |  1.00 |    0.08 |   1.1161 |        - |        - |   37.45 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 50    |    36.76 μs |  0.148 μs |  0.281 μs |  0.52 |    0.01 |   1.4775 |        - |        - |   27.52 KB |        0.73 |
| B_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 50    |    70.16 μs |  0.273 μs |  0.506 μs |  1.00 |    0.01 |   1.9707 |        - |        - |   37.45 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_Deserialize**         | **CoreCLR**   | **.NET 10.0**      | **1 Deserialize**                             | **2000**  | **1,243.44 μs** | **11.145 μs** | **20.657 μs** |  **0.62** |    **0.01** |  **46.8750** |  **31.2500** |        **-** | **1089.69 KB** |        **0.74** |
| B_Deserialize         | CoreCLR   | .NET 10.0      | 1 Deserialize                             | 2000  | 1,994.06 μs | 18.224 μs | 34.229 μs |  1.00 |    0.02 |  62.5000 |  31.2500 |        - | 1468.05 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 2000  | 1,545.83 μs |  9.192 μs | 17.489 μs |  0.54 |    0.01 |  56.8182 |  45.4545 |        - |  1089.7 KB |        0.74 |
| B_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 2000  | 2,875.93 μs | 11.700 μs | 21.393 μs |  1.00 |    0.01 |  72.9167 |  31.2500 |        - | 1468.07 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_Serialize**           | **CoreCLR**   | **.NET 10.0**      | **2 Serialize, as shipped (byte[])**          | **50**    |    **22.33 μs** |  **0.535 μs** |  **1.004 μs** |  **1.26** |    **0.06** |   **2.3082** |   **0.1776** |        **-** |   **42.61 KB** |        **3.45** |
| B_Serialize           | CoreCLR   | .NET 10.0      | 2 Serialize, as shipped (byte[])          | 50    |    17.68 μs |  0.174 μs |  0.326 μs |  1.00 |    0.03 |   0.6370 |        - |        - |   12.35 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 50    |    29.36 μs |  0.262 μs |  0.491 μs |  1.21 |    0.02 |   2.2837 |   0.1202 |        - |   42.61 KB |        3.45 |
| B_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 50    |    24.32 μs |  0.079 μs |  0.146 μs |  1.00 |    0.01 |   0.5796 |        - |        - |   12.35 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_Serialize**           | **CoreCLR**   | **.NET 10.0**      | **2 Serialize, as shipped (byte[])**          | **2000**  | **1,098.69 μs** |  **7.208 μs** | **13.538 μs** |  **1.20** |    **0.04** | **352.6786** | **343.7500** | **343.7500** | **1580.36 KB** |        **3.19** |
| B_Serialize           | CoreCLR   | .NET 10.0      | 2 Serialize, as shipped (byte[])          | 2000  |   916.56 μs | 16.528 μs | 30.636 μs |  1.00 |    0.05 |  78.1250 |  78.1250 |  78.1250 |  494.89 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 2000  | 1,377.08 μs | 22.895 μs | 43.003 μs |  1.21 |    0.04 | 333.3333 | 322.9167 | 322.9167 | 1580.37 KB |        3.19 |
| B_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 2000  | 1,138.95 μs |  7.005 μs | 13.157 μs |  1.00 |    0.02 | 102.6786 | 102.6786 | 102.6786 |  495.09 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_SerializeReusedCopy** | **CoreCLR**   | **.NET 10.0**      | **3 Serialize, reused writer + exact byte[]** | **50**    |    **18.27 μs** |  **0.077 μs** |  **0.143 μs** |  **1.03** |    **0.01** |   **0.6510** |        **-** |        **-** |   **12.35 KB** |        **1.00** |
| B_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 50    |    17.70 μs |  0.113 μs |  0.215 μs |  1.00 |    0.02 |   0.6488 |        - |        - |   12.35 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 50    |    24.85 μs |  0.083 μs |  0.152 μs |  1.02 |    0.01 |   0.5896 |        - |        - |   12.35 KB |        1.00 |
| B_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 50    |    24.46 μs |  0.099 μs |  0.185 μs |  1.00 |    0.01 |   0.5878 |        - |        - |   12.35 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_SerializeReusedCopy** | **CoreCLR**   | **.NET 10.0**      | **3 Serialize, reused writer + exact byte[]** | **2000**  |   **929.55 μs** | **12.216 μs** | **23.241 μs** |  **1.04** |    **0.04** | **106.6176** | **102.9412** | **102.9412** |  **495.02 KB** |        **1.00** |
| B_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 2000  |   892.40 μs | 16.155 μs | 28.715 μs |  1.00 |    0.04 |  78.1250 |  78.1250 |  78.1250 |  494.81 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 2000  | 1,140.73 μs |  6.772 μs | 12.720 μs |  1.01 |    0.02 | 102.6786 | 102.6786 | 102.6786 |  495.03 KB |        1.00 |
| B_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 2000  | 1,129.93 μs |  7.390 μs | 13.513 μs |  1.00 |    0.02 | 102.6786 | 102.6786 | 102.6786 |  495.09 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_SerializeReused**     | **CoreCLR**   | **.NET 10.0**      | **4 Serialize, reused writer, no copy**       | **50**    |    **16.92 μs** |  **0.050 μs** |  **0.092 μs** |  **1.03** |    **0.02** |   **0.0677** |        **-** |        **-** |    **1.78 KB** |        **1.00** |
| B_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 50    |    16.42 μs |  0.143 μs |  0.264 μs |  1.00 |    0.02 |   0.0652 |        - |        - |    1.78 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 50    |    23.57 μs |  0.065 μs |  0.121 μs |  1.02 |    0.01 |   0.0936 |        - |        - |    1.78 KB |        1.00 |
| B_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 50    |    23.20 μs |  0.131 μs |  0.240 μs |  1.00 |    0.01 |   0.0940 |        - |        - |    1.78 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_SerializeReused**     | **CoreCLR**   | **.NET 10.0**      | **4 Serialize, reused writer, no copy**       | **2000**  |   **789.26 μs** |  **2.464 μs** |  **4.567 μs** |  **1.02** |    **0.02** |   **3.1250** |        **-** |        **-** |   **70.84 KB** |        **1.00** |
| B_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 2000  |   771.37 μs | 10.174 μs | 18.603 μs |  1.00 |    0.03 |        - |        - |        - |   70.84 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 2000  |   996.31 μs |  2.233 μs |  4.195 μs |  1.02 |    0.01 |        - |        - |        - |   70.85 KB |        1.00 |
| B_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 2000  |   978.26 μs |  3.903 μs |  7.330 μs |  1.00 |    0.01 |        - |        - |        - |   70.85 KB |        1.00 |

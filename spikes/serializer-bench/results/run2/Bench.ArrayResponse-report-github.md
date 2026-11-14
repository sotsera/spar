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
| **A_Deserialize**         | **CoreCLR**   | **.NET 10.0**      | **1 Deserialize**                             | **50**    |    **28.88 μs** |  **0.224 μs** |  **0.425 μs** |  **0.59** |    **0.02** |   **1.3915** |        **-** |        **-** |   **27.52 KB** |        **0.73** |
| B_Deserialize         | CoreCLR   | .NET 10.0      | 1 Deserialize                             | 50    |    49.00 μs |  1.065 μs |  2.026 μs |  1.00 |    0.06 |   1.1364 |        - |        - |   37.45 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 50    |    36.08 μs |  0.194 μs |  0.370 μs |  0.52 |    0.01 |   1.4335 |        - |        - |   27.52 KB |        0.73 |
| B_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 50    |    69.43 μs |  0.308 μs |  0.578 μs |  1.00 |    0.01 |   1.9358 |        - |        - |   37.45 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_Deserialize**         | **CoreCLR**   | **.NET 10.0**      | **1 Deserialize**                             | **2000**  | **1,205.07 μs** |  **8.254 μs** | **15.504 μs** |  **0.62** |    **0.01** |  **46.8750** |  **31.2500** |        **-** | **1089.69 KB** |        **0.74** |
| B_Deserialize         | CoreCLR   | .NET 10.0      | 1 Deserialize                             | 2000  | 1,957.36 μs | 16.455 μs | 31.308 μs |  1.00 |    0.02 |  62.5000 |  31.2500 |        - | 1468.05 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 2000  | 1,506.93 μs |  9.110 μs | 17.112 μs |  0.53 |    0.01 |  56.8182 |  45.4545 |        - |  1089.7 KB |        0.74 |
| B_Deserialize         | NativeAOT | NativeAOT 10.0 | 1 Deserialize                             | 2000  | 2,835.16 μs | 25.970 μs | 49.410 μs |  1.00 |    0.03 |  72.9167 |  31.2500 |        - | 1468.07 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_Serialize**           | **CoreCLR**   | **.NET 10.0**      | **2 Serialize, as shipped (byte[])**          | **50**    |    **21.55 μs** |  **0.154 μs** |  **0.281 μs** |  **1.26** |    **0.02** |   **2.3085** |   **0.1710** |        **-** |   **42.61 KB** |        **3.45** |
| B_Serialize           | CoreCLR   | .NET 10.0      | 2 Serialize, as shipped (byte[])          | 50    |    17.15 μs |  0.070 μs |  0.129 μs |  1.00 |    0.01 |   0.6107 |        - |        - |   12.35 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 50    |    28.22 μs |  0.157 μs |  0.284 μs |  1.17 |    0.02 |   2.2196 |   0.1168 |        - |   42.61 KB |        3.45 |
| B_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 50    |    24.08 μs |  0.155 μs |  0.292 μs |  1.00 |    0.02 |   0.6679 |        - |        - |   12.35 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_Serialize**           | **CoreCLR**   | **.NET 10.0**      | **2 Serialize, as shipped (byte[])**          | **2000**  | **1,129.42 μs** | **18.824 μs** | **35.356 μs** |  **1.26** |    **0.05** | **348.2143** | **339.2857** | **339.2857** | **1580.14 KB** |        **3.19** |
| B_Serialize           | CoreCLR   | .NET 10.0      | 2 Serialize, as shipped (byte[])          | 2000  |   900.49 μs | 12.514 μs | 23.505 μs |  1.00 |    0.04 |  78.1250 |  78.1250 |  78.1250 |  494.87 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 2000  | 1,382.19 μs | 24.498 μs | 46.610 μs |  1.22 |    0.05 | 296.8750 | 286.4583 | 286.4583 | 1580.36 KB |        3.19 |
| B_Serialize           | NativeAOT | NativeAOT 10.0 | 2 Serialize, as shipped (byte[])          | 2000  | 1,133.20 μs | 13.597 μs | 25.870 μs |  1.00 |    0.03 | 102.6786 | 102.6786 | 102.6786 |  495.13 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_SerializeReusedCopy** | **CoreCLR**   | **.NET 10.0**      | **3 Serialize, reused writer + exact byte[]** | **50**    |    **17.94 μs** |  **0.070 μs** |  **0.131 μs** |  **1.04** |    **0.01** |   **0.6181** |        **-** |        **-** |   **12.35 KB** |        **1.00** |
| B_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 50    |    17.32 μs |  0.068 μs |  0.124 μs |  1.00 |    0.01 |   0.6236 |        - |        - |   12.35 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 50    |    24.57 μs |  0.104 μs |  0.199 μs |  1.02 |    0.01 |   0.5896 |        - |        - |   12.35 KB |        1.00 |
| B_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 50    |    24.04 μs |  0.088 μs |  0.165 μs |  1.00 |    0.01 |   0.5796 |        - |        - |   12.35 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_SerializeReusedCopy** | **CoreCLR**   | **.NET 10.0**      | **3 Serialize, reused writer + exact byte[]** | **2000**  |   **924.32 μs** |  **8.779 μs** | **16.489 μs** |  **1.02** |    **0.04** | **106.6176** | **102.9412** | **102.9412** |  **494.95 KB** |        **1.00** |
| B_SerializeReusedCopy | CoreCLR   | .NET 10.0      | 3 Serialize, reused writer + exact byte[] | 2000  |   904.70 μs | 17.451 μs | 32.777 μs |  1.00 |    0.05 |  78.1250 |  78.1250 |  78.1250 |  494.97 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 2000  | 1,135.97 μs |  8.699 μs | 16.550 μs |  1.02 |    0.02 |  98.2143 |  98.2143 |  98.2143 |  495.08 KB |        1.00 |
| B_SerializeReusedCopy | NativeAOT | NativeAOT 10.0 | 3 Serialize, reused writer + exact byte[] | 2000  | 1,115.26 μs |  7.423 μs | 13.943 μs |  1.00 |    0.02 |  98.2143 |  98.2143 |  98.2143 |  495.07 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_SerializeReused**     | **CoreCLR**   | **.NET 10.0**      | **4 Serialize, reused writer, no copy**       | **50**    |    **16.88 μs** |  **0.038 μs** |  **0.070 μs** |  **1.04** |    **0.01** |   **0.0676** |        **-** |        **-** |    **1.78 KB** |        **1.00** |
| B_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 50    |    16.21 μs |  0.070 μs |  0.129 μs |  1.00 |    0.01 |   0.0651 |        - |        - |    1.78 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 50    |    23.43 μs |  0.049 μs |  0.091 μs |  1.01 |    0.01 |   0.0930 |        - |        - |    1.78 KB |        1.00 |
| B_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 50    |    23.11 μs |  0.109 μs |  0.205 μs |  1.00 |    0.01 |   0.0937 |        - |        - |    1.78 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| **A_SerializeReused**     | **CoreCLR**   | **.NET 10.0**      | **4 Serialize, reused writer, no copy**       | **2000**  |   **784.41 μs** |  **1.769 μs** |  **3.279 μs** |  **1.02** |    **0.02** |   **3.1250** |        **-** |        **-** |   **70.84 KB** |        **1.00** |
| B_SerializeReused     | CoreCLR   | .NET 10.0      | 4 Serialize, reused writer, no copy       | 2000  |   768.56 μs |  7.464 μs | 13.267 μs |  1.00 |    0.02 |        - |        - |        - |   70.84 KB |        1.00 |
|                       |           |                |                                           |       |             |           |           |       |         |          |          |          |            |             |
| A_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 2000  |   991.12 μs |  1.969 μs |  3.747 μs |  1.02 |    0.01 |        - |        - |        - |   70.85 KB |        1.00 |
| B_SerializeReused     | NativeAOT | NativeAOT 10.0 | 4 Serialize, reused writer, no copy       | 2000  |   975.18 μs |  6.767 μs | 12.711 μs |  1.00 |    0.02 |        - |        - |        - |   70.84 KB |        1.00 |

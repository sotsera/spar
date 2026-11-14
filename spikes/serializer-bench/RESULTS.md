# Serializer benchmark: generated `Utf8Json*` code (A) vs System.Text.Json context (B)

**Question.** Which wire serializer should Spar use, judged by speed first and size second?

- **(A)** The D1 generator's emitted `Utf8JsonReader`/`Utf8JsonWriter` code, copied from `spikes/d1-pipeline`.
- **(B)** System.Text.Json with an app-authored source-generated `JsonSerializerContext`. It uses the D1 baseline options: `JsonSerializerDefaults.Web`, `RespectNullableAnnotations`, `RespectRequiredConstructorParameters`.

**Answer.** **A.** Deserialization is 1.6–2.1× faster with 26–48% fewer allocations. With a reused writer, serialization is at parity as emitted, and 10–17% faster than B with a small emitter change. The executable is 1,120,256 bytes smaller. As the D1 spike ships it, though, A *serializes* 1.2–1.6× slower and allocates 3–13× more. The whole cause is `SparJson.Write`, which creates an `ArrayBufferWriter` and a `Utf8JsonWriter` on every call. M1 must fix that (see [Recommendation](#recommendation)).

## Environment

| Item | Value |
|---|---|
| CPU | 13th Gen Intel Core i9-13900K, 24 cores / 32 logical, AVX2 (no AVX-512); BDN reports x86-64-v3 |
| OS | Windows 11 Pro 10.0.26320 |
| SDK / runtime | .NET SDK 11.0.100, runtime 11.0.0 (11.0.26.53018), `net11.0`, win-x64 |
| BenchmarkDotNet | 0.15.8 (latest stable; 0.16 is preview only) |
| Power plan | Balanced, left as found. BDN switches to High performance by default. The first smoke run did this, then reverted it. All later runs use `DontEnforcePowerPlan()`, so BDN changes no system setting. |
| Other load | Another agent was working in the repo: 5 MSBuild nodes, a VBCSCompiler and several `dotnet`/`node` processes were alive. Total CPU, sampled every 15 s during BDN runs 1 and 2, averaged 6–10%, peaked at 26%, and includes the benchmark itself (one core ≈ 3%). Background load was about 1–2 logical cores. It was not possible to stop the other agent. |

## Method

### Code under test

Everything is under `spikes/serializer-bench/`. Nothing in `spikes/d1-pipeline` was built or modified.

| Path | Contents |
|---|---|
| `Bench.Generator/` | Copy of the D1 generator (`ContractReader`, `Model`, `EquatableArray`, `CSharpEmitter`). `EmitSerializers`, `EmitReader`, `EmitWriter`, `ReadValue` and `WriteValue` are unchanged apart from the namespace. The dispatch binding and catalog are replaced by a `{Contract}Codec` class whose methods contain the exact expressions the binding used: `Parse…` = `SparJson.Begin` + `Read…`; `Serialize…` = `SparJson.Write(value, static (writer, v) => …)`. It adds `Write…(Utf8JsonWriter, …)` for the reused-writer variant and `WriteTuned…`, the experiment described below. |
| `Bench.Runtime/` | Copy of `SparJson` and the attributes, unchanged apart from the namespace. |
| `Shared/` | `Note` (the D1 DTO: `string`, `IReadOnlyList<string>`, nested `Note?`, `double`, `bool`, `long?`). Also `BenchContract` (commands `Save(Note) → Note` and `SaveMany(Note[]) → Note[]`), `BenchJsonContext` (B, D1 options) and `Workload`. |
| `Bench/` | BenchmarkDotNet suite and the `verify` correctness check. |
| `Bench.Manual/` | Stopwatch cross-check harness, built with the repo's shipping settings. |
| `Size.None/`, `Size.A/`, `Size.B/` | Minimal NativeAOT apps for the size comparison. |

### Workloads

`Workload` is deterministic (seeded per record). Every tenth text has a quote and a non-ASCII character, so it needs escaping. Tags draw from a pool that includes `café`. A third of the records have a nested parent. Scores have 2 or 15 decimal places, and a quarter of the versions are null. Both serializers read the same JSON bytes and write the same objects.

| Workload | Shape | JSON size |
|---|---|---:|
| small request | one `Note`, every property set, nested parent | 310 B |
| medium response | `Note[50]` | 10,794 B |
| large response | `Note[2000]` | 433,975 B |

### Variants

| Category | A | B |
|---|---|---|
| 1 Deserialize (from `ReadOnlySpan<byte>`) | `SparJson.Begin` + generated `ReadNote` / `ReadArray` | `JsonSerializer.Deserialize(span, ctx.Note / NoteArray)` |
| 2 Serialize, as shipped (`byte[]`) | `SparJson.Write`: new `ArrayBufferWriter` + new `Utf8JsonWriter` + `ToArray` | `JsonSerializer.SerializeToUtf8Bytes` (internally a cached writer and pooled buffer, then one exact `byte[]`) |
| 3 Serialize, reused writer + exact `byte[]` | one `ArrayBufferWriter` and `Utf8JsonWriter` reused (`Reset`), `SkipValidation = true`, then `WrittenSpan.ToArray()` | same writer, `JsonSerializer.Serialize(writer, value, typeInfo)`, same copy |
| 4 Serialize, reused writer, no copy | as 3 without the final copy (the floor) | as 3 without the copy |
| A2 (experiment, in 3 and 4) | `WriteTuned…`: property names pre-encoded once as `JsonEncodedText` and written with the combined overloads (`WriteString(name, value)`, `WriteNumber`, `WriteBoolean`, `WriteNull`, `WriteStartArray(name)`), and lists written with an indexed `for` loop instead of `foreach` over `IReadOnlyList<T>` | — |

Category 3 is the fair comparison for the current dispatch shape (`ValueTask<byte[]>`): it gives each serializer the same buffer strategy B already uses internally.

### Harnesses

**BenchmarkDotNet 0.15.8** with `[MemoryDiagnoser]`, B as the per-category baseline (Ratio = A/B), and two jobs with identical settings: 3 launches (separate processes) × (6 warm-up + 15 measured iterations of 250 ms).

- **CoreCLR job:** Release, RyuJIT, tiered compilation defaults.
- **NativeAOT job:** BDN's `NativeAotToolchain`, `net11.0`, win-x64.

BDN 0.15.8 has no .NET 11 monikers, so three workarounds were needed:

1. A custom `net11.0` runtime fails its SDK validator (`GetRuntimeVersion not implemented for NotRecognized`). The jobs therefore carry the `.NET 10.0` / `NativeAOT 10.0` runtime *labels* and use toolchains that build `net11.0`. Every benchmark process reports `.NET 11.0.0 (11.0.26.53018)` in its environment block, so the label is cosmetic.
2. The NativeAOT generator throws `Invalid TFM: 'net11.0'` when it infers the instruction set. The job sets `IlcInstructionSet=native` explicitly. On this machine that resolves to x86-64-v3 (AVX2), the same instruction set the JIT uses.
3. BDN's generated NativeAOT host project sets `IlcOptimizationPreference=Speed`, `TreatWarningsAsErrors=False` and `ImportDirectoryBuildProps=false`. **So the BDN NativeAOT numbers are not built with the shipping settings.** The `Bench` project itself still compiles under the repo's props with warnings as errors.

**Manual cross-check harness** (`Bench.Manual`), because of point 3. It is published with the repo's own settings: warnings as errors, `IsAotCompatible`, default ILC options (Blended, default instruction set). That build produced no warnings. Per case: 1 s warm-up that also calibrates operations to about 100 ms per sample, then 15 rounds. Each round takes one sample of *every* case in turn, so background drift spreads across A and B. It reports the median, min, max and `GC.GetAllocatedBytesForCurrentThread` delta per operation. The same binary runs on CoreCLR (`dotnet Bench.Manual.dll`) and as the published NativeAOT exe.

### Runs

| Run | What | Notes |
|---|---|---|
| BDN 1 | all 4 categories × 3 workloads × 2 jobs (48 benchmarks) | `--keepFiles`, to inspect the generated projects |
| BDN 2 | identical repeat | |
| Manual 1–2 | CoreCLR and NativeAOT, 15 rounds each, run twice | before A2 existed |
| Manual 3–4 | same, with A2 added | |
| BDN 3 | categories 3 and 4 only, with A2 | |

An earlier manual run labelled both runtimes `NativeAOT`: `PublishAot` sets `IsDynamicCodeSupported=false` in the CoreCLR runtimeconfig too, so runtime detection failed. It was discarded, and the harness now takes the runtime name as an argument.

## Correctness check (`Bench.exe verify`)

Observed on CoreCLR:

| Workload | Bytes | A and B serialize byte-identical | A read == source | B read == source | Cross round-trip identical |
|---|---:|---|---|---|---|
| small | 310 | yes | yes | yes | yes |
| array[50] | 10,794 | yes | yes | yes | yes |
| array[2000] | 433,975 | yes | yes | yes | yes |

`==` is a deep comparison: text, tag sequence, parent recursively, the score's bit pattern, pinned and version. The benchmark `GlobalSetup` also asserts byte equality of A, B and A2 before each run. The Size apps print the same result (155) for A and B under NativeAOT.

**Differences on non-workload input** (single `Note`; "accepted" lists what was read):

| Input | A | B |
|---|---|---|
| all properties present | accepted, `Tags` is `string[]` | accepted, `Tags` is `List<string>` |
| nullable `version` omitted | **accepted** (null) | **rejected**: `RespectRequiredConstructorParameters` makes every constructor parameter required, nullable ones included |
| nullable `parent` and `version` omitted | **accepted** | **rejected** (same reason) |
| required `text` omitted | rejected, `JsonException` | rejected, `JsonException` |
| `text: null` / `tags: null` | rejected, `JsonException` | rejected, `JsonException` |
| `tags: [null]` | **rejected** (`Expected a string`) | **accepted**: `[null]` inside `IReadOnlyList<string>` (nullable annotations are not enforced on elements) |
| `pinned: null` | rejected, **`InvalidOperationException`** | rejected, `JsonException` |
| unknown property | skipped | skipped |
| PascalCase `"Text"` | **rejected** (case-sensitive, then `text` missing) | **accepted**: Web defaults match case-insensitively |
| `score: "2.5"` | **rejected**, `InvalidOperationException` | **accepted**: Web defaults allow numbers in strings |
| `version: 1.5` / above `long.MaxValue` | rejected, **`FormatException`** | rejected, `JsonException` |
| duplicate `text` | last wins | last wins |
| escaped name `"text"` | accepted (`ValueTextEquals` unescapes) | accepted |
| trailing comma / comment / truncated / empty | rejected, `JsonReaderException` (a `JsonException` subtype) | rejected, `JsonException` |
| trailing content after the object (`{…} 42`) | **accepted**: the generated reader stops at the object's end | **rejected** |
| top-level `null` | rejected | **accepted**, returns null; the spike's binding then applies `!` |

These are wire-contract decisions, not performance factors. Two are bugs A must fix in M1: trailing content is accepted, and token-type and number errors surface as `InvalidOperationException`/`FormatException` instead of `JsonException`. The fixes are one `reader.Read()` end check per payload and a wrap or `TryGet…` call, both negligible in cost. Whether `"Text"` and `"2.5"` should be rejected is a contract choice. A's stricter behaviour matches the decision log ("exact wire rules").

## Results: speed and allocations

Times are BDN means. **Ratio = A/B; below 1 means A is faster.** "run 1 / run 2" are two complete, independent BDN runs. BDN's 99.9% error margin was ≤ 3.3% of the mean in every cell, and usually under 1%.

### Small request (one `Note`, 310 B)

| Category | Runtime | A (ns) run 1 / 2 | B (ns) run 1 / 2 | A/B run 1 / 2 | Alloc A | Alloc B |
|---|---|---:|---:|---:|---:|---:|
| Deserialize | CoreCLR | 801 / 778 | 1,399 / 1,348 | **0.57 / 0.58** | 816 B | 1,568 B |
| Deserialize | NativeAOT | 1,020 / 997 | 2,140 / 2,115 | **0.48 / 0.47** | 816 B | 1,568 B |
| Serialize, as shipped | CoreCLR | 877 / 823 | 540 / 531 | **1.62 / 1.55** | 5,224 B | 400 B |
| Serialize, as shipped | NativeAOT | 1,066 / 998 | 710 / 709 | **1.50 / 1.41** | 5,224 B | 400 B |
| Serialize, reused + copy | CoreCLR | 538 / 528 | 523 / 517 | 1.03 / 1.02 | 400 B | 400 B |
| Serialize, reused + copy | NativeAOT | 700 / 685 | 687 / 675 | 1.02 / 1.01 | 400 B | 400 B |
| Serialize, reused, no copy | CoreCLR | 502 / 497 | 483 / 482 | 1.04 / 1.03 | 64 B | 64 B |
| Serialize, reused, no copy | NativeAOT | 659 / 655 | 648 / 640 | 1.02 / 1.02 | 64 B | 64 B |

### Medium response (`Note[50]`, 10.8 KB)

| Category | Runtime | A (µs) run 1 / 2 | B (µs) run 1 / 2 | A/B run 1 / 2 | Alloc A | Alloc B |
|---|---|---:|---:|---:|---:|---:|
| Deserialize | CoreCLR | 28.7 / 28.9 | 50.3 / 49.0 | **0.57 / 0.59** | 27.5 KB | 37.5 KB |
| Deserialize | NativeAOT | 36.8 / 36.1 | 70.2 / 69.4 | **0.52 / 0.52** | 27.5 KB | 37.5 KB |
| Serialize, as shipped | CoreCLR | 22.3 / 21.6 | 17.7 / 17.2 | **1.26 / 1.26** | 42.6 KB | 12.4 KB |
| Serialize, as shipped | NativeAOT | 29.4 / 28.2 | 24.3 / 24.1 | **1.21 / 1.17** | 42.6 KB | 12.4 KB |
| Serialize, reused + copy | CoreCLR | 18.3 / 17.9 | 17.7 / 17.3 | 1.03 / 1.04 | 12.4 KB | 12.4 KB |
| Serialize, reused + copy | NativeAOT | 24.9 / 24.6 | 24.5 / 24.0 | 1.02 / 1.02 | 12.4 KB | 12.4 KB |
| Serialize, reused, no copy | CoreCLR | 16.9 / 16.9 | 16.4 / 16.2 | 1.03 / 1.04 | 1.78 KB | 1.78 KB |
| Serialize, reused, no copy | NativeAOT | 23.6 / 23.4 | 23.2 / 23.1 | 1.02 / 1.01 | 1.78 KB | 1.78 KB |

### Large response (`Note[2000]`, 434 KB)

| Category | Runtime | A (µs) run 1 / 2 | B (µs) run 1 / 2 | A/B run 1 / 2 | Alloc A | Alloc B |
|---|---|---:|---:|---:|---:|---:|
| Deserialize | CoreCLR | 1,243 / 1,205 | 1,994 / 1,957 | **0.62 / 0.62** | 1,090 KB | 1,468 KB |
| Deserialize | NativeAOT | 1,546 / 1,507 | 2,876 / 2,835 | **0.54 / 0.53** | 1,090 KB | 1,468 KB |
| Serialize, as shipped | CoreCLR | 1,099 / 1,129 | 917 / 900 | **1.20 / 1.25** | 1,580 KB (Gen2 GCs) | 495 KB |
| Serialize, as shipped | NativeAOT | 1,377 / 1,382 | 1,139 / 1,133 | **1.21 / 1.22** | 1,580 KB (Gen2 GCs) | 495 KB |
| Serialize, reused + copy | CoreCLR | 930 / 924 | 892 / 905 | 1.04 / 1.02 | 495 KB | 495 KB |
| Serialize, reused + copy | NativeAOT | 1,141 / 1,136 | 1,130 / 1,115 | 1.01 / 1.02 | 495 KB | 495 KB |
| Serialize, reused, no copy | CoreCLR | 789 / 784 | 771 / 769 | 1.02 / 1.02 | 70.8 KB | 70.8 KB |
| Serialize, reused, no copy | NativeAOT | 996 / 991 | 978 / 975 | 1.02 / 1.02 | 70.8 KB | 70.8 KB |

The remaining allocation in "no copy" (64 B per `Note` pair, 70.8 KB at 2,000 records) is the boxed enumerator from `foreach` over `IReadOnlyList<string> Tags`. A and B both pay it.

### Cross-check: shipping build settings (manual harness)

Median A/B ratios over 15 interleaved rounds; two independent runs per runtime. NativeAOT here uses the default ILC options, not BDN's `Speed` and `native` instruction set.

| Workload | Category | CoreCLR A/B | NativeAOT A/B |
|---|---|---:|---:|
| small | deserialize | 0.62 / 0.63 | 0.50 / 0.50 |
| small | serialize as shipped | 1.46 / 1.45 | 1.50 / 1.54 |
| small | serialize reused (no copy) | 1.05 / 1.04 | 1.13 / 1.13 |
| 50 | deserialize | 0.61 / 0.60 | 0.54 / 0.53 |
| 50 | serialize as shipped | 1.16 / 1.16 | 1.24 / 1.28 |
| 50 | serialize reused (no copy) | 1.05 / 1.04 | 1.11 / 1.13 |
| 2000 | deserialize | 0.62 / 0.63 | 0.55 / 0.55 |
| 2000 | serialize as shipped | 1.27 / 1.25 | 1.31 / 1.34 |
| 2000 | serialize reused (no copy) | 1.03 / 1.01 | 1.07 / 1.08 |

The direction and size of every deserialize and as-shipped result match BDN. One difference: with default ILC options, A's reused-writer serialization trails B by **7–13%** on NativeAOT, against 1–2% under BDN's `Speed` + AVX2 build. Per-sample spread (max−min)/median was 5–40%, mostly single outliers from background load. Medians moved by at most 3% between runs.

### Experiment A2: tuned generated writer

The D1 writer emits `WritePropertyName("text"u8)` followed by a separate value call, and `foreach` over `IReadOnlyList<T>`. `WritePropertyName` checks the name for characters that need escaping on every call. B's generated code writes pre-encoded names. A2 changes only the emitter (`EmitTunedWriter`, `TunedProperty`, `TunedValue`, `TunedItems`) and produces byte-identical output, which `GlobalSetup` asserts.

Manual harness, shipping settings (median A2/B, two runs; allocation per op):

| Workload | CoreCLR A2/B | NativeAOT A2/B | Alloc A2 | Alloc A / B |
|---|---:|---:|---:|---:|
| small | 0.86 / 0.87 | 0.85 / 0.87 | **0 B** | 64 B |
| 50 | 0.88 / 0.86 | 0.85 / 0.86 | **0 B** | 1,824 B |
| 2000 | 0.86 / 0.88 | 0.83 / 0.86 | **0 B** | 72,544 B |

BDN run 3 (categories 3 and 4 with A2): see the next table.

| Workload | Category | Runtime | A | A2 | B | A/B | **A2/B** | Alloc A / A2 / B |
|---|---|---|---:|---:|---:|---:|---:|---|
| small | reused + copy | CoreCLR | 528.0 ns | 446.0 ns | 510.5 ns | 1.03 | **0.87** | 400 / 336 / 400 B |
| small | reused + copy | NativeAOT | 692.7 ns | 604.2 ns | 680.9 ns | 1.02 | **0.89** | 400 / 336 / 400 B |
| small | reused, no copy | CoreCLR | 500.4 ns | 410.0 ns | 482.7 ns | 1.04 | **0.85** | 64 / 0 / 64 B |
| small | reused, no copy | NativeAOT | 659.6 ns | 561.1 ns | 647.8 ns | 1.02 | **0.87** | 64 / 0 / 64 B |
| 50 | reused + copy | CoreCLR | 18.21 µs | 15.16 µs | 17.80 µs | 1.02 | **0.85** | 12,648 / 10,824 / 12,648 B |
| 50 | reused + copy | NativeAOT | 24.73 µs | 21.28 µs | 24.04 µs | 1.03 | **0.89** | 12,648 / 10,824 / 12,648 B |
| 50 | reused, no copy | CoreCLR | 16.79 µs | 13.92 µs | 16.48 µs | 1.02 | **0.84** | 1,824 / 0 / 1,824 B |
| 50 | reused, no copy | NativeAOT | 23.52 µs | 19.90 µs | 23.13 µs | 1.02 | **0.86** | 1,824 / 0 / 1,824 B |
| 2000 | reused + copy | CoreCLR | 939.2 µs | 795.8 µs | 889.1 µs | 1.06 | **0.90** | 506,865 / 434,092 / 506,685 B |
| 2000 | reused + copy | NativeAOT | 1,138.5 µs | 996.7 µs | 1,128.7 µs | 1.01 | **0.88** | 506,942 / 434,139 / 506,896 B |
| 2000 | reused, no copy | CoreCLR | 780.3 µs | 659.9 µs | 761.1 µs | 1.03 | **0.87** | 72,544 / 0 / 72,544 B |
| 2000 | reused, no copy | NativeAOT | 999.0 µs | 873.9 µs | 982.8 µs | 1.02 | **0.89** | 72,549 / 5 / 72,549 B |

With a copy, A2 allocates only the exact output `byte[]`. A and B also allocate the boxed `Tags` enumerators.

## Results: executable size

Minimal apps with the D1 app's settings (`PublishAot`, win-x64, `InvariantGlobalization`), Release, published with the repo props and warnings as errors. **No warnings were raised.** Each app parses a `Note` and a `Note[]`, writes both back, and prints the total byte count. `Size.None` constructs a `Note` and prints, with no serialization.

| App | Executable bytes | Over `Size.None` |
|---|---:|---:|
| `Size.None` (no serialization) | 929,792 | — |
| `Size.A` (generated code only) | **1,257,472** | +327,680 |
| `Size.B` (System.Text.Json context only) | **2,377,728** | +1,447,936 |
| **B − A** | **1,120,256** | B's serialization code is 4.4× larger |

A's executable is 47% smaller than B's. In D1's full app the delta was 888,832 bytes (2,243,072 vs 3,131,904, 28%). That app had dispatch, async and zip code. The same JSON is served, and `Utf8JsonReader`/`Utf8JsonWriter` are paid for in both columns. Adding the A2 writer to the generator left `Size.A` unchanged at 1,257,472 bytes, because the unused tuned methods are trimmed. A shipped A2 would replace `Write…`, not sit beside it.

## Recommendation

**Choose A (generated `Utf8JsonReader`/`Utf8JsonWriter` code).** This confirms the 2026-11-13 decision; the evidence is now speed as well as size.

1. **Speed, deserialization:** A is faster in every case: 0.47–0.62× B's time on both runtimes, with 26–48% fewer bytes allocated. The likely cause, not profiled here, is B's parameterized-constructor path. It buffers arguments and tracks required parameters, and the Web defaults add case-insensitive name lookup.
2. **Speed, serialization:** as the D1 spike ships it, A is slower: 1.17–1.62× B's time, with 3–13× more allocation and Gen2 collections at 2,000 records. That comes from `SparJson.Write`'s per-call buffers, not from the generated code. With the same reused writer, A is at parity (1.01–1.04× under BDN; 1.01–1.13× under shipping AOT settings). A2 is **10–17% faster than B (BDN 0.84–0.90, shipping-settings harness 0.83–0.88) and allocates nothing beyond the output array**.
3. **Size:** A's executable is 1,120,256 bytes (47%) smaller.

**Required for M1, or the serialization result inverts:**

- `SparJson.Write` must not allocate an `ArrayBufferWriter` and `Utf8JsonWriter` per call. Reuse a per-thread or pooled writer and buffer, and copy once to an exact `byte[]` (category 3). Or let the bridge supply the buffer (category 4), if its transport can consume a span. Response writers can use `SkipValidation = true`, because generated code always emits valid structure.
- Adopt A2's emitter shape: pre-encoded `JsonEncodedText` names, the combined `Write*(name, value)` overloads, and indexed loops over `IReadOnlyList<T>`/arrays.
- Fix the two correctness gaps from the edge-case table: reject trailing content, and surface every malformed-input error as `JsonException`.

**What the real bridge would use.** The bridge transport is still open (M1: `WebResourceRequested` response or a web message). The spike's dispatch returns `ValueTask<byte[]>`, an owned array per response. **Category 3 (reused writer + one exact `byte[]`) is therefore the realistic comparison today.** A string-based web message would add one UTF-8→UTF-16 transcode, the same for A and B. Category 4 is the floor if the transport can take the bytes directly.

## Caveats and untested scenarios

- Background load from another agent was present throughout (about 1–2 logical cores). The A/B ratios repeated within ±0.09 between the two BDN runs and within ±0.04 between the two manual runs per runtime, so the conclusions do not depend on it. Absolute times may be a few percent pessimistic.
- BDN's NativeAOT build uses `IlcOptimizationPreference=Speed` and the native instruction set. The manual harness covers the shipping defaults; it agrees on direction and differs only in the size of the reused-writer gap.
- One DTO shape was measured. Not tested: wide DTOs, deep nesting, long strings, number-heavy or escape-heavy payloads, enums, dictionaries, `int` responses, and concurrency (a pooled writer needs a per-thread or rented design).
- A2 was measured in one BDN run (run 3) and two manual runs per runtime, not in BDN runs 1 and 2. The shipped `SparJson.Write` fix (pooled writer) was approximated by the reused-writer variants, not implemented in `SparJson`.
- B's numbers are for the D1 options. B with `JsonSerializerDefaults.General` (case-sensitive, no string numbers) or `GenerationMode = Serialization` was not measured. It might narrow the deserialization gap, but it would also change the wire contract.
- Exceptions on malformed input, and first-call (cold start) cost, were not timed.

## Reproduce

```powershell
cd spikes\serializer-bench
dotnet build Bench\Bench.csproj -c Release
.\Bench\bin\Release\net11.0\Bench.exe verify
$env:BENCH_ARTIFACTS = "<a folder outside the repo>"   # optional; BENCH_JOB=CoreCLR|NativeAOT runs one job
.\Bench\bin\Release\net11.0\Bench.exe --filter '*'
dotnet publish Bench.Manual\Bench.Manual.csproj -c Release
dotnet Bench.Manual\bin\Release\net11.0\win-x64\Bench.Manual.dll 15 CoreCLR
.\Bench.Manual\bin\Release\net11.0\win-x64\publish\Bench.Manual.exe 15 NativeAOT
foreach ($p in 'Size.None','Size.A','Size.B') { dotnet publish "$p\$p.csproj" -c Release }
```

BenchmarkDotNet reports from runs 1–3 are copied to `results/run1..3`. The raw output of manual runs 3–4 (reused and A2 rows only) is in `results/manual-runs-3-4.txt`. Manual runs 1–2 are recorded only in the tables above.

**Repository change:** `Directory.Packages.props` gained `<PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />`. Nothing else outside this folder changed.

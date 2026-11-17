# M1a: the rest of the wire-rules table

Recorded 2026-11-16. What it shows: every kind of the wire-rules table (plan section 2) except enums now goes through the generated serializers, the generated TypeScript and the SDK, with one spelling per value and every malformed value refused before the command runs. Decision: "The rest of the wire-rules table".

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Generator` | `WireKind` gains `Byte`, `SByte`, `Int16`, `UInt16`, `UInt32`, `UInt64`, `Single`, `Decimal`, `Char`, `DateTime`, `DateOnly`, `TimeOnly`, `TimeSpan` and `Bytes`; `byte[]` is `Bytes`, not a list, and a valid request; SPAR024 warns about `DateTime` once per place. |
| `src/Spar/Runtime/SparJson.cs` | A reader for each kind, with the checks of the decision, and writers for the ones `Utf8JsonWriter` does not spell as the table does. |
| `src/Spar.Sdk.Tasks/Client` | The TypeScript spellings and the SDK functions each kind's code calls; the client step stops only on errors. |
| `src/Spar.Client/src/wire.ts` | `readByte` … `readBytes` and their writers, with range, pattern and finiteness checks. |
| `tests/apps/WireShapes` | `KindsService`: `echo(Kinds)`, `length(byte[])`, `single(string) -> float`. |
| Tests | `tests/WireShapes.Tests/KindsTests.cs` (round trip, exact wire text, 31 malformed values, `byte[]` as a request, float writer failures); `tests/Spar.Tests/DateTimeWireTests.cs`; the generator's SPAR024 and every-kind tests; the client step's spellings, and the `Kinds` contract added to the differential `tsc` fixture; the SDK's wire tests; the browser round trip of every kind and a refused byte. |

## Checked before writing the readers (a scratch program, .NET 11.0.100)

| Behaviour | Observed |
|---|---|
| `decimal.TryParse("0.123456789012345678901234567890123")` | true, rounded to `0.1234567890123456789012345679` |
| `(-0.00m).ToString()` | `0.00` |
| `Utf8JsonReader.TryGetSingle` on `1e39` | true, infinity |
| `TryGetBytesFromBase64` on `"AQ ID"` | true (whitespace skipped); `"AQI"` false; `"AQJ="` false |
| `Utf8JsonWriter.WriteStringValue("a\ud800b")` | `"a�b"` |
| `DateTime` written by Kind | `…Z`, `…` (none), `…+01:00`; read back with an offset as `Kind = Local` |
| `TimeSpan.TryParseExact("1:2:3", "c")` | true |

## Results (observed, Windows 11)

| Check | Result |
|---|---|
| `KindsTests` | 37 of 37 |
| `DateTimeWireTests` | 7 of 7 |
| The browser round trip | every kind back with its type (`bigint`, `Uint8Array`, strings); a byte of 256 refused on the page as `rejected` / `invalid-request` |
| SDK, `npm test` and `npm run check` (TypeScript 6 and 7) | 147 of 147; clean |
| `dotnet test --solution Spar.slnx` | 415 of 415 |
| NativeAOT lane | Spar.Tests 158 of 158, WireShapes.Tests 74 of 74, no warnings |
| `Spar.PackageTests`, `Spar.BuildTests` | 10 of 10, 6 of 6 |
| `AppModel.Tests` after the documentation change | 10 of 10 |
| `-mt` build, size check | clean; 215 source files |

## Found while building

- The analyzer threw (AD0001) until SPAR024 was in its supported list; a test caught it.
- A `DateTime` list was warned about twice, for the list and for its item, with different messages; now once per place.
- RS1032 refused the first message: a message of several sentences ends with a period.

## Not covered

- `Int128`, `UInt128`, `BigInteger` (enums: see the follow-up below).
- Linux and CI for this change.

## Follow-up: enums (2026-11-16)

Decision: "Enums on the wire".

| Path | Contents |
|---|---|
| `src/Spar.Generator/ContractAnalysis.WireTypes.cs` | Enums and SPAR025, and the primitive classification moved out of `ContractAnalysis.cs` (801 lines otherwise). |
| `src/Spar.Generator/Emit/SerializerEmitter.cs` | A reader and a writer per enum; members written with `@`, so one named like a keyword compiles. |
| `src/Spar.Sdk.Tasks/Client` | The union type, the reader and writer `switch`es, the layout and the snapshot. |
| `src/Spar.Client/src/wire.ts` | `unknownMember` (a `TypeError`) and `refusedMember` (a `RangeError`). |
| `tests/apps/WireShapes` | `Priority` in `Kinds`, and `kinds.priority(int)`, which casts. |
| Tests | SPAR025 for each reason, with its message; SPAR004 and SPAR040 for enum names; a member named `class` through C# and TypeScript; an enum shared by two contracts; the differential `tsc` test with enums; `"normal"`, `"Urgent"`, `1` and a missing member refused; `High` written; a cast `42` a writer failure; the page sending and receiving an enum, and `"Urgent"` refused before sending. |

| Check | Result |
|---|---|
| `KindsTests` | 42 of 42 |
| Generator, client step, SDK | 83 of 83, 59 of 59, 148 of 148 |
| `dotnet test --solution Spar.slnx` | 427 of 427 |
| NativeAOT lane | Spar.Tests 158 of 158, WireShapes.Tests 79 of 79, no warnings |
| `Spar.PackageTests`, `Spar.BuildTests`, `AppModel.Tests` | 10 of 10, 6 of 6, 10 of 10 |
| `-mt` build, size check | clean; 216 source files |

# M1a: the console harness and the production boundary

Recorded 2026-11-16. What it shows: the core (the bridge codec, a bridge session with its limits, dispatch with a DI scope per command, and the generated serializers) publishes as a Production NativeAOT executable with no warnings. That executable writes exactly what its CoreCLR build writes, and carries none of the test host, the loopback host or the test libraries, while the bridge code around them is kept. Decision: "Console harness built; its budget raised to 3.5 MB; lone surrogates on read".

## What was added

| Path | Contents |
|---|---|
| `tests/apps/Spar.Harness/` | A Spar app (source mode, in `Spar.slnx`) with one contract, `echo`: the wire shapes, declared errors with and without a payload, a `long`, a `double` that can be NaN, a command that throws and one that waits for cancellation. Its `Program.cs` calls `SparApplication.Create` like any app, then hands the app to `ConsoleHost`. |
| `tests/apps/Spar.Harness/ConsoleHost.cs` | A host with no window. It builds the app as a desktop host will (`SparRunningApp`, `ValidateScopes`), runs every vector of `tests/conformance/bridge-messages.json` through the codec, then feeds a fixed script to a bridge session and writes each message in and out, one line each, as UTF-8 with LF line endings. It waits for a step's commands before the next, so the order is the same in every run, and gives every call one `traceparent`, so correlation ids are too. Exit code 0 when every vector passed. |
| `src/Spar/Hosting/SparRunningApp.cs` | `SparRunningApp`, moved out of the test session's file: the test host builds one, and so will every host. `SparApp` exposes its builder and catalog internally, and `Spar` lets `Spar.Harness` see its internals, as it will let the hosts. |
| `tests/Spar.BuildTests/HarnessTests.cs` | Two tests on the copy's Release build and its NativeAOT publish (below). |
| `tests/Spar.BuildTests/ProductionBoundary.cs` | The markers of the boundary test, shared by the Notebook's test and the harness's. |

The script, for a page with the app's contracts: `hello`; the shapes echo (a lone-surrogate escape, a non-BMP character, the extreme `int` and `long`, a nested parent, an unknown property); a success, an `empty` error and a `refused` error with its payload; `long.MinValue` as a string; NaN, which cannot be written; a command that throws; an unknown command; a truncated request; a running call and its cancel; a message over the 64 KiB limit the harness sets; a call with a lower id; a message of an unknown type. Then a page built against other contracts: `hello` with another fingerprint, and one call.

## Results (observed, Windows 11, .NET SDK 11.0.100)

| Check | Result |
|---|---|
| `dotnet test --project tests/Spar.BuildTests` | 5 of 5 in 70 s (the Notebook's three and the harness's two) |
| Release build of the harness, in the copy | 2.69 s |
| NativeAOT publish of the harness, in the copy | 5.65 s, no warnings |
| `Spar.Harness.exe` | 3,104,768 B; 3,043,840 B without the vector reading (`JsonDocument`), so the core alone is over the 3 MB budget too. The spike's 2,243,072 B app had dispatch and serializers but no bridge session, DI scope validation, logging or instrumentation |
| CoreCLR output against NativeAOT output | byte for byte identical, 89 lines; every canonical vector re-encodes exactly, every `decode` vector decodes, every malformed one is refused |
| Session outcomes | `open` with the fingerprint; results and declared errors; `response`/`serialization`/`completed` for NaN; `failed`/`failed`/`unknown` with the correlation id and no `detail`; `rejected` with `unknown-command`, `invalid-request`, `too-large` and `contract-mismatch`, `notStarted`; `cancelled`/`stopped`; the lower id and the unknown type dropped, with their reasons |
| Boundary | none of the markers (as UTF-8: `SparTestSession`, `SparTestApp`, `ISparEntryPoint`, `LoopbackHost`, `LoopbackChannel`, `LoopbackAssets`, `Spar.Loopback`, `Spar.Testing`, `Microsoft.AspNetCore.Server.Kestrel`; as UTF-16: `/_spar/channel`, `/_spar/send`, `Spar.TestHost.IsSupported`); the control finds `BridgeSession`, `SparDispatcher` and `BridgeCodec` |
| `dotnet test` (solution) | 350 of 350 |
| `dotnet test --project tests/Spar.PackageTests` | 7 of 7 in 2 min 33 s, with the new check that a `SparTestedApplication` item's `Aliases` reaches the resolved reference (`ReferencePath`) |
| `dotnet build Spar.slnx -mt` | clean |
| `dotnet run eng/check-size.cs` | 198 files, clean |

## Found by the harness

- **A lone-surrogate escape in a string the app reads failed the call as `failed` / `unknown`,** a handler failure, although the wire rules decide it becomes U+FFFD and is never an error. `Utf8JsonReader.GetString()` throws `InvalidOperationException` for it, and dispatch treats anything but a `JsonException` as the command's own. `WireShapes.Tests` had checked the escape only inside a skipped property. Fixed in `SparJson.ReadString`: when `GetString` throws, a slow path decodes the escapes itself, replaces unpaired surrogates with U+FFFD, and turns invalid UTF-8 into a `JsonException` at the value's offset, so the call is `rejected` / `invalid-request`. `TryGetDateTimeOffset` also throws, rather than returning false, on such an escape; a date holding one is now malformed. Three tests were added to `WireShapes.Tests` (30 of 30); the harness now echoes `lone � end` on both runtimes.

## Not verified

- **Linux:** the plan's exit asks for the harness on both OSes; it has run on Windows only (the Linux runs are a separate M1a item).
- **The size breakdown:** which parts of the core make up the 3.04 MB was not measured (no mstat analysis).
- **A desktop host's own code:** the harness stands in for one; the boundary with a real host in it comes with M1b and M1c.
- **The package tests' time:** 2 min 33 s, against 49.5 s for the run before; the cause was not looked into (a fresh pack after the runtime change is the likely one).

# M1a: the contract diagnostics, located

Recorded 2026-11-15. What it shows: every diagnostic of the starting map (plan section 2, "Diagnostics") that a single compilation can raise is reported by the analyzer at its declaration, once, as you type; the cross-contract checks run on every contract at once; the client step no longer reports its own copies, and writes nothing while the contracts have a problem. Decision: "Contract diagnostics located in the analyzer".

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Generator/ClientNames.cs` | The names the generated TypeScript uses, in one place: the SDK imports, the reserved subpaths, the module's own names, the reserved words, the name rules, and the reader, writer and decoder names. The client step's emitters write with them, so the analyzer checks what the emitters write. |
| `src/Spar.Generator/ContractAnalysis.cs` | SPAR003 (a name that is not camelCase ASCII, or for a type not ASCII letters and digits), SPAR004 (reserved), SPAR015, SPAR016, SPAR021 (a settable property outside the constructor), SPAR022, SPAR023, SPAR031, SPAR032, SPAR034, and SPAR040 within one contract. An explicit name's problem is reported at its attribute, a derived name's at the declaration. `[SparName]` on a record or error type now sets its TypeScript name (`RecordModel.Name`, `ErrorModel.Name`). |
| `src/Spar.Generator/ContractSet.cs` | SPAR005 (two contracts with one name, ignoring case) and SPAR040 in the shared module; a contract in either is left out of the generated C# and client. |
| `src/Spar.Generator/ContractAnalyzer.cs` | Reads every contract once per compilation, runs `ContractSet`, and files each problem under the type that contains it; each type's symbol action reports its own, once per location and message. |
| `src/Spar.Sdk.Tasks/` | `ClientLayout` takes the names from the model and checks nothing; `ContractSource` counts the problems; `SparClient` writes nothing when there are any. Decoders are now `decodeGet` (they were `getDecoder`), so a decoder can no longer take a reader's name. |

## Results (observed)

| Check | Result |
|---|---|
| `tests/Spar.Generator.Tests` | 55 of 55: 35 located rows (each `ID: underlined text`), 5 clash rows, the shared-module clash, a type two contracts use reported once, the messages, and clashing contracts left out of a catalog that still compiles |
| `tests/Spar.Sdk.Tasks.Tests` | 41 of 41, 11 fewer than before: the name checks the client step used to report are now generator tests; `[SparName]` renames a type in the `.d.ts` and `.js` |
| `dotnet test --solution Spar.slnx` | 269 of 269; both committed contract snapshots unchanged |
| NativeAOT lane | WireShapes.Tests 21 of 21, Notebook.Tests 12 of 12, no warnings |
| `-mt` build, size check | clean; 109 source files |
| The Notebook's contract named `shared` in a real build | one error, `PagesService.cs(6,2): error SPAR004: Contract 'PagesService' resolves to the name 'shared', which @spar/app uses for one of its own subpaths. Use another name in [SparContract("…")]`; the client step printed that it kept the last good client; the generated package and the snapshot kept their bytes. Restored, the next build succeeded with the client unchanged. |

## Not done or not verified

- **Not built:** contracts from referenced assemblies (the generator discovers only the project's own), so the referenced half of the clash matrix and the generator's location-less diagnostics; the decided rules without an ID yet (yield after an error, `Capacity` with coalescing, `[Flags]`, `DateTime`, the cancellation suggestion); SPAR030's `closed` check.
- **Not run:** an IDE session (the squiggles and their timing after an edit), Linux, CI.

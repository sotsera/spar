# M1a: contracts from referenced libraries

Recorded 2026-11-15. What it shows: an app serves a library's contracts only when its project lists the library in `SparContracts`; the clash matrix between an app and its libraries, with each library as a project reference and as a built assembly; and the list reaching the generator, the analyzer and the client step in real builds, design time included. Decision: "Contracts from referenced libraries: the app opts in".

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Generator/ContractProblems.cs` | Discovery of the listed libraries' public contracts, SPAR006, and the referenced-assembly form of every diagnostic (no location, "In referenced assembly 'X': …"); clashes placed at the project's declarations, else once. "In the project" means a tree of this compilation. |
| `src/Spar.Generator/SparGenerator.cs` | The listed libraries' contracts, read from the compilation, join the project's own before the set check. |
| `src/Spar.Generator/ContractAnalyzer.cs` | Reads the list from the compiler's config; reports the unlocated problems when the compilation ends. |
| `src/Spar.Sdk/Sdk/Sdk.targets` | The `SparContracts` items become `SparContractAssemblies` (a compiler-visible property), join the client step's inventory, and reach the task with the project file. |
| `src/Spar.Sdk.Tasks/` | `ContractAssemblies` and `ProjectFile` on `SparClient`; a referenced contract's module header names its library; a problem with no place is reported against the project file. |
| Tests | `tests/Spar.Generator.Tests/ReferencedContractTests.cs` (20: eight cases with the library as a project reference and as metadata, SPAR006 three ways, the list's case and spaces); `tests/Spar.Sdk.Tasks.Tests` (a library's module, its shared type, its header). |

## Results (observed)

| Check | Result |
|---|---|
| `dotnet test --solution Spar.slnx` | 290 of 290 (Spar.Generator.Tests 75, Spar.Sdk.Tasks.Tests 42) |
| `-mt` build, size check | clean; 112 source files |
| A scratch app and library under `artifacts/referenced-check/`, source mode | listed: the client has `jobs`, `queue` and `shared` (the library's `Job` once), the snapshot lists both contracts, the generated catalog has `"jobs"` and `AddScoped<global::Lib.JobsService>`; the item removed: an incremental build deleted `jobs.js` and `shared.js` and the snapshot dropped the contract; a misspelled `Libb`: `App.csproj : error SPAR006: SparContracts lists 'Libb', which the project does not reference. …`; a command in the library returning `Job`: `App.csproj : error SPAR010: In referenced assembly 'Lib': Command 'Get' returns 'Lib.Job'. …`; restored, an unchanged build skipped the client step |
| Design-time compile of the scratch app | the compiler's config has `build_property.SparContractAssemblies = Lib` |

## Not done or not verified

- **Not run:** a call to a library's command at run time (done since: see the follow-up below), the package-only consumer with a library from a package, an IDE session, Linux, CI.
- The scratch projects were deleted after the run; the generator and client tests keep the cases.

## Follow-up 2026-11-16: a library's command at run time

Decision: "A library's contract at run time; Spar's properties stay off plain references".

### What was added

| Path | Contents |
|---|---|
| `tests/apps/WireShapes.Library/` | A plain class library (`Microsoft.NET.Sdk`, referencing the Spar runtime only) with `LedgerService`: `post(Entry) -> Entry \| LedgerError` with a `long` in the request, the value and the error's payload, and `who()`, which returns the contract and command `ISparCall` holds. The contract takes the app's singleton `Ledger` and the scoped `ISparCall` from DI. |
| `tests/apps/WireShapes/` | References the library and lists it in `SparContracts`; `Program.cs` registers `Ledger`; the snapshot gains `contract ledger`; the page calls `ledger.post` and `ledger.who` through `/spar/app/ledger.js`. |
| `tests/WireShapes.Tests/LibraryContractTests.cs` | Three tests through the generated `LedgerClient` and the raw invoker: a value and a declared error with `long` values beyond 2^53, the balance kept by the app's singleton between calls; `ISparCall` filled (`ledger.who`); the wire form (`"cents":"5"`, `{"code":"overdrawn","balance":"5"}`, a number for a `long` rejected as `invalid-request`). |
| `tests/WireShapes.EndToEndTests/BrowserBridgeTests.cs` | The page in Chromium posts 9007199254740993 cents and gets a `bigint` back, then the overdrawn error with its payload, then `ledger.who`. |
| `src/Spar.Sdk/Sdk/Sdk.targets` | `SparPlainReferences`: `SparVariant` and `SparTestLane` are removed from every project reference except the tested app, transitive references included, before the first call to them. |
| `tests/Spar.BuildTests/` | `Dotnet.cs` counts each project's `CoreCompile` instances in the replayed log; `ReferenceBuildTests` builds `WireShapes.Tests` in the clean copy and requires one per project. |

### Found while building

The first build of `WireShapes.Tests` failed with CS2012 on `src/Spar/obj/Debug/net11.0/Spar.dll`: the tested app is built with `SparVariant=Test`, its library inherited the global property, and passed it on to `Spar.csproj`, which the app itself references without it. Spar and the library were each built twice, with and without the variant, into one `obj/`. With `SparPlainReferences`, the binary log has one build evaluation of each, in the Debug build and in the NativeAOT lane's publish (where the test project also passes `SparTestLane=Aot` to the library, transitively).

### Results (observed, Windows 11)

| Check | Result |
|---|---|
| `WireShapes.Tests` | 33 of 33 (3 new) |
| `WireShapes.EndToEndTests` | 14 of 14 (1 new) |
| NativeAOT lane, `WireShapes.Tests` published and run | 33 of 33, no warnings |
| `ReferenceBuildTests` in the clean copy | passes; with `SparPlainReferences` disabled it fails, and that clean build hits CS2012 again |
| `dotnet test --solution Spar.slnx` | 358 of 358 |
| `Spar.BuildTests` | 6 of 6 (the `WireShapes.Tests` build in the copy: 3.9 s) |
| `-mt` build, size check | clean; 203 source files |

### Not covered

- **A library's documentation comments do not reach its TypeScript module:** the client step loads referenced assemblies without their XML documentation files (`MetadataReference.CreateFromFile` with no documentation provider), so `ledger.d.ts` has no JSDoc even when the library writes its `.xml`. The app's own contracts are not affected.
- A library from a package, a library with an interface contract implemented in the app, Linux, CI.

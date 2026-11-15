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

- **Not run:** a call to a library's command at run time (the generated dispatch compiles and is registered; no test drives it yet), the package-only consumer with a library from a package, an IDE session, Linux, CI.
- The scratch projects were deleted after the run; the generator and client tests keep the cases.

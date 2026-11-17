# M1a, first slice: build, generator, test host, one contract

Recorded 2026-11-15. What it shows: the chain from a contract in an app to a command test, built from Spar's own sources (not a spike), passes under CoreCLR and in the NativeAOT test lane, with nothing of the test machinery in the Production executable. Decisions: "CI on GitHub Actions; the first slice", "Test host mechanics: the generated entry point", "M1a names".

## Environment

Windows 11 Pro 10.0.26320, SDK 11.0.100, `Microsoft.CodeAnalysis.CSharp` 5.9.0 for the generator, `Microsoft.Extensions.DependencyInjection` 11.0.0, `xunit.v3` and `xunit.v3.aot` 4.0.3. Repository policy: warnings as errors (`MSBuildTreatWarningsAsErrors` too), code style, trim and AOT analyzers on in every project. Not run on Linux.

## What exists

| Path | Contents |
|---|---|
| `src/Spar/` | `Result<T, TError>` and `Result<TError>` (C# 15 unions), the contract attributes (`TimeoutSeconds`), `ISparCall`, `SparApplication.Create` and `Run`, `SparBuilder`, `SparFolders`, `AddExternal`, and under `Runtime/` the catalog, the dispatcher (one scope per call, the time limit folded into the token, the `CallError` outcomes), `SparJson` (one depth limit of 64, trailing content rejected, the wire primitives), and the test session behind `Spar.TestHost.IsSupported`. |
| `src/Spar.Generator/` | One analysis shared by the generator and `ContractAnalyzer`. The generator writes `SparContracts` (a handler per command, the catalog with its fingerprint, the registration of class contracts), `SparSerializers` (a reader and a writer per wire type: records, `closed record` errors, lists, nullable values, `string`, `bool`, `int`, `long` as a canonical string, `double`, `Guid`, `DateTimeOffset`), the `Create` interceptor, and in the Test variant the entry point and the typed test clients. The analyzer reports SPAR001, 002, 010–014, 020, 021, 030, 033, 035, 060 and 061. |
| `src/Spar.Testing/` | `SparTestApp.StartAsync<Program>()`, `SparTestOptions` (`Services`, `AllowExternal`, `KeepData`), the per-instance data roots. |
| `src/Spar.Sdk/Sdk/` | `Sdk.props` and `Sdk.targets`: the variant mapping, per-variant `obj/` and `bin/`, the NativeAOT test lane, NativeAOT for apps, the test-host switch, test results under `artifacts/test-results/`, and source-mode references. |
| `samples/Notebook/` | `Program.cs` in the decided shape, the `pages` contract (create, get, list, save, delete) with `PageError` (`TitleTaken`, `InvalidTitle`, `NotFound`, `ChangedElsewhere`), and `MarkdownPageStore`: one Markdown file per page with `title`, `revision` and `updated` front matter, replaced atomically, ids that cannot leave the notebook folder. |
| `samples/Notebook.Tests/` | Ten command tests through the generated `PagesClient`. |
| `.github/workflows/ci.yml` | The CI workflow, not run yet. |

## Results (observed)

| Check | Result |
|---|---|
| `dotnet build Spar.slnx` | succeeded, 0 warnings; repeat builds (no-op) 417, 446 and 462 ms |
| `dotnet build Spar.slnx -mt -nodeReuse:false` | succeeded, no MSB4286–4288 |
| Design-time compile of the Notebook (`-t:Compile -p:DesignTimeBuild=true …`) | exit 0, no diagnostics |
| `dotnet test --solution Spar.slnx` | 10 of 10 passed (657 ms total); results in `artifacts/test-results/Notebook.Tests/`, no `TestResults/` in the working directory |
| The test executable directly, CoreCLR | 10 of 10, 181–221 ms test time (3 runs) |
| NativeAOT lane: `dotnet publish samples/Notebook.Tests -c Release -p:SparTestLane=Aot` | 0 warnings, 12 s, executable 16,475,136 B; 10 of 10, 19 ms test time (3 runs) |
| The `Test` build of the app | `bin/Test/` and `obj/Test/`, with `Spar.TestEntryPoint.g.cs` and `Spar.TestClients.g.cs`; the `Development` and `Production` builds have neither |
| Production publish of the Notebook (Release, NativeAOT) | 2,541,056 B. Absent from the image: `SparTestSession`, `ISparEntryPoint`, `SparEntry`, `PagesClient`. Running it exits 2 with "This build of Spar has no desktop host yet." |
| A throwaway app with three invalid commands | SPAR010 (sync return), SPAR011 (two requests) and SPAR020 (`Dictionary<string, int>`), each at its line and column; the valid command beside them still generated |

What the ten tests cover: a page stored as Markdown with its front matter (exact file text); a page without text; a taken title and two invalid titles as declared errors with their payloads; a save over a newer revision refused with `ChangedElsewhere(2)` and the page unchanged; listing order and delete, then delete again as `NotFound`; a request without its required field as `rejected`/`invalid-request`/`notStarted` and an unknown command as `unknown-command`, with nothing stored; an id with `../` that never reaches outside the notebook folder; two apps at once with separate notebooks and data roots.

**Problems met and fixed** (in `docs/pitfalls.md`): generated code with unread primary-constructor parameters failed the build (CS9113); `PublishAot` set after Microsoft.NET.Sdk's targets gave a framework-dependent apphost; the NativeAOT lane's `obj/aot/` made the default globs compile the other lane's generated files; an item `Update` cannot test custom metadata outside a target (MSB4191).

## Not done or not verified

- **Linux:** nothing ran on Linux.
- **CI:** the workflow has never run.
- **Generator tests under `tests/`:** none yet. No snapshot tests, diagnostic tests or clash matrix; only the throwaway app above.
- **Contract rules not implemented:**
  - the rest of the diagnostic map (names, clashes, reserved names, `[Flags]`, `DateTime`);
  - `closed` checking of error types (any record with nested derived records is accepted);
  - interface contracts, which are generated but untested;
  - streams and subscriptions;
  - `[SparName]` on types;
  - `decimal`, `DateOnly`, `TimeOnly`, `TimeSpan`, `byte[]`, `char`, enums and `ulong`;
  - the lone-surrogate rule (a lone-surrogate escape in a request is a `JsonException`, so `invalid-request`, not U+FFFD).
- **Not built:** the contract snapshot, the manifest, the TypeScript client step and the bridge codec.
- **Untested at run time:** `TimeoutSeconds`, cancellation, `ISparCall`, `AddExternal` in this codebase (it was tested in `spikes/test-host`), and the `response`/`serialization` path.
- **SPAR020's location** is the command, not its return type as the spike catalogue says.
- **Variant folder naming:** a test project's own build folder follows its configuration (`bin/Production/` for the Release NativeAOT lane), since `IsTestProject` is not known when the variant is chosen. It is harmless but reads oddly.
- **Not built:** the package-only consumer and `Spar.Sdk` packing.

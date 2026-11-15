# app-model-check results

**Moved on 2026-11-15:** the check now lives in `tests/AppModel` (the same files, at the same depth), and `dotnet test` runs it through `tests/AppModel.Tests` (decision "The documentation check under tests/"). This record keeps its history; the paths below are relative to `tests/AppModel`. Since then `tests/AppModel/Real` also builds the C# blocks Spar supports with real Spar, against `tests/AppModel/Planned` for the rest.

Checks that every C# and TypeScript block of `docs/app-model.md` compiles, verbatim, against stand-ins for the Spar API. First run on 2026-11-14. The figures in the sections above "History" are from the rerun of 2026-11-15, after a review of the document; "History" keeps the earlier runs that explain a decision.

**Outcome (2026-11-15): all 26 document blocks are verbatim copies and compile.** The contract-rule check finds no broken rule, all 21 deliberate TypeScript mistakes and both deliberate C# mistakes fail with the expected codes, and all 17 run-time routing checks pass.

## Tools

Versions observed on 2026-11-15:

- .NET SDK 11.0.100. C# `LangVersion` 15.0, the default and the maximum. `net11.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` inherited from the repository's `Directory.Build.props`.
- Node 24.21.0, npm 11.19.0.
- TypeScript 7.0.2 (`typescript`) and 6.0.3 (`typescript6`, an npm alias), with `@types/react` 19.3.0 and `@types/react-dom` 19.3.0. `node_modules` was already present from the first run (`npm install --offline --ignore-scripts`, from the offline npm cache); nothing was installed or downloaded on 2026-11-15.

## Layout

| Path | Contents | From the document? |
|---|---|---|
| `tools/extract-blocks.mjs` | Copies each `csharp`/`ts`/`tsx` block into the file named in the paragraph before it. Writes only changed files. `--check` fails if a copy differs from the document. | tool |
| `tools/check-contracts.mjs` | Reads the document's C# blocks and checks each contract against the rules that compiling against stand-ins cannot enforce (see "Contract rules"). Added on 2026-11-15. | tool |
| `SqlTool/Program.cs` and 12 files under `SqlTool/Connections`, `CopyJobs`, `Monitoring`, `Recipes`, `Scripts` and `Status` | the 13 C# blocks (table under "C# results") | verbatim |
| `frontend-generated/copyJobs.d.ts` | the generated-module block, served as `@spar/app/copyJobs` through `paths` | verbatim |
| 12 files under `frontend/src/` | the other 9 `ts` blocks and the 3 `tsx` blocks (table under "TypeScript results") | verbatim |
| `Spar.Stubs/` | stand-ins for exactly the Spar C# API the document uses | no |
| `SqlTool/Support.cs` | `IConnectionStore`, `FileConnectionStore` (in memory), `ConnectionsApi : IConnections` | no |
| `SqlTool/ExhaustiveChecks.cs` | complete `switch` expressions over `PlanError`, `RunError` and two `Result` types with no default arm, plus `Result` conversions outside `async` | no |
| `SqlTool/Failing/ExhaustiveMissingCase.cs`, `SqlTool/Failing/FileDialogMissingCase.cs` | a `PlanError` switch and a file-dialog `Result` switch, each with one case missing. Compiled only with `-p:SparCheckMissingCase=true`. | no |
| `sdk/core.ts`, `sdk/c-call/sdk.ts` | the SDK prototype. They started as copies of `spikes/ts-results/forms/core.ts` and `forms/c-call/sdk.ts` and have changed since, in the reruns under "History" and on 2026-11-15, so they no longer equal those files. | no |
| `sdk/app-sdk.ts` | `@spar/app/sdk`: re-exports the prototype and adds `SubscribeOptions<R>`, `ready()`, `setColorScheme()` and the page APIs of the native services | no |
| `frontend-generated/jobLog.d.ts` | two `Manual` subscriptions for the probes and mistakes: `follow`, and `tail`, which declares errors | no |
| `frontend-generated/windows.d.ts` | `@spar/app/windows`: the window kinds of `Program.cs` as a union | no |
| `probes/ok.ts` | positive controls for the mistakes in `bad/bad.ts` | no |
| `bad/bad.ts` | mistakes that must fail. The `[TSxxxx]` tag on each numbered comment is the exact set of codes its next line must produce. | no |
| `tools/check-bad.mjs` | runs `tsc -p tsconfig.bad.json` and compares the diagnostics with the tags, line by line. It fails on a clean compile, a missing code or an extra code. | tool |
| `tools/failure-routing.mts` | run-time checks of where `settle` and `unwrapOutcome` send each outcome, run by Node with type stripping. It is not type-checked. | tool |

Neither the scripts nor the tsconfig files use absolute paths: paths are resolved from the script's location or from the tsconfig.

## Commands

From `spikes/app-model-check/`, as run on 2026-11-15:

```text
node tools/extract-blocks.mjs                          26 blocks, 7 written (the blocks the review changed)
node tools/extract-blocks.mjs --check                  26 blocks, 0 differ
npm run check                                          exit 0; it runs, in order:
  verbatim   (extract-blocks --check)                  26 blocks, 0 differ
  contracts  (check-contracts)                         7 contracts, 14 commands, 18 error cases: no rule broken
  check:ts7  (tsc 7.0.2 -p tsconfig.json)              0 errors
  check:ts6  (tsc 6.0.3 -p tsconfig.json)              0 errors
  bad:ts7                                              all 21 mistakes rejected as expected
  bad:ts6                                              all 21 mistakes rejected as expected
  routing                                              18 ok, 0 FAIL
dotnet build AppModelCheck.slnx --no-incremental       Build succeeded. 0 Warning(s) 0 Error(s)
dotnet run --project SqlTool --no-build                prints the recorded configuration, exit 0
dotnet build SqlTool -p:SparCheckMissingCase=true      Build FAILED: 2 Error(s), both CS8509 (expected)
```

Each TypeScript script runs `node node_modules/<package>/bin/tsc`, because both packages provide a `tsc` bin. The C# builds ran in place: they write only under `bin/` and `obj/`, which git ignores.

## Changes of 2026-11-15 (document review)

The review found examples that broke rules the document states. Each was fixed in the document, and the check was extended where it could not have caught the defect.

| Defect in the document | Fix | What now guards it |
|---|---|---|
| `ExportAsync(IReadOnlyList<string> tables, …)`: a list as a request | a positional record, `ExportRequest(IReadOnlyList<string> Tables)` | `check-contracts` |
| `ExportService` gives the contract name `export`, a JavaScript reserved word (found during the review, not in its list) | renamed `CsvExportService` (`csvExport`), in `SqlTool/Recipes/CsvExportService.cs`; `ExportService.cs` deleted | `check-contracts` |
| `CrashInfo? LastCrash()`: a synchronous command | `Task<CrashInfo?> LastCrashAsync()` with `Task.FromResult` | `check-contracts` |
| `ImportError.Cancelled` and `ExportError.Cancelled`: the code `cancelled` is the `CallError` kind of an aborted call | renamed `Dismissed`; both stand for a closed dialog | `check-contracts` |
| `e.call` and `e.message` on a `CallError` (6.1 `onFailure` and `observe`, 6.2 `failure`) | the examples read only `kind`, `code` and `execution`; `message` and `call` removed from `CallError` in `sdk/core.ts`, and `call` from the internal `Outcome` | mistakes 19 and 20 (TS2339), a positive probe |
| `sdk/core.ts` built `protocol` / `unknown-error-case` with `execution: "completed"` | `execution: "unknown"`, as the table of the decision "`CallError` kinds" | 3 new routing checks |
| a subscription's `match` was said to take `success`, `events` and `failure` | the prose adds the usual `errors` (plan 2c). The stand-in types already accepted it: `Subscription<T, E>`. | a positive probe (`jobLog.tail`) and mistake 18 (TS2345) |
| the generated-module block had one-line interfaces, no header, and three shortened or missing JSDoc comments | one member per line, a header naming the C# source file, the C# summaries carried over unchanged | `tsc`, both versions |

Also changed in the stand-ins: `Spar.Stubs/Result.cs` lost `Match` and `Switch`. Nothing used them, and the decided `Result` API has no `Match` (plan section 2).

## Contract rules

`tools/check-contracts.mjs` matches text, not syntax trees, and relies on the document's layout. It stands in for the contract diagnostics (SPAR004, SPAR010, SPAR011, SPAR012, SPAR015) only until the analyzer exists. It checks that:

- a command takes zero or one request, which is not a list or an array, then an optional `CancellationToken`;
- a command returns `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` or `IAsyncEnumerable<T>`;
- a contract's name is not a JavaScript reserved word or a subpath of `@spar/app`;
- no error case is called `Cancelled`.

Result on the document of 2026-11-15: 7 contracts, 14 commands, 18 error cases, no rule broken.

Control: run against the committed document before the fixes (`git show HEAD:docs/app-model.md`, saved outside the repository), it exits 1 with these 5 findings:

```text
FAIL doc line 363: error case ImportError.Cancelled has the code 'cancelled', the CallError kind of an aborted call
FAIL doc line 427: contract ExportService is named 'export', a JavaScript reserved word
FAIL doc line 432: export.export takes 'IReadOnlyList<string>': a request is one positional record or one scalar, never a list
FAIL doc line 420: error case ExportError.Cancelled has the code 'cancelled', the CallError kind of an aborted call
FAIL doc line 769: crashReport.lastCrash returns 'CrashInfo?': a command returns a Task, a ValueTask or an IAsyncEnumerable
```

## C# results

All 13 blocks compile verbatim, with 0 warnings and 0 errors (CA2016 at error).

| Block (document line on 2026-11-15) | File |
|---|---|
| 55 | `SqlTool/Program.cs` (runs and returns 0) |
| 129 | `SqlTool/CopyJobs/CopyJobTypes.cs` (`closed record` accepted with C# 15) |
| 183 | `SqlTool/CopyJobs/CopyJobsService.cs` |
| 275 | `SqlTool/Connections/IConnections.cs` |
| 324 | `SqlTool/Monitoring/InstanceMonitor.cs` |
| 361 | `SqlTool/Recipes/RecipesService.cs` |
| 420 | `SqlTool/Recipes/CsvExportService.cs` |
| 544 | `SqlTool/Scripts/ScriptService.cs` |
| 625 | `SqlTool/Status/StatusService.cs` |
| 656 | `SqlTool/CopyJobs/CopyProgressReporter.cs` |
| 685 | `SqlTool/Monitoring/PowerAwareMonitor.cs` |
| 732 | `SqlTool/Monitoring/NetworkAwareMonitor.cs` |
| 768 | `SqlTool/Status/CrashReport.cs` |

- **The `Result` stand-in** (`Spar.Stubs/Result.cs`) is the union struct of the decision "The C# `Result` container is a union struct":
  - `Result<T, TError>` and `Result<TError>` are `readonly struct`s marked `[Union]`, with their union members on a nested `IUnionMembers` interface, so `Value` stays typed `T`.
  - `return plan;`, `return new PlanError.TableMissing(…);` and `return Result.Success;` are union conversions. They compile in `async` commands and outside `async` (`ExhaustiveChecks.Missing`, `ResultChecks.Forget`), and for `ConnectionError.NotFound` in `ConnectionsApi.TestAsync`.
  - Members: `IsSuccess`, `IsError`, `Value`, `Error`, `TryGetValue` and `TryGetError`. Every member except `ToString` throws on `default`.
  - Until the decision of 2026-11-14 the stand-in was a sealed class with two implicit conversions.
- A complete `switch` with no default arm compiles without CS8509 over `PlanError` (2 cases), `RunError` (3 cases), a `Result<CopyPlan, PlanError>` (the value and 2 cases) and a `Result<PlanError>` (`Success` and 2 cases).
- Output of `dotnet run --project SqlTool --no-build`, showing that the `Create` configuration ran:

  ```text
  window background: light #ffffff, dark #1f1f1f
  window size (1200, 800), minimum (800, 600), remembers placement True
  font-src additions: https://fonts.gstatic.com
  allowed permissions: ClipboardRead
  window kind settings: route /settings, modal True, single True, contracts IConnections
  window kind queryEditor: route /query, modal False, single False, contracts 
  tray: SQL Tool [Open (ShowWindow) | Pause monitoring [pause] -> IMonitor | - | Quit (Quit)], close to tray True
  service: Singleton IConnectionStore -> FileConnectionStore
  service: Scoped IConnections -> ConnectionsApi
  service: Singleton IMonitor -> InstanceMonitor
  ```

### Expected C# failures

`dotnet build SqlTool -p:SparCheckMissingCase=true` fails with exactly these two errors:

```text
SqlTool\Failing\ExhaustiveMissingCase.cs(9,61): error CS8509: The switch expression does not handle all possible
values of its input type (it is not exhaustive). For example, the pattern 'SqlTool.CopyJobs.PlanError.TableMissing'
is not covered.
SqlTool\Failing\FileDialogMissingCase.cs(9,88): error CS8509: The switch expression does not handle all possible
values of its input type (it is not exhaustive). For example, the pattern 'Spar.FileDialogError.Dismissed' is not
covered.
```

CS8509 is a **warning** by default. It is an error here only because of the repository's `TreatWarningsAsErrors`. On 2026-11-14, with `-p:TreatWarningsAsErrors=false`, the same build succeeded with `warning CS8509`; that run was not repeated on 2026-11-15. A consumer project without that setting would only get the warning, which is why SPAR055 was decided (decision "CS8509 stays the app's setting; a Spar rule for `Result` switches").

### DI abstractions: no package reference

The brief asked for `Microsoft.Extensions.DependencyInjection.Abstractions` through central package management. On `net11.0` that assembly is part of `Microsoft.NETCore.App`: it is in `packs/Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0/`, and restore lists the package in `packagesToPrune` with `(,11.0.32767]`. A direct reference fails under the repository policy. A probe on 2026-11-14 (in a disposable folder under `artifacts/`) showed it:

```text
error NU1510: Warning As Error: PackageReference Microsoft.Extensions.DependencyInjection.Abstractions will not be
pruned. This package is automatically available and does not need to be referenced explicitly. Remove the
PackageReference item.
```

So `Spar.Stubs` references no package. `IServiceCollection`, `ServiceCollection` and `AddSingleton`/`AddScoped` all come from the shared framework, and `Directory.Packages.props` here adds no versions. The finding is recorded in `docs/pitfalls.md`.

## TypeScript results

All 13 blocks pass on TypeScript 7.0.2 and 6.0.3, with 0 errors.

| Block (document line on 2026-11-15) | File |
|---|---|
| 476 | `frontend/src/notes.ts` |
| 505 | `frontend/src/drop.ts` |
| 587 | `frontend/src/printing.ts` |
| 809 | `frontend-generated/copyJobs.d.ts` (as `@spar/app/copyJobs`) |
| 921 | `frontend/src/copy.ts` |
| 988 | `frontend/src/CopyProgressView.tsx` |
| 1029 | `frontend/src/main.tsx` |
| 1053 | `frontend/src/actions.ts` |
| 1103 | `frontend/src/confirmDelete.ts` |
| 1126 | `frontend/src/outside.ts` |
| 1161 | `frontend/src/openWindows.ts` |
| 1205 | `frontend/src/search.ts` |
| 1230 | `frontend/src/AppTitleBar.tsx` |

The options are `strict`, `exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`, `noUnusedLocals`, `noUnusedParameters`, `verbatimModuleSyntax`, `isolatedModules`, `jsx: react-jsx` and `types: []`.

`probes/ok.ts` passes on both versions. It covers:

- `resume: "manual"` and `resume: "automatic"` on the `Automatic` subscription, and `resume: "manual"` on a `Manual` one;
- `match` without `errors` on `cancelAll`, and `match` on an awaited `Result`;
- a subscription that declares errors, read with `events` and `errors` (new on 2026-11-15);
- a `failure` handler that reads `kind`, `code` and `execution` (new on 2026-11-15).

`SubscribeOptions` in the stand-in:

```ts
export interface SubscribeOptions<R extends SubscriptionResume> extends CallOptions {
  readonly resume?: R extends "automatic" ? SubscriptionResume : "manual";
}
```

### Expected TypeScript failures (`bad/bad.ts`)

21 mistakes, each on one line. Both versions report one diagnostic per mistake (two for mistake 21), with the same code at the same position; the message text differs between the versions in places. Messages below are from tsc 7.0.2, shortened where marked with "…".

| # | Mistake | Code | Message (tsc 7.0.2) |
|---|---|---|---|
| 1 | `plan(...).match` without the `tableMissing` case | TS2741 | Property 'tableMissing' is missing in type '{ connectionNotFound: () => number; }' but required in type 'ErrorHandlers<PlanError>'. |
| 2 | misspelled case `tableMising` (and no `tableMissing`) | TS2561 | Object literal may only specify known properties, but 'tableMising' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'tableMissing'? |
| 3 | payload field `e.connection` on `connectionNotFound` | TS2551 | Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'? |
| 4 | `run(...).match` without `errors` | TS2345 | Argument of type '{ success: (r: CopyReport) => bigint; }' is not assignable to parameter of type 'Handlers<CopyReport, RunError, …>'. |
| 5 | `errors: {}` on `cancelAll` | TS2353 | Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: …; readonly failure?: … }'. |
| 6 | `resume: "automatic"` on a `Manual` subscription | TS2322 | Type '"automatic"' is not assignable to type '"manual"'. |
| 7 | `(await copyJobs.plan(recipe)).value` | TS2339 | Property 'value' does not exist on type 'Result<CopyPlan, PlanError, {}>'. |
| 8 | `copyJobs.plan(recipe).steps` | TS2339 | Property 'steps' does not exist on type 'Call<CopyPlan, PlanError>'. |
| 9 | a misspelled failure kind, `unavailabel` | TS2322 | Type '() => number' is not assignable to type 'never'. |
| 10 | a subscription read without handling `reset` | TS2322 | Type '{ gap: () => number; }' is not assignable to type 'SubscriptionEvents & { gap: () => number; }'. |
| 11 | a misspelled event, `gapp` | TS2322 | Type '() => number' is not assignable to type 'never'. |
| 12 | a subscription read without an `events` object | TS2345 | Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyProgress, {}, …, SubscriptionEvents, unknown>'. |
| 13 | a file dialog read without handling `dismissed` | TS2741 | Property 'dismissed' is missing in type '{ accessDenied: …; unavailable: …; busy: … }' but required in type 'ErrorHandlers<FileDialogError>'. |
| 14 | the same action id twice in one `define` | TS1117 | An object literal cannot have multiple properties with the same name. |
| 15 | a misspelled action in a menu, `findTabel` | TS2551 | Property 'findTabel' does not exist on type '{ readonly findTable: Action; }'. Did you mean 'findTable'? |
| 16 | a default button that is not one of the buttons | TS2322 | Type '"Keep"' is not assignable to type '"Cancel" \| "Delete"'. |
| 17 | a window kind that is not declared, `setings` | TS2345 | Argument of type '"setings"' is not assignable to parameter of type 'WindowKind'. |
| 18 | a subscription that declares errors, read without `errors` (new) | TS2345 | Argument of type '{ success: () => number; events: { reset: () => number; }; }' is not assignable to parameter of type 'Handlers<LogLine, LogError, …>'. |
| 19 | `e.message` on a `CallError` (new) | TS2339 | Property 'message' does not exist on type 'CallError'. |
| 20 | `e.call` on a `CallError`: the call is `contract` and `command` (decision "Bridge messages: encoding C header fields") | TS2339 | Property 'call' does not exist on type 'CallError'. |
| 21 | `dismissed: e => e` on a case without a payload, whose entry is `undefined` (decision "M1a names") | TS2322, TS7006 | Type '(e: any) => any' is not assignable to type '() => unknown'. Parameter 'e' implicitly has an 'any' type. |

For mistake 2, tsc reports only TS2561, not also TS2741 for the missing `tableMissing`. One diagnostic is enough to fail the build, and its message suggests the right name.

### Run-time routing (`tools/failure-routing.mts`)

18 of 18 checks pass under Node 24.21.0 with type stripping (rerun 2026-11-15 after `CallError` gained `contract` and `command`, decision "Bridge messages: encoding C header fields"; the newest check is that `observe` and `onFailure` both see `copyJobs.plan`). The 3 added earlier on 2026-11-15:

- an error case the SDK does not know becomes `{"kind":"protocol","code":"unknown-error-case","execution":"unknown"}`, now with the call's `contract` and `command`;
- that failure reaches `observe` and the call's `failure` function, not the interceptor;
- a declared error case still goes to its own handler.

## Document blocks that had to change

- **To compile:** none, in any run. No file under `Failing/` holds a document block, and no fixed copy was needed.
- **To follow the document's own rules:** the 7 blocks of the review of 2026-11-15 (`Program.cs`, `RecipesService.cs`, `CsvExportService.cs`, `CrashReport.cs`, `copyJobs.d.ts`, `copy.ts`, `CopyProgressView.tsx`). All of them compiled before the review. Compiling against stand-ins proves the syntax and the types, not the contract rules; hence `check-contracts`.

## Observations on the document (not compile failures)

- **Command names `import` and `export`** (`recipes.import`, `csvExport.export`) are JavaScript reserved words. They are legal as property names, and the plan reserves only contract names, so they were left. Whether the generator should refuse them is not decided.
- **Payload-less error cases** (`ImportError.Dismissed`, `TryAgain`, `CopyScriptError.ClipboardBusy`) are declared in C#, while their TypeScript interface-map entry is still open (`docs/app-model.md` section 7). The SDK stand-in writes such entries as `{}`.
- **A `CallError` does not name its call.** With only the decided fields, the app-wide `onFailure` and `observe` hooks cannot say which command failed. Listed as open in `docs/app-model.md` section 7.
- Resolved since the first run: `CopyProgress` now has a `JobId` field, and the `TimeSpan` wire format is decided (a string in the constant format), so `elapsed: string` is right.

## Not tested

- NativeAOT publication of `SqlTool`. The build ran the AOT and trim analyzers (`IsAotCompatible`) with no warnings, but there was no `PublishAot` run.
- The contract diagnostics themselves. No analyzer runs here: `check-contracts` is a text check of four rules, and a signature laid out differently from the document's could escape it.
- Runtime behaviour of the TypeScript beyond the 18 routing checks: everything else is type-checked with `noEmit`, and no transport or page was run.
- IDE behaviour (completions, quick fixes, go-to-definition) for these files.
- Lint rules such as `no-floating-promises`. Mistake 6 (`jobLog.follow(...)`, unused) and similar lines are not flagged by tsc itself.
- On 2026-11-15: `--listFiles` for the React typings, the `TreatWarningsAsErrors=false` variant of the missing-case build, and the NU1510 probe were not repeated.

## History

Earlier runs, kept because decisions cite them. Their counts were right on their day and are superseded by the figures above.

**First run, 2026-11-14.** 8 document blocks, all verbatim; 8 mistakes rejected. `Result<T, TError>` was then a sealed class with two implicit conversions, from `T` and from `TError`. `--listFiles` confirmed that `node_modules/@types/react-dom/client.d.ts` and `@types/react/jsx-runtime.d.ts` were used, and no stub for `react-dom/client` was needed.

**Rerun 2026-11-14, `Call` as a `Promise` subclass** (decision of that date). `sdk/c-call/sdk.ts` now has:
- `class Call<T, E> extends Promise<Result<T, E>>`, with `Symbol.species` = `Promise`, exported as a type only;
- a `Symbol.toStringTag` getter on `Result`;
- `sdk/app-sdk.ts` re-exports `Call` with `type`, because TS1448 refused a value re-export under `verbatimModuleSyntax`.

Results:
- `npm run check` passed: 8 of 8 blocks verbatim, 0 errors with TypeScript 7.0.2 and 6.0.3, and all 8 mistakes in `bad/` rejected with the same codes as before.
- Only types were checked. The page code was not run with the new class.

**Rerun 2026-11-14: failure handling** (decision of that date).
- **SDK changes:**
  - `sdk/core.ts` has the seven `CallError` kinds and the `stopped` execution.
  - `failure` accepts a function or an object by kind (`FailureByKind`), and misspelled kinds are rejected.
  - `FailureResults<F, K>` adds `undefined` for kinds left to the interceptor and for a missing `failure`.
  - `configureSpar({ onFailure?, observe? })`: `observe` sees every failure, and `unwrap` never calls `onFailure`.
- **Document:** the `docs/app-model.md` 6.1 block shows `observe` and a `failure` object.
- **Results** (`npm run check`):
  - 8 of 8 blocks verbatim;
  - 0 errors with TypeScript 7.0.2 and 6.0.3. `runCopy`, with a `failure` function, still types as `Promise<string>`, and `planCopy`, with a `failure` object, as `Promise<string | undefined>`;
  - 9 of 9 mistakes rejected. The new mistake 9, a misspelled failure kind, gives TS2322;
  - `tools/failure-routing.mts`, run by Node 24 with type stripping, passed 11 of 11 run-time checks of the routing: function form, listed and unlisted kinds, no handler, `unwrap`, success.

**Rerun 2026-11-14: the `events` slot** (decision of that date).
- **SDK changes:**
  - `SubscriptionEvents` and `CoalescedEvents` are handler interfaces with `reset` required.
  - `Result` takes a third type parameter for them, and `Subscription<T>` is the generated stream type.
  - The 6.2 example uses `events` instead of a `switch` inside `success`.
- **Results** (`npm run check`):
  - 8 of 8 blocks verbatim.
  - 0 errors with TypeScript 7.0.2 and 6.0.3.
  - 12 of 12 mistakes rejected. The new ones are 10 (no `reset`, TS2322 with the elaboration "Property 'reset' is missing"), 11 (a misspelled event, TS2322) and 12 (no `events` object, TS2345).
  - 14 of 14 run-time routing checks.

**Later on 2026-11-14: the native-service examples.** The document grew from 8 to 26 blocks and the mistakes from 12 to 17 as each native API was decided (the union-struct `Result`, tray, file dialogs, folders, drag and drop, clipboard, printing, resource usage, actions and menus, dialogs, shell, taskbar, power, windows, zoom and find, capture protection, network status, logs and crash reports). Each step's counts are in the decision that added it in `docs/decisions.md`; this file was not updated at the time.

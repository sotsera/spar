# app-model-check results

Checks that every C# and TypeScript block of `docs/app-model.md` compiles, verbatim, against stand-ins for the Spar API. Run on 2026-11-14.

**Outcome: all 8 document blocks compile verbatim. No block needed a change.** All expected failures fail with the expected codes.

## Tools

- .NET SDK 11.0.100. C# `LangVersion` 15.0, the default and the maximum. `net11.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` inherited from the repository's `Directory.Build.props`.
- Node 24.21.0, npm 11.19.0.
- TypeScript 7.0.2 (`typescript`) and 6.0.3 (`typescript6`, an npm alias). Both were installed from the offline npm cache with `npm install --offline --ignore-scripts`, together with `@types/react` 19.3.0 and `@types/react-dom` 19.3.0. Nothing was downloaded, and no stub for `react-dom/client` was needed.

## Layout

| Path | Contents | From the document? |
|---|---|---|
| `tools/extract-blocks.mjs` | Copies each `csharp`/`ts`/`tsx` block into the file named in the paragraph before it. Writes only changed files. `--check` fails if a copy differs from the document. | tool |
| `SqlTool/Program.cs`, `SqlTool/CopyJobs/CopyJobTypes.cs`, `SqlTool/CopyJobs/CopyJobsService.cs`, `SqlTool/Connections/IConnections.cs` | document blocks at lines 47, 85, 134, 228 | verbatim |
| `frontend-generated/copyJobs.d.ts` | document block at line 289, served as `@spar/app/copyJobs` through `paths` | verbatim |
| `frontend/src/copy.ts`, `frontend/src/CopyProgressView.tsx`, `frontend/src/main.tsx` | document blocks at lines 342, 396, 438 | verbatim |
| `Spar.Stubs/` | stand-ins for exactly the Spar C# API the document uses | no |
| `SqlTool/Support.cs` | `IConnectionStore`, `FileConnectionStore` (in memory), `ConnectionsApi : IConnections` | no |
| `SqlTool/ExhaustiveChecks.cs` | complete `switch` expressions over `PlanError` and `RunError` with no default arm, plus `Result` conversions outside `async` | no |
| `SqlTool/Failing/ExhaustiveMissingCase.cs` | a `PlanError` switch with one case missing. Compiled only with `-p:SparCheckMissingCase=true`. | no |
| `sdk/core.ts`, `sdk/c-call/sdk.ts` | unchanged copies of `spikes/ts-results/forms/core.ts` and `forms/c-call/sdk.ts` (checked with `diff`) | no |
| `sdk/app-sdk.ts` | `@spar/app/sdk`: re-exports form C and adds `SubscribeOptions<R>`, `ready()`, `setColorScheme()` | no |
| `frontend-generated/jobLog.d.ts` | a second, `Manual` subscription (`SubscribeOptions<"manual">`), used by `bad/bad.ts` | no |
| `probes/ok.ts` | positive controls for each mistake in `bad/bad.ts` | no |
| `bad/bad.ts` | mistakes that must fail. The `[TSxxxx]` tag on each numbered comment is the exact set of codes its next line must produce. | no |
| `tools/check-bad.mjs` | runs `tsc -p tsconfig.bad.json` and compares the diagnostics with the tags, line by line. It fails on a clean compile, a missing code or an extra code. | tool |

Neither the scripts nor the tsconfig files use absolute paths: paths are resolved from the script's location or from the tsconfig.

## Commands

From `spikes/app-model-check/`:

```text
node tools/extract-blocks.mjs --check                         8 blocks, 0 differ
dotnet build AppModelCheck.slnx                               Build succeeded. 0 Warning(s) 0 Error(s)
dotnet run --project SqlTool --no-build                       prints the recorded configuration, exit 0
dotnet build SqlTool -p:SparCheckMissingCase=true             Build FAILED: CS8509 (expected)
npm install --offline --ignore-scripts --no-audit --no-fund   added 6 packages
npm run check                                                 verbatim + tsc 7 + tsc 6 + bad (tsc 7) + bad (tsc 6)
```

The individual scripts are `check:ts7`, `check:ts6`, `bad:ts7` and `bad:ts6`. Each runs `node node_modules/<package>/bin/tsc`, because both packages provide a `tsc` bin.

## C# results

| Block | File | Result |
|---|---|---|
| line 47 | `SqlTool/Program.cs` | compiles verbatim; runs and returns 0 |
| line 85 | `SqlTool/CopyJobs/CopyJobTypes.cs` | compiles verbatim (`closed record` accepted with C# 15) |
| line 134 | `SqlTool/CopyJobs/CopyJobsService.cs` | compiles verbatim, with CA2016 at error |
| line 228 | `SqlTool/Connections/IConnections.cs` | compiles verbatim |

- `Result<T, TError>` is a sealed class with two implicit conversions: from `T` and from `TError`. `return new PlanError.TableMissing(…);` and `return new PlanError.ConnectionNotFound(…);` compile in the `async` `PlanAsync`. The derived case converts through the closed base in a single user-defined conversion. The same holds outside `async`, in `ExhaustiveChecks.Missing`, and for `ConnectionError.NotFound` in `ConnectionsApi.TestAsync`.
- A complete `switch` over `PlanError` (2 cases) or `RunError` (3 cases) with no default arm compiles without CS8509.
- Output of `dotnet run --project SqlTool --no-build`, showing that the `Create` configuration ran:

  ```text
  window background: light #ffffff, dark #1f1f1f
  font-src additions: https://fonts.gstatic.com
  allowed permissions: ClipboardRead
  service: Singleton IConnectionStore -> FileConnectionStore
  service: Scoped IConnections -> ConnectionsApi
  ```

### Expected C# failure

```text
SqlTool\Failing\ExhaustiveMissingCase.cs(9,61): error CS8509: The switch expression does not handle all possible
values of its input type (it is not exhaustive). For example, the pattern 'SqlTool.CopyJobs.PlanError.TableMissing'
is not covered.
```

CS8509 is a **warning** by default. It is an error here only because of the repository's `TreatWarningsAsErrors`: with `-p:TreatWarningsAsErrors=false` the same build succeeds with `warning CS8509`. A consumer project without that setting would only get the warning.

### DI abstractions: no package reference

The brief asked for `Microsoft.Extensions.DependencyInjection.Abstractions` through central package management. On `net11.0` that assembly is part of `Microsoft.NETCore.App`: it is in `packs/Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0/`, and restore lists the package in `packagesToPrune` with `(,11.0.32767]`. A direct reference fails under the repository policy. A probe in `artifacts/app-model-check/di-abstractions-reference/` (disposable) shows it:

```text
error NU1510: Warning As Error: PackageReference Microsoft.Extensions.DependencyInjection.Abstractions will not be
pruned. This package is automatically available and does not need to be referenced explicitly. Remove the
PackageReference item.
```

So `Spar.Stubs` references no package. `IServiceCollection`, `ServiceCollection` and `AddSingleton`/`AddScoped` all come from the shared framework, and `Directory.Packages.props` here adds no versions.

## TypeScript results

| Block | File | tsc 7.0.2 | tsc 6.0.3 |
|---|---|---|---|
| line 289 | `frontend-generated/copyJobs.d.ts` (as `@spar/app/copyJobs`) | pass | pass |
| line 342 | `frontend/src/copy.ts` | pass | pass |
| line 396 | `frontend/src/CopyProgressView.tsx` | pass | pass |
| line 438 | `frontend/src/main.tsx` | pass | pass |

The options are `strict`, `exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`, `noUnusedLocals`, `noUnusedParameters`, `verbatimModuleSyntax`, `isolatedModules`, `jsx: react-jsx` and `types: []`. `--listFiles` confirms that `node_modules/@types/react-dom/client.d.ts` and `@types/react/jsx-runtime.d.ts` were used. `probes/ok.ts` passes on both versions. It covers `resume: "manual"` and `resume: "automatic"` on the `Automatic` subscription, `resume: "manual"` on the `Manual` one, `match` without `errors` on `cancelAll`, and `match` on an awaited `Result`.

`SubscribeOptions` in the stand-in:

```ts
export interface SubscribeOptions<R extends SubscriptionResume> extends CallOptions {
  readonly resume?: R extends "automatic" ? SubscriptionResume : "manual";
}
```

### Expected TypeScript failures (`bad/bad.ts`)

Both versions give the same diagnostics:

| # | Mistake | Code | Message (tsc 7.0.2) |
|---|---|---|---|
| 1 | `plan(...).match` without the `tableMissing` case | TS2741 | Property 'tableMissing' is missing in type '{ connectionNotFound: () => number; }' but required in type 'ErrorHandlers<PlanError>'. |
| 2 | misspelled case `tableMising` (and no `tableMissing`) | TS2561 | Object literal may only specify known properties, but 'tableMising' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'tableMissing'? |
| 3 | payload field `e.connection` on `connectionNotFound` | TS2551 | Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'? |
| 4 | `run(...).match` without `errors` | TS2345 | Argument of type '{ success: (r: CopyReport) => bigint; }' is not assignable to … Property 'errors' is missing … |
| 5 | `errors: {}` on `cancelAll` | TS2353 | Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: …; readonly failure?: … }'. |
| 6 | `resume: "automatic"` on the `Manual` subscription | TS2322 | Type '"automatic"' is not assignable to type '"manual"'. |
| 7 | `(await copyJobs.plan(recipe)).value` | TS2339 | Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'. |
| 8 | `copyJobs.plan(recipe).steps` | TS2339 | Property 'steps' does not exist on type 'Call<CopyPlan, PlanError>'. |

For mistake 2, tsc reports only TS2561, not also TS2741 for the missing `tableMissing`. One diagnostic is enough to fail the build, and its message suggests the right name.

**Rerun 2026-11-14, `Call` as a `Promise` subclass** (decision of that date). `sdk/c-call/sdk.ts` now has:
- `class Call<T, E> extends Promise<Result<T, E>>`, with `Symbol.species` = `Promise`, exported as a type only;
- a `Symbol.toStringTag` getter on `Result`;
- `sdk/app-sdk.ts` re-exports `Call` with `type`, because TS1448 refused a value re-export under `verbatimModuleSyntax`.

Results:
- `npm run check` passed: 8 of 8 blocks verbatim, 0 errors with TypeScript 7.0.2 and 6.0.3, and all 8 mistakes in `bad/` rejected with the same codes as before.
- Only types were checked. The page code was not run with the new class.

## Document blocks that had to change

None. Every block compiles verbatim. No file under `Failing/` holds a document block, and no fixed copy was needed.

## Observations on the document (not compile failures)

- `CopyJobsService.ProgressAsync` builds `new CopyProgress(jobId, rows)`, so the job id fills the field named `Table`. It compiles, but the example would read better with a `CopyProgress(string JobId, long Rows)` or with a table name.
- C# `CopyReport.Elapsed` is a `TimeSpan`, while the TypeScript declaration has `elapsed: string`. The document already lists the `TimeSpan` wire type as Open.
- The NU1510 behaviour above would fit `docs/pitfalls.md`: on `net11.0`, do not reference `Microsoft.Extensions.*.Abstractions` packages directly. This check did not edit `docs/`.

## Not tested

- NativeAOT publication of `SqlTool`. The build ran the AOT and trim analyzers (`IsAotCompatible`) with no warnings, but there was no `PublishAot` run.
- Runtime behaviour of the TypeScript: everything is type-checked with `noEmit`, and no transport or page was run.
- IDE behaviour (completions, quick fixes, go-to-definition) for these files.
- Lint rules such as `no-floating-promises`. Mistake 6 (`jobLog.follow(...)`, unused) and similar lines are not flagged by tsc itself.

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

# Contract syntax spike: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/contract-syntax/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/contract-syntax) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Proves the decided contract syntax (`docs/decisions.md`, "Contract syntax") end to end in a source generator before M1:
both contract forms, the command shapes, derived and explicit names, `Result<T, TError>` with C# 15 `closed record`
errors, TypeScript scoping, and every diagnostic, including clashes with a referenced assembly.

Toolchain: SDK 11.0.100 (Roslyn 5.12, C# 15), generator built against Microsoft.CodeAnalysis 5.9.0 (netstandard2.0),
xunit.v3 4.0.3, TypeScript 7.0.2 (`tsc` borrowed read-only from `spikes/react/React.App/frontend/node_modules`).
No package was downloaded; everything came from the local cache. Nothing outside `spikes/contract-syntax/` was changed.

## What was built

| Path | Contents |
|---|---|
| `Spar.Runtime/` | `[SparContract]`, `[SparCommand]`, `[SparName]`, `Result<T, TError>`, JSON helpers, pull `SparStream` that owns the call's DI scope |
| `Spar.Generator/` | One analyzer assembly: `ContractReader` (per contract), `ClosedTypes` (`closed` detection), `ContractDiscovery` (local and referenced contracts), `ContractSet` (cross-contract checks), `CSharpEmitter`, `ClientEmitter` (manifest and TypeScript), `ContractGenerator` (`IIncrementalGenerator`), `ContractAnalyzer` (`DiagnosticAnalyzer`) |
| `Spar.Tool/`, `Spar.Tasks/`, `Spar.targets` | Client step from d1, reduced to client generation: inventory, then an MSBuild task (`TaskHostFactory`, `Runtime="NET"`) writing `obj/spar/client/` |
| `Sample.Contracts/` | Class library with a class contract, an interface contract, DTOs and a closed error. References only the runtime. |
| `Sample.App/` | Console app with local class and interface contracts using local and referenced types; `Program.cs` runs 27 checks through the generated dispatch; strict NativeAOT settings. `frontend/` holds a TypeScript consumer and a negative type test. |
| `Sample.Invalid/` | Not in the solution. Deliberately invalid contracts, for `dotnet build` and the design-time probe. |
| `Generator.Tests/` | 62 xunit.v3 tests: naming, every diagnostic with its location, the clash matrix, `closed` detection, in-memory dispatch round trips, one full output snapshot. |
| `ide-probe.cs` | File-based app: loads a project through `MSBuildWorkspace` (design-time build), runs its analyzers, edits in memory. |

The d1 copy was trimmed: subscriptions, the bridge message protocol (`SparCalls`), the frontend zip, the contract snapshot
targets and the `Exec`/`InProc` task modes were left out (see "Not tested").

## Supported syntax

### Contract forms

```csharp
// Class form: one instance per call, from the call's DI scope. AddSparContracts registers it as scoped.
[SparContract]                       // wire name "planning"
public sealed class PlanningService(CallScope scope) { … }

// Interface form: the app registers the implementation.
[SparContract("billing")]            // explicit name; derived would be "billingApi"
public interface IBillingApi { … }
services.AddScoped<IBillingApi, BillingApi>();
```

Both forms produce the same binding shape; only the resolved service type differs. `DispatchTests` runs the same seven
calls through a class and an interface contract and gets byte-identical results.

Every public, non-static, non-override method of a class contract is a command (every abstract method of an interface
contract). `[SparCommand]` only renames. This is my reading of the decision ("commands are derived from the method
name"), so a public helper on a contract class becomes a command or a diagnostic. Confirm before M1.

### Commands

```csharp
Task<Plan> GetAsync(string id, CancellationToken cancellationToken);            // scalar request: planning.get("p1")
Task<int> CountAsync();                                                         // no request, no token
Task<Result<Plan, PlanError>> BuildAsync(BuildPlanRequest request, CancellationToken cancellationToken);
IAsyncEnumerable<PlanStep> StepsAsync(int count, [EnumeratorCancellation] CancellationToken cancellationToken);
[SparCommand("total")] Task<Money> GetTotalAsync(Guid planId, CancellationToken cancellationToken);
```

- Zero or one request parameter, then an optional `CancellationToken` (it must be last when present).
- Request: a positional record or a scalar: `string`, `int`, `long`, `double`, `bool`, `Guid` (nullable allowed).
- Response: those, positional records, arrays and `IReadOnlyList<T>` of them.
- Return: `Task<T>`, `Task<Result<T, TError>>` or `IAsyncEnumerable<T>`.

### Names

| Declaration | Wire name |
|---|---|
| `class CopyJobsService`, `interface ICopyJobsService`, `interface ICopyJobs` | `copyJobs` |
| `class URLService` | `url` (leading acronym lower-cased as a unit) |
| `interface IOStatsService` | `oStats` (the `I` rule cannot tell; use an explicit name) |
| `[SparContract("jobs")]` | `jobs` |
| `GetPlanAsync`, `URLValueAsync`, `[SparCommand("explicitName")] SomethingAsync` | `getPlan`, `urlValue`, `explicitName` |

Explicit names must be camelCase ASCII identifiers. Contract names may not be JavaScript reserved words or `sdk`, `shared`,
`spar`. Contract names are compared ignoring case, because they become package folders.

### Errors

```csharp
public closed record PlanError
{
    public sealed record ConnectionNotFound(string ConnectionId) : PlanError;
    public sealed record TableMissing(string Connection, string Table) : PlanError;
    public sealed record Forbidden : PlanError;                  // no payload
    [SparName("accountLocked")] public sealed record Locked(string Account) : PlanError;   // renamed code
}

if (request.Connection != "main")
{
    return new PlanError.ConnectionNotFound(request.Connection);  // converts into Result<Plan, PlanError>
}

return plan;                                                     // so does the value
```

`Result<T, TError>` (`where TError : class`, a `readonly struct`) has implicit conversions from `T` and `TError`,
`Success`/`Failure`, `IsError`, `Value`, `Error` and `Match`. A case converts through its base: C# applies the reference
conversion and then the user-defined one, also inside `async` methods and target-typed conditionals.

Wire and TypeScript:

```text
{"ok":true,"value":{…}}
{"ok":false,"error":{"code":"connectionNotFound","connectionId":"other"}}
{"ok":false,"error":{"code":"forbidden"}}
```

```ts
export type PlanError =
  | { readonly code: "connectionNotFound"; readonly connectionId: string }
  | { readonly code: "forbidden" }
  | { readonly code: "tableMissing"; readonly connection: string; readonly table: string };

build: (request: BuildPlanRequest, options?: spar.CallOptions): Promise<spar.Result<Plan, PlanError>>
```

`spar.Result` is a plain stand-in (`{ ok: true; value } | { ok: false; error }`) in the stand-in SDK, for
`spikes/ts-results` to replace with the opaque shape.

### TypeScript scoping

- One package per contract: `packages/@spar/<contract>/index.ts`, plus `@spar/sdk`.
- A type used by two or more exported contracts (local or referenced) is declared once in
  `packages/@spar/shared/<Full.CSharp.Name>.ts`. Each contract package imports and re-exports it
  (`import type { Money } from "../shared/Sample.Contracts.Money.js"; export type { Money };`). Shared modules import
  only other shared modules: anything a shared type references is reachable from the same contracts.
- A type used by one contract is declared in that contract's package.
- Two different types with one TypeScript name in a contract's type closure (DTOs and errors together) is SPAR040.
  `[SparName("PlanItem")]` renames a type, and is the advised fix.

From the sample app: `Sample.Contracts.Money`, `Item` and `LookupError` are shared (used by `planning`, `billing`,
`inventory`, `pricing`); `Sample.App.Item` is `PlanItem` inside `planning`, next to the shared `Item`.

### Referenced assemblies

The app's generator also exports public contracts from referenced assemblies that reference `Spar.Runtime`. It
generates their dispatch and serializers in the app, and `AddSparContracts` registers their class contracts. The
library itself runs no generator.

## Diagnostics

All are errors, category `Spar.Contracts`. The analyzer reports every diagnostic that has a location in the
compilation, per document. The generator reports only those without one (problems inside referenced assemblies), so
nothing is reported twice. An invalid command is skipped alone; contract-level errors drop the contract.

| Id | Message (shortened) | Location |
|---|---|---|
| SPAR001 | Contract 'X' must be a sealed class or an interface. (non-sealed, abstract, static, record) | type name |
| SPAR002 | Contract 'X' must not be generic. | type name |
| SPAR003 | 'n' is not a valid contract / command name / TypeScript name / error code … | the attribute (`SparContract("Copy-Jobs")`), or the type or case for `[SparName]` |
| SPAR004 | Contract 'X' resolves to the name 'delete', which is reserved in JavaScript or by Spar. Give it another name with [SparContract("…")]. | type name, or the attribute when explicit |
| SPAR005 | Contracts 'App.PlanningService' and 'Lib.PlanningService' (in 'Lib') both resolve to the wire name 'planning'. Give one of them an explicit name with [SparContract("…")]. | each local contract; no location (from the generator) when all are referenced, with "… in its assembly" |
| SPAR010 | Command 'A' returns 'ValueTask<int>'. A command returns Task<T>, Task<Result<T, TError>> or IAsyncEnumerable<T>. (also `Task`, `void`, sync, streamed `Result`) | return type |
| SPAR011 | Command 'X' takes at most one request parameter, then a CancellationToken. Put the values in one record, … | the extra parameter |
| SPAR012 | The CancellationToken of command 'X' must be its last parameter. | that parameter |
| SPAR013 | Parameter 'p' of command 'X' cannot be ref, out, in or params. | parameter |
| SPAR014 | Command 'X' must not be generic. | method name |
| SPAR015 | The request of command 'X' is 'string[]'. A request is one record or one scalar … | parameter |
| SPAR016 | Commands Get, GetAsync of contract 'jobs' both resolve to the name 'get'. Give them distinct names with [SparCommand("…")]. (also overloads) | each method |
| SPAR020 | Type 'Dictionary<string, int>' is not a supported wire type. … (also `Result` outside a return type) | the property's parameter, the request parameter, or the return type |
| SPAR021 | Record shapes: not positional (an `init` property outside the constructor), abstract, generic, a closed error used as a value | record name or the stray property |
| SPAR022 | Record 'R' has no property for constructor parameter 'name'. | parameter |
| SPAR023 | Record 'R' has two properties with the JSON name 'url'. | the second parameter |
| SPAR030 | Error type 'E' must be a non-generic closed record with nested sealed cases, such as … | error type name; the command's return type when the type is in a referenced assembly |
| SPAR031 | Error case 'Outside' derives from 'PlanError' but is not nested in it. … | case name |
| SPAR032 | Error case 'PlanError.Open' must be a non-generic sealed record. | case name |
| SPAR033 | Error case 'Bad' cannot have a payload named 'message': 'kind', 'code', 'message' and 'execution' are reserved … | parameter |
| SPAR034 | Error cases NotFound, Missing of 'PlanError' both resolve to the code 'notFound'. Give one a distinct code with [SparName("…")]. | the later case |
| SPAR035 | Error type 'Empty' has no cases. … | type name |
| SPAR040 | Contract 'appPlanning' uses two different types named 'Item': 'App.Item' and 'Lib.Item'. Give one of them a TypeScript name with [SparName("…")]. | each clashing type declared in the compilation; else the contract; else none (generator) |

Diagnostics about a referenced contract start with "In referenced assembly 'L3': …" and carry no location.

`dotnet build Sample.Invalid` (observed; paths shortened):

```text
Invalid.cs(7,21): error SPAR005: Contracts 'Sample.Contracts.InventoryService' (in 'Sample.Contracts') and 'Sample.Invalid.InventoryService' both resolve to the wire name 'inventory'. Give one of them an explicit name with [SparContract("…")].
Invalid.cs(13,22): error SPAR040: Contract 'orders' uses two different types named 'Item': 'Sample.Contracts.Item' and 'Sample.Invalid.Item'. Give one of them a TypeScript name with [SparName("…")].
Invalid.cs(23,24): error SPAR030: Error type 'OpenError' must be a non-generic closed record with nested sealed cases, …
Invalid.cs(30,40): error SPAR033: Error case 'Failed' cannot have a payload named 'message': …
Invalid.cs(36,5): error SPAR010: Command 'CountAsync' returns 'System.Threading.Tasks.ValueTask<int>'. …
Invalid.cs(38,42): error SPAR011: Command 'FindAsync' takes at most one request parameter, then a CancellationToken. …
```

Design-time path (`dotnet run ide-probe.cs -- Sample.Invalid/Sample.Invalid.csproj`, observed):

```text
opened Sample.Invalid in about 1.5 s; analyzers: …, Spar.Generator, …
client step ran during the design-time load: False
[as loaded] 1293 ms
  generated: SparBinding.pricing.g.cs, SparBinding.reports.g.cs, SparCatalog.g.cs, SparSerializers.g.cs
  compiler errors: CS8652
  SPAR005 at Invalid.cs(7,21)   SPAR040 at Invalid.cs(13,22)  SPAR030 at Invalid.cs(23,24)
  SPAR033 at Invalid.cs(30,40)  SPAR010 at Invalid.cs(36,5)   SPAR011 at Invalid.cs(38,42)
[after an unsaved edit (Message renamed, BrokenAsync added)] 92 ms
  SPAR033 gone; SPAR013 at Invalid.cs(44,35) added; the other five unchanged
```

All diagnostics were in source. The `inventory` and `orders` contracts were dropped, and `reports` kept its valid
`fineAsync`. CS8652 appears because the probe's Roslyn 5.9 knows `closed` only as a preview feature (see Problems).

## How `closed` is detected

Probe (session scratchpad, Roslyn 5.9 in-process, `LanguageVersion.Preview`, then emit and reload as metadata):

| Fact | Source | Metadata |
|---|---|---|
| `ITypeSymbol.IsClosed` / `GetClosedDerivedTypeInfo(ct)` | true; cases `[ConnectionNotFound, TableMissing]`, `IsComplete` true | same |
| These APIs are `[Experimental("RSEXPERIMENTAL006")]` | using them is a build error without a suppression | |
| `IsAbstract`, `IsRecord` | true, true | true, true |
| `GetAttributes()` | no `IsClosedTypeAttribute` | still none: Roslyn hides it |
| Raw metadata | n/a | the type definition carries `System.Runtime.CompilerServices.IsClosedTypeAttribute` (a type reference into the BCL on net11.0) |
| Roslyn 5.9 at `LanguageVersion.Latest` | CS8652 "closed classes is in Preview" | |
| SDK 11.0.100 (Roslyn 5.12), default language version | compiles; no preview needed | |

The generator uses no experimental API (`ClosedTypes.cs`):

1. **Type with syntax** (this compilation, or a project reference in the IDE): closed when a declaration has a
   modifier whose text is `closed`. The token text is compared, not `SyntaxKind.ClosedKeyword`, because that enum value
   would be compiled into the generator while the host may load a newer Roslyn.
2. **Type from a metadata reference:** `compilation.GetMetadataReference(assembly)` gives a
   `PortableExecutableReference`. `GetMetadata()` → `ModuleMetadata.GetMetadataReader()`, find the type definition
   (nested types too) and look for `IsClosedTypeAttribute`.
3. **Cases:** the types deriving directly from the base, found by walking the base's assembly. A closed type can only
   be derived from in its own assembly. Derived types not nested in the base are SPAR031.

`ClosedDetectionTests` checks closed record, closed class, nested closed record, abstract record and sealed record from
source, metadata and project reference. The real build exercises path 2 with the SDK compiler: `Sample.Contracts.LookupError`
is read from `Sample.Contracts.dll` and serialized correctly by the app.

## Test results (observed)

- `dotnet test --project Generator.Tests`: **62 passed, 0 failed** (about 1.8 s).
  - Naming: 17.
  - Diagnostics: 16, each asserting the exact set of (id, located text).
  - Clash matrix: 23, most run with the library as metadata and as a project reference:
    - contract vs referenced contract, and the explicit-name fix;
    - two referenced assemblies (no location);
    - DTO vs referenced DTO, and the `[SparName]` fix;
    - the same name in different contracts (fine);
    - a shared referenced type emitted once and re-exported;
    - error vs referenced error, and DTO vs referenced error;
    - referenced closed and non-closed errors;
    - problems inside a referenced contract.
  - `closed` detection: 3.
  - Dispatch: 2. Class and interface forms are byte-identical through in-memory generated code; generated C# and
    TypeScript contain no `\r`.
  - Snapshot: 1. `Generator.Tests/Snapshots/RepresentativeApp.txt` holds all generated C# and TypeScript for an app
    plus a library.

  Every test also checks that the generated code compiles with no errors and no warnings.
- `dotnet build ContractSyntax.slnx` Debug and Release: 0 warnings, 0 errors.
- `Sample.App` on CoreCLR (Debug) and as the NativeAOT exe: **27 of 27 checks PASS**. The checks cover:
  - every command shape on both forms, local and referenced;
  - every error case, including a payload-less case, a renamed code and a referenced error;
  - a stream in batches;
  - one contract instance per call (2 calls → 2 instances) and one scoped dependency per call;
  - invalid payloads (wrong type, trailing content, missing required property) → `SparInvalidRequestException`;
  - unknown contract or command → `SparUnknownCommandException`.
- TypeScript 7.0.2, strict, with `verbatimModuleSyntax`, `isolatedModules`, `erasableSyntaxOnly`, `noUnusedLocals`,
  `noUnusedParameters`, `exactOptionalPropertyTypes` and `noUncheckedIndexedAccess`:
  - `frontend/consumer.ts`, using all five packages: **0 errors**.
  - `frontend/negative/negative.ts`: exactly the **4 expected errors**:
    - TS2322: the unhandled `forbidden` case is not assignable to `never`;
    - TS2551: `connection` on `connectionNotFound`;
    - TS2339: `value` read without checking `ok`;
    - TS2345: a number passed where the request is a string.
- Build integration:
  - The second unchanged build logs `Skipping target "SparGenerateClient"`.
  - Renaming `INotesService` to `[SparContract("memo")]` wrote `memo/` and deleted `notes/`; reverting restored it.
  - The design-time load did not run the client step.

## NativeAOT

`dotnet publish Sample.App -c Release`:

- Settings: `PublishAot`, `IsAotCompatible`, `TrimmerSingleWarn=false`, `IlcTreatWarningsAsErrors=true`, and the
  repository's `TreatWarningsAsErrors`.
- Run fresh (native folder deleted, `-nodeReuse:false -p:UseSharedCompilation=false`): **0 warnings, 0 errors**, no
  suppressions.
- `Sample.App.exe`: 2,578,944 bytes, including Microsoft.Extensions.DependencyInjection 11.0.0, five contracts and
  their serializers. All 27 checks pass.

## Problems found

1. **`closed` is experimental API in Roslyn 5.9** (RSEXPERIMENTAL006), and `GetAttributes()` hides the marker.
   Detecting it without a suppression needs syntax plus raw metadata reading. That works, but it duplicates what
   `GetClosedDerivedTypeInfo` does, and it walks an assembly's types per error type.
2. **Roslyn 5.9 hosts see `closed` as preview (CS8652).** The generator still reads the syntax, but:
   - an IDE or tool on Roslyn older than the C# 15 release shows a compile error on every `closed record`;
   - the client tool and the probe must parse at `LanguageVersion.Preview`.

   The "generators target the oldest supported IDE's Roslyn" policy therefore implies a minimum IDE that ships C# 15.
3. **Location-less diagnostics.** Problems inside referenced contracts and clashes between two referenced assemblies
   have no location in the app. They come from the generator and appear in the build and Error List without file:line.
   A contract library runs no analyzer of its own in this design.
4. **Cost per edit, not measured in a large solution.** For each compilation:
   - the analyzer re-reads every contract, including all referenced contract assemblies;
   - the generator's referenced-contract step runs on every compilation change;
   - `FindDerived` walks the whole assembly per error type.

   The probe's in-memory edit took 92 ms on a tiny project.
5. **Silent skips.**
   - Non-public contracts in referenced assemblies are ignored.
   - A missing DI registration for an interface contract fails only at call time, with `InvalidOperationException`
     from `GetRequiredService`.
6. **`Result` default.** `default(Result<T, TError>)` is a success holding `default(T)`.
7. **TypeScript clash messages are per contract.** One type clashing in two contracts gets two diagnostics at the same
   location, with different contract names.
8. **Non-generic `Task` is rejected (SPAR010)** as the decision lists only `Task<T>`. Commands with no result
   (`DeleteAsync`) then need a dummy value.
9. **The `I`-prefix rule is ambiguous** for acronyms: `IOStatsService` becomes `oStats`.
10. **Probe settings.** `ide-probe.cs` needs `ManagePackageVersionsCentrally=false` (`#:package` versions conflict with
    central package management) and `DisableMSBuildAssemblyCopyCheck=true` (MSBL001 from Microsoft.Build.Locator), as
    the d1 probe did. These are probe-only build properties, not analyzer suppressions.

## Observed, skipped, not tested

**Observed (this session, after the last code change):**

- 62 tests passing.
- Debug and Release solution builds with 0 warnings.
- 27 of 27 app checks on CoreCLR and NativeAOT.
- Strict AOT publish with 0 warnings.
- `tsc` positive (0 errors) and negative (4 expected errors) checks.
- Incremental skip of the client step, and stale package removal on rename. This was run before the message-wording
  change, which touched no emitted code.
- `dotnet build Sample.Invalid` showing 6 diagnostics with file:line:col.
- The `MSBuildWorkspace` design-time probe showing analyzer diagnostics in source and updating after an unsaved edit.

**Skipped:**

- Interactive IDE checks: squiggles in Visual Studio and VS Code, Error List refresh, and how location-less generator
  diagnostics display.
- Subscriptions (`[SparSubscription]`), the bridge protocol, cancellation outcomes and the host transports. None of
  these were copied.
- Error deserialization for a managed test client.

**Not tested:**

- Large-solution performance of the analyzer and generator.
- Whether the generator's syntax-provider transform re-runs when an error case is added in another file. The analyzer
  recomputes per compilation; the generator's output might lag in the IDE until the contract's file changes.
- Generic or nested contract types beyond the SPAR002 check.
- Enums, dictionaries and `DateTime`, which are rejected as unsupported.
- `ValueTask` support, which was deliberately rejected.
- The experimental `IsClosed` path inside the generator, which would need an agreed suppression.
- Two referenced assemblies defining the same full type name.
- Packaging. The pipeline ran from project references only.

## Recommendations for M1

1. Keep the analysis split used here:
   - one `ContractReader` per contract;
   - one `ContractSet.Analyze` for cross-contract rules;
   - an analyzer that reports located diagnostics per document;
   - a generator that reports only location-less ones.

   Generator, analyzer and client tool must share the code.
2. Detect `closed` with syntax plus metadata, as in `ClosedTypes`, unless the user agrees to suppress RSEXPERIMENTAL006.
   Either way, cache per compilation:
   - one walk building a base-type → derived-types map;
   - one metadata scan per referenced assembly.
3. Decide the minimum supported IDE and Roslyn from C# 15 support, not only from the generator reference. A Roslyn 5.9
   host reports CS8652 on `closed record`.
4. Decide three open syntax points:
   - Are all public methods commands, or is `[SparCommand]` required?
   - Is the `CancellationToken` optional?
   - Should a plain `Task` become `Promise<void>`? Recommended: yes.
5. Contract libraries: decide whether a library references the analyzer in a validation-only mode, so its own errors
   get locations, or whether the app alone validates it, as here. Also decide whether non-public referenced contracts
   should be a diagnostic rather than ignored.
6. Generate a startup check that every interface contract has a registration, or emit
   `AddSparContracts(services, configure)` that requires one.
7. Keep TypeScript scoping as built:
   - contract packages;
   - `shared/<Full.Name>.ts` for types used by two or more contracts;
   - SPAR040 inside a contract's closure, DTOs and errors together.

   M1's clash matrix can start from `ClashMatrixTests`, which covers metadata and project-reference views.
8. Treat the `I` and `Service` derivation as advisory. The names table shows where it guesses wrong; the analyzer could
   suggest an explicit name when the remainder starts with an acronym.

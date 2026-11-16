---
name: spar-contract-generation
description: Change Spar's contract analysis, the generated C# dispatch and serializers, the contract and `Result` diagnostics (SPAR001–SPAR055, SPAR100), the fingerprint and contract snapshot, or the generated TypeScript modules under `@spar/app` (one per contract, and `windows`).
---

# Change contract generation

The design is plan section 2 in `docs/design/contracts.md` (contracts, wire-rules table, diagnostic map, generated client) and 2c in `docs/design/bridge.md` (streams, subscriptions, failures); `docs/app-model.md` sections 3–6 show it compiled. In `docs/decisions.md` later entries supersede earlier ones. A change to the contract syntax, the wire or the generated client updates `docs/app-model.md` and its compiled check in the same change; the check is `tests/AppModel`, which `dotnet test` runs through `tests/AppModel.Tests`. Page-side SDK rules are in `spar-typescript-conventions`.

## Pipeline

- **The source generator owns all C#:** per-contract dispatch, the catalog with the fingerprint, and serializers written against `Utf8JsonReader`/`Utf8JsonWriter`. No System.Text.Json metadata, no reflection. One small async method per command; a shared one doubled async allocations.
- **The pre-compile client step owns no C#.** It writes the generated TypeScript (`.js` + `.d.ts` + a `.d.ts.map` to the C# declarations, under `obj/spar/client`) and the contract snapshot, from the same analysis library. There is no separate manifest (decision "No contract manifest for now; the differential tsc test built"). Never copy SDK logic (`src/Spar.Client`) into generated files.
- **Located diagnostics come from a `DiagnosticAnalyzer`** sharing the analysis; the generator reports only location-less ones (inside referenced assemblies), so nothing is reported twice.
- **The C# and TypeScript sides change together.**

## Contracts

- **Forms:** `[SparContract] sealed class` (one instance per call from the call's DI scope; only `[SparCommand]` methods are commands) or `[SparContract] interface` (every method is a command, the implementation comes from DI, `[SparCommand]` only overrides; the generated catalog lists interface contracts, and an app that registers no implementation does not start). A class implementing a contract interface exposes only its members. Mixing the forms, and non-method or static members or default implementations on a contract interface, are diagnostics.
- **Commands:** zero or one request (a positional record or a scalar), then an optional `CancellationToken`. Dispatch fills the scoped `ISparCall` before resolving the contract.
- **Returns:**
  - unary: `Task`, `Task<T>`, `Task<Result<T, TError>>`, `Task<Result<TError>>`, and the `ValueTask` forms;
  - streams: `IAsyncEnumerable<T>` or `IAsyncEnumerable<Result<T, TError>>`; yielding an error ends the stream and Spar disposes the iterator, and yielding after one is a diagnostic;
  - subscriptions: `[SparSubscription(Capacity, Resume)]`, `Resume` `Automatic` by default or `Manual` for side effects. A coalesced one (`Coalesce = true`, name settled in M3) keeps only the newest value, has no `gap`, and rejects `Capacity`.
- **Libraries:** an app serves a library's public contracts only when it lists the library, `<SparContracts Include="Lib" />` (decision "Contracts from referenced libraries: the app opts in"); `ContractProblems` discovers them for the analyzer and the client step, the generator reads the same list. Test each case with the library as a project reference and as metadata: a project reference has source locations outside the app's compilation.
- **Names:** classes keep their name, interfaces drop one leading `I`, both drop a trailing `Service`, then acronym-aware camelCase as in `JsonNamingPolicy.CamelCase` (`IOStatsService` → `ioStats`); commands drop `Async`. Overrides: `[SparContract("…")]`, `[SparCommand("…")]`, `[SparName("…")]` (types and error cases).
  - Explicit names are camelCase ASCII. Contract names compare ignoring case, since they become folders and subpaths.
  - Errors: two contracts with one name (referenced assemblies included), two commands with one name (overloads included), two types with one TypeScript name in a contract.
  - Reserved: JavaScript reserved words and every subpath `@spar/app` uses itself, today `sdk`, `vite`, `windows`, `telemetry`, `shared`, `spar`, and `testing` (the page-test helpers).
- **Errors:** C# 15 `closed record` hierarchies with nested sealed cases, never a union (CS0029). The code is the camelCase case name or its `[SparName]`; reserved payload names are `kind`, `code`, `message`, `execution`. A case without a payload is `{"code":"forbidden"}`; its TypeScript entry is `undefined`, with a handler that takes no parameter (decision "M1a names"). `RSEXPERIMENTAL006` is off for `src/Spar.Generator/**.cs` only, for Roslyn's closed-type API.
- **`Result<T, TError>` and `Result<TError>`** are Spar's `readonly struct` C# 15 unions. Generated code never creates or forwards a `default` result; one that reaches the writer is a `response`/`serialization` failure.
- **Wire types:** positional records, the primitives of the wire-rules table (all built; `byte[]` is one base64 value and may be a request; an enum is one member name, SPAR025 for `[Flags]`, aliased values or no members), nullable values (optional; non-nullable is required), arrays and `IReadOnlyList<T>`, nested records, `closed record` errors, and the opaque grants `FileGrant`/`FolderGrant` (the page sees only names). Extending the subset is a recorded contract decision.

## Wire and serializers

- **Implement the wire-rules table and the bullets under it exactly.** Easy to miss: `long`/`ulong` only as canonical decimal strings (`bigint` in TypeScript); `[Flags]` enums are a diagnostic and `DateTime` a warning suggesting `DateTimeOffset`; lone surrogates become U+FFFD both ways; void has no `value` key; a repeated declared property is a `JsonException`; unknown properties are skipped unvalidated.
- **Serializer requirements R0–R4** (decision "Serializer differential fuzzing"): compiles for every shape; trailing content rejected; every malformed input a `JsonException` with its byte offset; one depth limit, 64, for reading and writing; numbers overflowing to infinity rejected. The traps behind them are in `docs/pitfalls.md`, "Wire format and serialization".
- **Writer failures:** buffer each unary response and each stream item. On failure reset the pooled writer and send `kind: "response"`, `code: "serialization"`, `execution: "completed"`; a stream sends `"execution":"stopped","itemsSent":N`.

## Dispatch outcomes

- **Failures are the `CallError` kinds** of the decision "`CallError` kinds". Only dispatch's own refusals before the command runs are `rejected` / `notStarted` (`invalid-request`, `unknown-command`, `contract-mismatch`, `forbidden-command`); their exception types must not derive from ones a handler can throw (`InvalidOperationException`, `JsonException`).
- **A handler's exception is `failed` / `unknown`,** and so is an `OperationCanceledException` from another token. `cancelled` needs the command to end on its own call token. Nothing is retried.
- **No default deadline.** An optional C# limit, `[SparCommand(TimeoutSeconds = 30)]` (a positive integer), is folded into the call token.
- **Identical behaviour on every transport:** WebView2, WebKitGTK, loopback, and the test host's command and browser surfaces (plan section 5, `docs/design/testing.md`), whose typed C# test clients are generated per contract.

## Fingerprint and snapshot

- **The fingerprint hashes the full wire shape** (properties, types, nullability, error cases). A mismatch is `rejected` / `contract-mismatch` (409 on loopback) before any work.
- **`<Project>.contracts.snapshot`** is committed next to the project with LF endings: `Update` locally, `Verify` (SPAR100) under `ContinuousIntegrationBuild`. It is the one exception to "generated files live under `obj/`"; keep it in step with the generated surface.

## Generated TypeScript

- **One fixed `file:` package,** `"@spar/app": "file:../obj/spar/client"`, a subpath per contract. Never generate into `node_modules`.
- **The client step is `SparClient` in `src/Spar.Sdk.Tasks`** (decision "Client step: the generated TypeScript modules"): it runs `ContractAnalysis` and `CatalogEmitter.Fingerprint` on its own compilation of the project, so a change to either changes the client too. It owns the whole package folder: write-if-changed, stale files deleted. Its emitters are `DeclarationEmitter` (`.d.ts`, importing `@spar/app/sdk` by name) and `ModuleEmitter` (`.js`, importing `./sdk/sdk.js` and `./sdk/wire.js` relatively); `@spar/app/spar` exports the fingerprint.
- **Values go through the SDK's `wire` module,** never through logic copied into a module: `wire.field` (own properties only), typed readers, JSON-text writers, `wire.member`/`writeObject`. Error codes are read with a `switch`. `invoke` gets the request's encoder, so a value the encoder refuses ends `rejected` / `invalid-request`.
- **Names are checked by the analysis, written by the emitters, from one list:** `ClientNames` in `src/Spar.Generator` holds the SDK imports, reserved subpaths and words, the module's own names and the reader, writer and decoder names. SPAR003 is a name that breaks the rule, SPAR004 a reserved one, SPAR040 two types with one TypeScript name. `ContractProblems` reads every contract once and serves both reporters: the analyzer reports as you type, and the client step reports the same diagnostics in the build and fails before the frontend steps, writing nothing.
- **The task analyzer wants `AbsolutePath` all the way to each file API** (MSBuildTask0005 follows calls transitively): combine with `new AbsolutePath(relative, basePath)`, never `Path.Combine` on strings.
- **A contract module** (`docs/app-model.md` section 5): DTOs as `readonly` interfaces; each error type as an interface map (case name to payload, JSDoc from C#) with the union derived from it; one exported object with a `readonly` property per command. Unary commands return `Call<T, E>` (`Call<void, E>` without a value, no `E` without declared errors), streams `AsyncIterable<Result<T, E>>`, subscriptions `Subscription<T>` with `SubscribeOptions<"automatic" | "manual">`.
- **Types are scoped per contract module;** a C# type used by several contracts is emitted once and re-exported.
- **`@spar/app/windows`** declares the window kinds from `Program.cs` as a union of string literals, so a misspelled kind fails `tsc`. The build reads them as constants without running code (plan section 2, "`Program.cs`", decided 2026-11-15), like the CSP, the permissions and the window colours.
- **Build objects field by field,** never by copying a parsed object. Only the generated encoder writes `bigint`, and it checks `Number.isFinite`.
- **Validated in Spar's tests, never in the user's build,** by the three checks in plan section 2, "Generated client".

## Diagnostics

- **Categories and IDs:** `Spar.Contracts` for SPAR001–040 (map in plan section 2, "Diagnostics"), `Spar.Results` for SPAR050–055, SPAR100 for the snapshot, SPAR101–104 for the CSP analyzer. Errors, except SPAR051–054, the `DateTime` warning (SPAR024) and the suggestion for long-running commands that cannot observe cancellation. Decided rules without an ID yet: yield after an error, `Capacity` with coalescing, that suggestion. A warning leaves its command valid, and the client step stops only on errors.
- **One diagnostic per problem and location.** Messages about a referenced assembly start with "In referenced assembly 'X': …".
- **Skip only the invalid command,** never its contract's whole binding; the client step skips invalid contracts silently.
- **Track IDs** in `AnalyzerReleases.Shipped.md`/`Unshipped.md` (RS2008) instead of suppressing it.

## Output

- **Readable as hand-written code:** consistent indentation, blank lines between members and steps, no one-line blobs, stable order, C# docs carried into XML docs and JSDoc (a listed library's from the `.xml` beside its assembly, read by `XmlDocumentationFile`, since the task does not reference Roslyn's Workspaces), a header naming the source, small named helpers, shared logic in the runtime or SDK. Snapshot tests review it.
- **`\n` line endings only,** never `AppendLine` or `Environment.NewLine`.
- **Write only on change,** delete only owned outputs (`spar-msbuild`), and fix output in its generator.
- **The parsers use the Roslyn they reference;** a contract in syntax it does not know is skipped without a diagnostic. Raise it with the supported C# version (pitfalls, "Build, MSBuild and packages").

## Verify

- **Generator tests:** every valid shape and every diagnostic with its location; the name-clash matrix with contracts, DTOs and errors from a referenced assembly (project reference and metadata); output snapshots; an error case added in another file updates the output.
- **Build test:** adding, renaming and removing a contract leaves no stale module or member (`spar-msbuild`, `dotnet-msbuild` skills).
- **Runtime:** a CoreCLR round trip through the affected dispatch path; a strict NativeAOT publish when serialization changes; updated shared conformance vectors when the wire changes.
- **Consumer:** a real TypeScript consumer compiled against the regenerated client across the `tsconfig` × TypeScript 6/7 matrix.
- **Differential:** a change to the generated `.js` or `.d.ts` changes the typed twin in `tests/Spar.Sdk.Tasks.Tests/TypeScriptTwin.cs` too, written on its own rather than by calling the emitters, and `DifferentialTests` compares what `tsc` writes from it with what the client step wrote (tokens, so layout and comments do not count). A new wire shape goes into a fixture there.

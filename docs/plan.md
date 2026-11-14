# Spar v7 plan

Revised 2026-11-13 (second pass the same day, after the M0 spikes and the design discussions), and corrected 2026-11-14 after a review of the spikes. It replaces the first draft of 2026-11-12, which is in git history. Evidence and reasons are in `docs/decisions.md`; surprising behaviours are in `docs/pitfalls.md`.

## Goal

Spar is a C# framework for desktop apps with a web frontend: WebView2 on Windows and WebKitGTK on Linux. The backend is C#, published by default as one NativeAOT executable on .NET 11. C# contracts generate the TypeScript client, so the frontend calls the backend through typed functions.

Spar is built for agents first: **an agent should be able to make a change, verify it, and know which build it is looking at.** People use the same tools.

The first real consumer is a SQL Server tool (monitor instances, compare databases, copy data with saved recipes). It is built as a separate app that uses Spar only through its packages.

## Inputs to this revision

- **M0 spikes** (`spikes/`, moving to `docs/evidence/`):
  - the compile pipeline (D1), MSBuild tasks, and IDE behaviour;
  - D8 automation, loopback, the React app, the desktop host;
  - serializers (benchmark and fuzzing), packaging, test frameworks, Aspire;
  - SqlClient (AOT, tool features, Linux), native services, CsWinRT, `.esproj`;
  - bridge streaming and push, contract syntax, TypeScript results and compiled contracts;
  - the Linux host, asset packing, OpenTelemetry.
- **The earlier v7 attempt** (`old/spar.v7`):
  - agent-first framing, and a settled-decisions table;
  - the three call outcomes, and "a request belongs to a document";
  - a test level that runs in a browser without the desktop host;
  - file-size limits, analyzer settings, and portable skills.
- **The WebView2 investigation runbook:** host milestones, a readiness message, the teardown checklist, failure recovery, and how to choose a diagnostic tool.
- **External research:** .NET 11 MSBuild, the .NET skills catalogue and its evaluation format, Aspire 13, CsWinRT 3.0, and agent tooling in other UI frameworks.

## Settled decisions

Details and evidence in `docs/decisions.md`.

| Decision | Reason |
|---|---|
| Two desktop hosts from the start: Windows (WebView2) and Linux (WebKitGTK). The core is host-neutral; each host supplies its channel, transport, origin checks, automation and native services. | User decision. The Linux spike worked on both WebKitGTK APIs, and the SQL tool's data layer matched Windows on Linux. |
| NativeAOT by default. Per app, the author may publish on CoreCLR or accept their own AOT warnings. Spar's own code stays warning-free. Never fall back silently. | User decision. SqlClient is not AOT-clean (49–53 ILC diagnostics) but works with them accepted. |
| .NET 11 (SDK 11.0.100), C# 15. `InvariantGlobalization=true` by default, the NativeAOT default; an app turns it off in its own project file (+260,096 B on the minimal baseline app of `spikes/sqlclient-aot`). SqlClient needs it off, so the SQL Server tool turns it off; Spar itself never does (user, 2026-11-14). | Current release; `closed` hierarchies; measured cost. |
| Plain `dotnet build`, `run`, `test` and `publish`, plus IDE builds, work without Spar tooling. The CLI build is the source of truth after external edits. | Agents and people use tools they know; neither IDE regenerates on external edits. |
| A source generator emits all contract C#: dispatch, catalog and serializers. A pre-compile step emits only the generated client. They share one analysis library, and a `DiagnosticAnalyzer` reports located diagnostics. | D1 and the IDE checks: live diagnostics with squiggles, no build needed first. |
| Wire serialization: generated `Utf8JsonReader`/`Utf8JsonWriter` code, with the fuzzing requirements (R0–R4). | Faster to read, 47% smaller than System.Text.Json metadata; at least as fast to write once tuned. |
| Spar build steps are compiled, thread-safe MSBuild tasks (`Runtime="NET"`). Checked by `spikes/msbuild-task-analyzer` (SDK 11.0.100 = MSBuild 18.12.2): the task project references `Microsoft.Build.TaskAuthoring.Analyzer` 18.13.0 (`PrivateAssets="all"`; it runs, about 35 ms, no build-time difference) with Framework and Utilities.Core 18.12.2 as compile-only references. Tasks take the full thread-safe shape now: `[MSBuildMultiThreadableTask]`, `IMultiThreadableTask`, and a `TaskEnvironment` constructor. Under `-mt` only tasks with the attribute stay in-process (32 projects: 255–262 ms against 474–514 ms with `-m:8`); others move to TaskHost processes. The analyzer misses static mutable state, so that stays a review and test rule; its code fixes are not shipped. Its option and three severity raises are agreed and in the root `.editorconfig` (user, 2026-11-14). `TaskHostFactory` is used only in Spar's own checkout. Variant builds run in-proc through the `MSBuild` task, with per-variant `obj`/`bin`, `RemoveProperties`, and a cross-process lock. | Measured: 7–30 ms warm steps; in-proc variants 1.6–2.2 s faster than `Exec` per call. |
| One `SparVariant` property selects the build variant. | The earlier mode-flag matrix was the main source of fragility. |
| A call has three outcomes: success, a declared typed error, or **outcome unknown**. Cancellation is reported only when the backend confirms it. No automatic retries; subscriptions resume only when their contract allows it. | A lost reply must never be read as "didn't happen". |
| Results are opaque in TypeScript: `await cmd(req).match({ success, errors, failure? })` or `unwrap()`. `failure` is a function (every failure) or an object by `CallError` kind (only those kinds). Other failures go to the app-wide interceptor `onFailure`, and `match` returns `undefined` for them. `unwrap` throws and never reaches the interceptor. `observe` sees every failure. Neither hook can retry or change a result (2026-11-14). | User decision; checked with `tsc` and both language servers. |
| One DI scope per command, from Microsoft DI (`Microsoft.Extensions.DependencyInjection`; user decision 2026-11-14, +0.3–0.9 MB in AOT). Every request belongs to one document, with sender checks. On navigation or close, stop admitting work, cancel, and drop late replies. | Proven in every spike. |
| The desktop origin is `https://app.localhost` on Windows and `spar://app/` on Linux, the same for every app, and never changes between versions. | User decision. `.local` added about 2 s per load after a network change; `app.localhost` loaded in 54 ms and needs no name resolution. The profile is already per app, so one name for every app mixes no data. |
| Isolated data and WebView profiles per test run, under `artifacts/`. Wait for the browser to exit before reusing or deleting a profile. | Repeatable tests; the browser sometimes takes about 6.5 s to exit. |

## Open questions

| Question | State, and where it is answered |
|---|---|
| Bridge transport per host. | Leaning: `postMessage` for everything on Windows; `fetch` unary plus one channel per document on loopback; Linux measured (both work). Confirm in M1. |
| Test framework design: what Spar's test framework offers so an app is simple to test (levels, fakes, scenes, clock, host automation, assertions). | User topic (2026-11-14). The spike review is done (`docs/evidence/test-framework-review/`) and the proposal is in section 5; its points are being decided with the user. First review every spike for its test lessons: what was hard to test, which harnesses and fakes each one built, what was flaky or slow, what needed the user. Then discuss with the user what the framework can and should implement. Includes performance regression tests (user, 2026-11-14): BenchmarkDotNet suites for runtime hot paths (dispatch, serializers, bridge codec) and a build-time suite for clean, warm no-op and incremental builds, run against stored baselines so a new feature that slows things down is caught; which of them run in CI, and how noise is handled, is part of the discussion. Also includes how parallel tests and test processes create WebView2 environments and profiles under `artifacts/webview2-profiles/` without clashing (one folder per process or a shared folder with identical options, a lock, cleanup of folders left by crashed runs). Builds on section 5 and D7. |
| API design for native features (tray, custom title bar, close to tray, placement, file open and save, folder picker, drag and drop, clipboard, print and print to PDF, keyboard shortcuts, single instance, resource usage; notifications later): as easy and pleasant as possible in C# and TypeScript. | User requirement (2026-11-14). Principles and first ideas in section 3, "Native services". Designed with the user one feature at a time, each with a compiled example in `docs/app-model.md` before implementation. |
| Profiles and shutdown: one user data folder with a profile per window or per instance, or separate folders; InPrivate; how an app setting clears its browsing data; fresh profiles for tests; whether the host ever waits for the browser to exit. | User topic (2026-11-14), to discuss after `spikes/webview-profiles`. The user's reading matches Microsoft's documentation: `Close` is synchronous, the browser exits once no WebView uses it, and the documentation sets no wait before the host exits. Waiting for `BrowserProcessExited` is needed only before deleting the user data folder: tests, or a "clear data" setting implemented as a deletion. So the 6.5 s exits matter only there. Microsoft recommends profiles under one folder over several folders; each profile has its own cookies, preferences and cache. `Profile8.Delete` and `ClearBrowsingData*` exist. The documentation is silent on where InPrivate data lives, on a host that exits without waiting, and on a quick relaunch while the old browser is still exiting: the spike measures these. Linux equivalents (WebKitGTK network sessions) are checked if time allows.
**Measured by `spikes/webview-profiles`** (WebView2 157, WebKitGTK 2.52 on 6.0 and 4.1; 330 Windows and 320 Linux shutdown runs):
<ul>
<li>Exiting without waiting lost no data on either OS, even with the host killed. Every browser process exited.</li>
<li>The 6.4 s exits are SmartScreen: a close within about 0.5 s of a navigation, with its reputation request still in its TLS connect. With `IsReputationCheckingRequired = false` the exit took 52–67 ms; on Linux, 3–24 ms.</li>
<li>`Close` does run `beforeunload`, `unload` and `pagehide` on this Runtime.</li>
<li>A relaunch works with no special handling. A second instance with different options fails with `0x8007139F`. On Linux a second instance on the same folders starts silently and loses `localStorage`.</li>
<li>Extra windows: same profile +26 MB, another profile +30 MB, InPrivate +27 MB, another folder +149 MB.</li>
<li>`ClearBrowsingDataAll` clears in about 70 ms. `Profile8.Delete` poisons the name for good (`ERROR_DELETE_PENDING` at every later start).</li>
<li>Test profiles: InPrivate costs 76–79 ms per test; ephemeral data sets on Linux 60–69 ms.</li>
</ul>
**Decided (user, 2026-11-14), all of these:**
<ul>
<li>exit without waiting, and wait for `BrowserProcessExited`, with a timeout, only before deleting or reusing the folder;</li>
<li>single instance enforced before the environment is created, which Linux needs for data integrity;</li>
<li>one folder per app, with windows on the default profile and named profiles only for separate data;</li>
<li>"clear data" through `ClearBrowsingDataAll` / `webkit_website_data_manager_clear`, never `Profile8.Delete`;</li>
<li>InPrivate or ephemeral data sets for in-process tests. The user data folder those tests still need lives under `artifacts/webview2-profiles/` (AGENTS.md), never versioned, so cleanup knows where to look. How parallel tests and test processes share or split those folders without stepping on each other is part of the test-framework topic;</li>
<li>**Decided (user, 2026-11-14):** SmartScreen (`IsReputationCheckingRequired`) is off while the navigation lock keeps the page on the app origin, and on automatically when the app allows navigation to other origins from C#. An explicit C# override exists both ways. A level-2 security default (section 3c).</li>
</ul> |
| Adopting the in-process test factory (20–27 ms starts, shared console and statics). | Part of the test-framework discussion (section 5). An approval on 2026-11-14 was withdrawn by the user the same day, to first review the list of test types. |
| Agent devtools scope (Linux has no CDP), and CLI against MCP. | M2 evaluation. |
| Linux CI: now needed from M1 (Xvfb in a container). | M1. GTK4 needs a non-root user and user namespaces for its sandbox. |
| C# Hot Reload: applied in 0.9–3.9 s in the spikes; contract changes restart. | M1 dev-loop step. |
| A Spar-owned minimal OTLP exporter. | Parked. |
| Topics from the review of earlier versions and Tauri: bridge limits and admission, two-stage close, startup failure UI, single instance, app data folders, the SQL tool's desktop features, test clients and fixtures, devtools rules. | `docs/research-review.md`; to be discussed and moved into this plan item by item. |

## Design

### 1. Build and packaging

- **Packages:**
  - `Spar.Sdk`, one MSBuild project SDK for app and test projects: `<Project Sdk="Spar.Sdk">`, version pinned in `global.json` (`msbuild-sdks`). It ships the generator, the analyzer, the task assembly and the compiled TypeScript SDK, each once.
  - Runtime packages: `Spar` (core), `Spar.Windows`, `Spar.Linux`.
  - **Choosing platforms (decided, user, 2026-11-14):**
    - An app sets `<SparPlatforms>windows;linux</SparPlatforms>`, or one of the two; both is the default.
    - `Spar.Sdk` adds the host packages at its own version, so apps never reference `Spar.Windows` or `Spar.Linux` themselves, and `dotnet new spar-app --platforms …` writes the property.
    - Each platform maps to its runtime identifiers (`win-x64`/`win-arm64`, `linux-x64`/`linux-arm64`), and NativeAOT removes the other host's code from each executable.
    - Running on a platform the app does not target stops with a clear error that names the property.
    - NativeAOT does not cross-compile between operating systems, so each executable is published on its own OS or in a container of it.
  - Testing packages per test level.
  - The `spar` .NET tool, versioned with the SDK.
- **Package rules:**
  - Never reuse a version.
  - The runtime package is referenced implicitly at the SDK's version.
  - The SDK version is an input of the client step.
  - NuGet's `analyzers/` folder is ignored for MSBuild SDK packages, so the generator is added as an `Analyzer` item from `Sdk.targets`.
- **Pipeline:**
  1. `ResolveReferences`.
  2. Contract inventory, written only when it changed.
  3. Client step: the generated modules under `obj/spar/client` (`.js`, `.d.ts`, `.d.ts.map` to C#), the manifest, and the contract snapshot.
  4. Frontend: the type-check in every variant (Debug included, see below); in Release and test variants also `npm ci --ignore-scripts` when the lockfile changed (section 3c), `vite build` into `obj/`, then packing.
  5. One `CoreCompile`, where the generator and analyzer run.

  Design-time builds run none of steps 2–4.
  - **Debug type-checks too (decided, user, 2026-11-14),** with the opt-out `SparTypeCheck=false` (name to confirm in M1). It runs only when the generated client or the TypeScript sources changed since the last check, and reports errors at file and line, so a contract rename breaks `dotnet build` and an agent sees it. The app's `tsconfig` decides how strict it is. The measured baseline (`spikes/react`, target `SparDevTypeCheck`) cost about 1 s of a contract restart and gated the build (TS6133 failed it).
- **Frontend packing:** one `Spar.Frontend` resource holding an index plus per-file Brotli (quality 11, fitted window).
  - Files that Brotli shrinks by less than 5% are stored as is.
  - The host decodes on a background thread at start into a cache, and serves uncompressed entries straight from the image.
  - Small bundles may pack uncompressed, with a feature switch that trims the decoder.
  - No zip.
  - Measured:
    - the decoder adds about 168 KB on both OSes, statically linked;
    - a 2.8 MB bundle decodes in about 5 ms and makes the exe 44% smaller than raw;
    - neither WebView decodes `Content-Encoding` itself.
  - Deflate was measured only inside the zip container: its payloads were about 15% larger than Brotli's (React 71,240 against 61,176 B; the 2.8 MB bundle 1,006,299 against 875,155 B). Per-file Deflate inside the same index, without the zip reader, was not measured, so its decoder cost is unknown.
- **Assets are served with `Cache-Control: no-store`.** Debug builds embed no bundle (see section 4).
- **Determinism:** write-if-changed outputs, with stamp files as MSBuild `Outputs`. Exclude every `bin/`, `obj/`, `node_modules` and `dist` from the default globs.
- **Multithreaded MSBuild:** every task is safe for `-mt`, and a build-test lane runs `-mt`.
- **A package-only consumer test from M1's first day:** an isolated feed, package folder and HTTP cache (`NUGET_HTTP_CACHE_PATH`), outside any repository folder, covering update and downgrade while warm.

### 2. Contracts and the generated client

- **Contracts:** an attributed class (`[SparContract]`; only `[SparCommand]` methods are commands; one instance per call from the DI scope), or an attributed interface (every method is a command; implementation from DI). Mixing the two is a diagnostic.
- **`Program.cs`** (proposal from `docs/app-model.md`; not yet confirmed by the user): exactly one `SparApplication.Create` and one `Run`, directly in the top-level statements, so the build can read the constant declarations of sections 3b, 3c and 3d without running code. Anything else is an analyzer error at its location.
- **Commands:**
  - Zero or one request (a positional record or a scalar), then an optional `CancellationToken`. The scalars accepted in `spikes/contract-syntax` were `string`, `int`, `long`, `double`, `bool` and `Guid`, nullable allowed.
  - Returns: `Task`, `Task<T>`, `Task<Result<T, TError>>`, `Task<Result<TError>>`, their `ValueTask` forms, `IAsyncEnumerable<T>`, `IAsyncEnumerable<Result<T, TError>>` (a stream that declares errors: yielding an error item ends it, Spar disposes the iterator, and a diagnostic flags code that yields after the error; decided 2026-11-14), or a `[SparSubscription(Capacity, Resume)]` subscription.
  - The scoped `ISparCall` (token, call id, names) is injectable.
- **Names:**
  - Derived: a class keeps its name, an interface drops one `I`, both drop `Service`, then acronym-aware camelCase (`IOStatsService` → `ioStats`). Commands drop `Async`.
  - Overrides are `[SparContract("…")]`, `[SparCommand("…")]` and `[SparName("…")]`.
  - Duplicates are errors, including contracts from referenced assemblies.
  - Explicit names are camelCase ASCII identifiers. Contract names are compared ignoring case, because they become folders.
  - **Reserved contract names:** JavaScript reserved words, and every subpath that `@spar/app` uses itself. The spike reserved `sdk`, `shared` and `spar`; the decided layout also has `vite` (below), `telemetry` (section 6b) and `windows` (decision "Secondary and modal windows"), which a contract must not take either, and `testing` if section 5 adopts it. The list follows the SDK's subpaths.
- **Errors:** C# 15 `closed record` hierarchies with nested sealed cases. Union conversions into `Result` (`return new PlanError.X(…)`). The wire shape is `{"ok":false,"error":{"code":"x",…}}`, with reserved payload names (`kind`, `code`, `message`, `execution`). Roslyn's experimental closed-type API is allowed in the generator (RSEXPERIMENTAL006, agreed).
  - A case without a payload (`sealed record Forbidden : PlanError;`) is `{"code":"forbidden"}`. `[SparName("…")]` on a case renames its code. The TypeScript interface-map entry for a payload-less case is not defined yet.
  - **The C# `Result<T, TError>` API is decided:** the union struct below (decision "The C# `Result` container is a union struct"). Earlier state, the first spike's (`spikes/contract-syntax/Spar.Runtime/Result.cs`): a `readonly struct` with `where TError : class`, implicit conversions from `T` and `TError`, `IsError`, `Value` (throws on an error), `Error`, `Success`, `Failure` and `Match`. There `default(Result<T, TError>)` is a success holding `default(T)`, so a forgotten assignment reads as success.
  - **Decided (user, 2026-11-14): the union struct**, from `spikes/result-api`. The user prefers allocating as little as possible, and the class form had no other advantage worth an allocation per result. `docs/app-model.md` and its check use it.
  - **Analyzer rules for default results** (`spikes/spar050`; category `Spar.Results`; SPAR050–SPAR054 adopted and SPAR055 added, decisions "Analyzer rules SPAR050–SPAR054 adopted" and "CS8509 stays the app's setting; a Spar rule for `Result` switches"):

    | ID | Severity | Catches |
    |---|---|---|
    | SPAR050 | Error | Definite defaults: `default`, `new()`, structs holding a `Result`, `= default` parameters, `this = default`, `Activator` and `Unsafe.SkipInit`, `new()`-constrained type arguments |
    | SPAR051 | Warning | APIs that may return default: `FirstOrDefault` and similar, `GetValueOrDefault`, `List.Find`, a `TryGetValue` value read on the failing path |
    | SPAR052 | Warning | Storage that starts as default: arrays without an initializer, `Array.Resize`/`Clear`, `Span.Clear`, `ArrayPool.Rent`, `CollectionsMarshal` |
    | SPAR053 | Warning | A field or auto-property not assigned in every constructor, like CS8618 |
    | SPAR054 | Warning | A generic method in source that produces `default(T)`, called with a `Result`. Reported at compilation end, so it may need full-solution analysis in IDEs. |
    | SPAR055 | Error | A `switch` expression over a `Result` (or a Spar contract's `closed` error hierarchy) that misses a case, reported next to CS8509 so it stays an error even where `TreatWarningsAsErrors` is off. Decided by the user, 2026-11-14; not yet prototyped. Whether it also covers `switch` statements without a `default`, which silently do nothing for a missing case, is decided when it is built. |

    - **Coverage:** all 6 original cases, and 117 of 131 default-producing cases in a corpus of 199 (89 %, against 25 % for the prototype).
    - **False positives:** 0, on 57 clean cases and on the app-model check.
    - **Cost:** 31–63 ms per build on 200 files, and about 2,100 lines of analyzer plus a code-fix assembly. There are 10 code fixes.
    - **Left to the run-time guard (14 cases):** library or generated code built without the analyzer, early virtual calls from constructors, and APIs where .NET throws first.
    - **Decided (user, 2026-11-14): adopt SPAR050–SPAR054.**
      - No CS0649/CA2019 severity override. SPAR053 also reports a never-assigned field when the field holds a `Result`, alongside the compiler's warning, so fields of other types are unaffected.
      - No `#pragma` in source. Spar's own tests of the run-time guard turn SPAR050 off through an `.editorconfig` section scoped to those test files, with its reason recorded. The rest of the test code keeps the analyzer.
    - The contract-generation skill lists both categories, `Spar.Contracts` and `Spar.Results` (decision "Guidance review applied").
    - **The type:** a C# 15 custom union (`[Union]`) with its union members on a nested `IUnionMembers` interface, so its own `Value` stays typed `T`.
    - **Members:** `IsSuccess`, `IsError`, `Value` and `Error`, each throwing on the other state with that state in the message, plus `TryGetValue` and `TryGetError` with nullable annotations. No `Match`: a `switch` over the result does that job.
    - **Conversions:** `return plan;` and `return new PlanError.TableMissing(…);` are union conversions. Unlike implicit operators, they also convert interface-typed values (`IReadOnlyList<T>`).
    - **Reading:** `await svc.PlanAsync(r, ct) switch { CopyPlan p => …, PlanError.TableMissing e => … }` is exhaustive (CS8509).
    - **No value:** `Result<TError>`, returned with `return Result.Success;`. On the wire it is `{"ok":true}`; in TypeScript, `Call<void, E>`.
    - **`default`** is a distinct invalid state, on which every member throws:
      - the struct form needs a small Spar analyzer (SPAR050, a new rule, not a severity override), which caught 4 of 6 cases;
      - the class form gets 5 of 6 from nullable analysis.

      If it reaches the writer anyway, the writer reports the `response` / `serialization` failure (decision "`CallError` kinds"; reasoned through, not run).
    - **Costs (NativeAOT, 20 commands):** struct +102,400 bytes and no allocation for a synchronous error; class +83,456 bytes and 32–40 B per result. Dispatch is 120–180 ns for every form.
    - **The generator** emits one small async method per command. A single shared one doubled async allocations (about 600 B per call).
- **Diagnostics:** the spike catalogue is in `spikes/contract-syntax/RESULTS.md`, "Diagnostics", with each message and location rule. All are errors in category `Spar.Contracts`. Later decisions add a `DateTime` warning (decision "Wire format choices") and a suggestion for long-running commands that cannot observe cancellation (decision "Cancellation token delivery"). Messages about a referenced assembly start with "In referenced assembly 'X': …". It is the starting ID map for M1:
  - SPAR001–005: contract shape and names (sealed class or interface, not generic, valid and non-reserved names, wire-name clashes across assemblies);
  - SPAR010–016: command shape (return type, request parameters, token position, `ref`/`out`, generic commands, request types, command-name clashes including overloads). SPAR010 there rejected `ValueTask` and plain `Task`, which are now allowed;
  - SPAR020–023: wire types and record shapes;
  - SPAR030–035: error types and cases (shape, nesting, reserved payload names, code clashes, no cases);
  - SPAR040: two types with one TypeScript name in a contract.

  Already taken elsewhere: SPAR100 (the snapshot check) and SPAR101–SPAR104 (the CSP analyzer in `spikes/csp-navigation`).
- **Wire types:** positional records, primitives, nullable values (optional on the wire), arrays and `IReadOnlyList<T>`, nested records, and `closed record` errors.
- **Wire rules** (from `spikes/wire-details`; the choices once marked * were decided by the user on 2026-11-14, each on grounds that hold beyond SQL Server):

  | C# | JSON | TypeScript |
  |---|---|---|
  | `long`, `ulong` | always a canonical decimal string; JSON numbers rejected | `bigint` (decided). `Int128`, `UInt128` and `BigInteger` join this family when an app needs them |
  | smaller integers | number | `number` |
  | `decimal` | canonical string, scale kept, no exponent | `string` |
  | `DateTime` | ISO 8601 as STJ writes it (`Z`, offset, or none, by `Kind`); a contract warning suggests `DateTimeOffset` (decided) | `string` |
  | `DateTimeOffset` | ISO 8601 with an offset, required on read | `string` |
  | `DateOnly`, `TimeOnly`, `TimeSpan` | `yyyy-MM-dd`, `HH:mm:ss[.fffffff]`, the constant format `[-][d.]hh:mm:ss[.fffffff]` (decided) | `string` |
  | `Guid` | lower-case `D` | `string` |
  | `byte[]` | padded base64, whitespace rejected | `Uint8Array` (decided) |
  | `char` | one UTF-16 code unit | `string` |
  | `double`, `float` | finite only; NaN or ±Infinity is a writer failure, and the TypeScript encoder checks `Number.isFinite` | `number` |
  | enum | member name, exact match; `[Flags]` is a contract diagnostic for now (decided) | a union of string literals |
  | `string` | a lone surrogate becomes U+FFFD in both directions, never an error (decided: .NET writers, WebView2 and `fetch` already replace or reject them, so only replacement behaves the same on every transport); invalid UTF-8 in a value that is read is a `JsonException` | `string` |

  - **Skipped unknown properties are not decoded.** Inside them, invalid UTF-8 and lone-surrogate escapes were accepted by every reader in `spikes/serializer-fuzz`, System.Text.Json included. Decided: the rule stays limited to values that are read. Skipped values never reach the app, and System.Text.Json does not validate them either.
  - **Canonical `long` strings:** `Utf8Parser.TryParse` and `long.TryParse` accept `+1`, `007` and `-0`, and `Utf8Parser` stops early on `1 ` and `1e3` without failing. The reader needs its own check: `-?(0|[1-9][0-9]*)`, at most 19 digits, then a parse that must consume every byte (`spikes/wire-details`, about 15 lines).
  - **`null` and void:** a void result has no `value` key; a `T?` result writes `"value":null`. Responses write nullable properties explicitly; requests accept missing or `null`.
  - **Duplicate properties:** a repeated declared property is a `JsonException` with its name and byte offset; unknown properties are skipped. No measurable cost.
  - **Writer failures:** every unary response is buffered, and each stream item has its own buffer. A failure resets the pooled writer and sends `{"ok":false,"failure":{"kind":"response","code":"serialization","execution":"completed"}}`. A stream sends `"execution":"stopped","itemsSent":N` (decided). Unbuffered writing had already sent 278,560 bytes of invalid JSON when it failed.
  - **Why:**
    - Sent as numbers, only 603 of 100,000 random 64-bit ids survived `JSON.parse`. SQL Server values do cross 2^53: CPU ticks after about 35 days of uptime, query hashes.
    - `JSON.parse` changed 5 of 7 `decimal` samples.
    - SqlClient already replaces lone surrogates in `nvarchar`.
  - **SQL `decimal(38)`** overflows `System.Decimal` (only `GetSqlDecimal` reads it). Decided: Spar adds no SQL-specific wire type; an app that needs such values sends them as `string`.
- **The fingerprint hashes full wire shapes.** A mismatch returns `contract-mismatch` (409 on loopback) before any work.
- **Bundle budget (decided, user, 2026-11-14):** optional and off by default. `<SparBundleBudget>300KB</SparBundleBudget>` (name to confirm in M1) makes the build warn or fail when the compressed frontend grows past it. Spar has no built-in licence check for npm dependencies; the docs point to existing tools.
- **Generated client:**
  - One fixed `file:` dependency in the app's `package.json`: `"@spar/app": "file:../obj/spar/client"`, with subpaths `@spar/app/<contract>`, `/sdk` and `/vite`. It is never generated into `node_modules`.
  - Modules are generated directly as `.js` + `.d.ts` + a `.d.ts.map` that points to the C# declarations, so go-to-definition opens the contract. Errors are emitted as an interface map with JSDoc from the C# docs.
  - Validation runs in Spar's tests, never in the user's build:
    - the `.d.ts` files compile with `skipLibCheck: false` and the strictest flags, under TypeScript 6 and 7;
    - the `.js` matches the `.d.ts` and the manifest;
    - a differential test also generates `.ts` and compares it against `tsc --declaration`.
  - Renames are done in C#.
  - **The generated client builds every object field by field, never by copying a parsed object** (`Object.assign`, spread). `JSON.parse('{"__proto__":{"x":1}}')` creates an own `__proto__` key, and `Object.assign({}, it)` then changes the copy's prototype (`spikes/wire-details`).
- **Contract snapshot (decided, user, 2026-11-14):** `<Project>.contracts.snapshot`, written by the build next to the project and committed, with LF endings through `.gitattributes`. `Update` is the local default and `Verify` (SPAR100) the default in CI. It is the one stated exception to the rule that generated files live under `obj/`. M1 also fixes the fingerprint so it covers full wire shapes, not only type names.
- **Readiness:** the SPA sends `ui-ready` once its handlers are registered. The host reports it separately from navigation completed.
- **Generated code is readable:** formatted like hand-written code, with documentation carried over.

### 2b. TypeScript SDK (`src/Spar.Client`)

- **A project of its own** (an npm workspace, not on the npm registry) holding:
  - the protocol;
  - the per-host transports (WebView2, WebKitGTK, loopback), with the host providing the channel object;
  - `Call`/`match`/`unwrap` and the interceptor;
  - streams and subscriptions (resume policy);
  - the Vite plugin;
  - optional browser telemetry.
- **Ships compiled:** ES modules, `.d.ts`, `.d.ts.map`, source maps and the `.ts` sources, so go-to-definition reaches TypeScript. It is compiled with its own strict settings, separately for the browser (DOM) and the Vite plugin (Node).
- **Tree-shakable:** ES modules, `"sideEffects": false`, no top-level side effects; optional parts (telemetry) live in separate modules, not as methods.
- **Frameworks** (`spikes/frontend-frameworks`; React 19.3, Vue 3.5, Angular 22.2, run in headless Chromium against a scripted transport):
  - **React and Angular work.** React works with `use()`, TanStack Query and StrictMode. Angular works with `resource()`, the `async` pipe and strict templates, with zone.js and zoneless.
  - **Vue breaks by default:** `ref`, `reactive`, Pinia and templates wrap `Result` and `Call` in Proxies, and `#private` fields throw through them. App code can work around it with `shallowRef`, `markRaw` or `toRaw`.
  - **SDK changes, decided (user, 2026-11-14, conditional on the cost, which `spikes/call-promise-cost` measured as negligible):**
    1. `Call` becomes a real `Promise` subclass with `match`/`unwrap` (`Symbol.species` = `Promise`). That fixes TanStack's `data` type, and `no-floating-promises` then flags a forgotten call by default. It worked with `use()`, TanStack, Angular in both zone modes, and Vue. Cost: at most +91 ns per call over a native promise on WebView2 (0.15 % of a 59 µs round trip), +261 ns on WebKitGTK. It allocates less than the thenable and keeps 75 instead of 163 bytes per pending call. `await call` continues one microtask after a native promise, never later than the thenable. The client constructs it directly (the wrapping `of` form costs twice as much), keeps the `Symbol.species` override, and exports `Call` as a type only, so the inherited `Call.resolve` is never reachable.
    2. A `Symbol.toStringTag` getter on `Result` and `Call`, so Vue never proxies them. Checked with `ref`, `reactive` arrays, Pinia and `use()`.
    3. An optional `@vue/reactivity` `RefUnwrapBailTypes` augmentation, so `ref(result).value` keeps its type.

    Keep `#private`. A WeakMap fails through a Proxy the same way.
  - **Never freeze `Call`:** React's `use()` writes `status` and `value` onto it.
  - **Guidance for the docs:**
    - TanStack: `retry: false` with an `unwrap` query, which otherwise ran 4 backend calls for one declared error;
    - never create a call during render, which started the command 30 times in 600 ms, as with any Promise;
    - subscription `failure` handlers ignore their own abort;
    - RxJS through a small abort-on-unsubscribe `Observable` (`from()` kept the backend running about 260 ms);
    - Angular under the strict CSP: AOT only, no `<base>`, no component `styles`, and `inlineCritical: false`;
    - Vue: the runtime-only build, because the full build's template compiler is blocked as `eval`.
- **Tests:**
  - Vitest with fake channels for each host type;
  - type tests (code that must compile, and code that must fail);
  - a `tsconfig` × TypeScript 6/7 matrix;
  - **conformance vectors shared with the C# tests.**

### 2c. Bridge protocol

- **One text message protocol on every host:**
  - call ids;
  - credit-based flow control for streams;
  - an explicit cancel message;
  - exactly one terminal message per call;
  - `traceparent` reserved in every call.
- **Transports:**
  - Windows: `postMessage` (0.2 ms unary).
  - Linux: the script message handler, plus `evaluate_javascript` (0.2–0.3 ms).
  - Loopback: `fetch` for unary calls, and one streamed channel per document (the 6-connection browser limit blocked per-call streams).
  - Neither WebView streams intercepted responses usefully, so streams always use credits.
- **Which window may call what (decided, user, 2026-11-14):**
  - **Default:** every window of the app may call every command, because every window runs the app's own code from its one origin.
  - **Allow-list:** a window can declare one in C#, for example `w.Commands(c => c.Allow<IConnections>())`, with the name settled with the native-API design. A call outside it is refused before it runs (`rejected`, `forbidden-command`, `notStarted`).
  - **Docs:** they recommend a list for any window that shows content the app does not fully control.
- **Deadlines (decided, user, 2026-11-14):** calls and streams have no default deadline or idle timeout, because some calls wait on a person (pickers) or run long (copies).
  - On the page, a deadline is the web's `AbortSignal.timeout(ms)` passed as the call's `signal`.
  - In C#, an optional backend limit (for example `[SparCommand(Timeout = "00:00:30")]`, name to settle in M1) is folded into the command's cancellation token.
  - Both end like any cancellation: `cancelled` only when the backend confirms, otherwise outcome unknown. Never retried.
- **Cancellation:** the page is told "cancelled" only when the backend confirms the command ended on its token (for a stream, after its iterator was disposed). Otherwise it gets the real result, or outcome unknown (`unconfirmed` after the confirmation timeout, `lost` when the connection drops). Never retried. The page sees these as `CallError` with `execution` (decision "Contract syntax"), which replaced the spike's `CancelledError`, `NotExecutedError` and `OutcomeUnknownError`; a confirmed cancel is `kind: "cancelled"`, with `execution` `notStarted` or `stopped` (decision "`CallError` kinds").
  Details from `spikes/react` ("Cancellation"):
  - **A cancel that overtakes its call** (it arrived first, on another connection) is remembered by the host for 30 s. The call never runs, and the page gets not-executed `cancelled-before-start`. Implemented, but no test provoked it.
  - **An `OperationCanceledException` from another token** (an internal timeout, say) is not a confirmation. It is reported as `failed`, which is outcome unknown.
  - **Leaving a `for await` loop with `break`** cancels and waits for the confirmation. If none comes, the `break` itself throws outcome unknown.
  - **A pull stream stops at its next `yield`,** even when its code ignores the token (16–17 ms, one timer tick). Only a long stretch without a yield or an await on the token stays unconfirmed.
  - **Work that a command starts and does not await** is invisible to the host; "cancelled" covers only the command's own code. This is a contract limit to document.
  - **A navigation starts a new document epoch** (`NavigationStarting` on WebView2): the old document's calls are cancelled and their late replies dropped. On loopback a closed channel or request does the same.
- **Subscriptions:** a bounded drop-oldest buffer with visible `gap` events. Resumption is chosen at three levels (see M3).
  - **The `events` slot (decided, user, 2026-11-14).** Subscription items are `Result`s whose `match` takes `success` (the app's values only), `events` and the usual `errors` and `failure`.
    - `events.reset` is required, because ignoring it leaves stale state; `gap`, `reconnecting` and `resumed` are optional.
    - Each subscription offers only the events it can emit: a coalesced one has no `gap`.
    - The generated type is `Subscription<T>`, an `AsyncIterable` of `Result<T, E, SubscriptionEvents>`.
    - Checked in `spikes/app-model-check`: a missing `reset` fails `tsc` (TS2322, naming `reset`), and so do a misspelled event and a missing `events` object.
  - **Coalesced subscriptions** (decided, user, 2026-11-14): `[SparSubscription(Coalesce = true)]`, name to settle in M3. The subscription keeps only the newest value and replaces older ones silently, with no `gap` events. It suits state that only matters as its latest value: resource usage, progress, connection status.
    - The page sees each value at most once and always ends on the newest. A resumed subscription starts with the current value.
    - The capacity is 1 by definition, so `Capacity` is not allowed with it (a contract diagnostic).
- **Measured:** 100k rows in about 160 ms on Windows; 20,000 push events per second delivered completely.
- **The spikes' encoding.** This is evidence for M1, **not a decided wire format.** The code is `spikes/d1-pipeline/Spike.Runtime/SparCalls.cs`, `SparStream.cs` and `Spike.Tool/SparSdk.ts`; the trace field is in the newer copy under `spikes/browser-telemetry/d1-pipeline/`.
  - **Framing:** one message per `postMessage`, or one per line of a streamed response. Each is a kind letter, the call id, the kind's fields separated by spaces, then compact JSON (which never contains a raw newline).
  - **Host to page:**

    | Message | Meaning |
    |---|---|
    | `o doc` | channel open, for document `doc` |
    | `r id json` | unary result (the `ok` envelope) |
    | `i id dropped json-array` | a batch of stream items; `dropped` counts items a subscription dropped before it |
    | `e id` | completed |
    | `c id` | cancelled and stopped (the confirmation) |
    | `n id code json` | not executed |
    | `u id code json` | outcome unknown |
    | `z` | flush before close (telemetry copy) |

  - **Page to host:**

    | Message | Meaning |
    |---|---|
    | `h doc` | hello, opens the document's channel |
    | `q id contract command fingerprint trace json` | unary call |
    | `s id contract command fingerprint trace batch credits json` | stream or subscription call |
    | `k id credits` | more credits |
    | `x id` | cancel |
    | `z` | close work done (telemetry copy) |

  - **Codes seen:** not executed `invalid-request`, `unknown-command`, `contract-mismatch` (with the host's fingerprint) and `cancelled-before-start`; outcome unknown `failed` (exception type and message as detail) and `failed-while-stopping`. The SDK also raised not-executed `unavailable` when the backend could not be reached.
  - **Credits:** the page grants `credits` batches in flight (default 8) and refills them in halves as it consumes. What is in flight is bounded by window × batch size. Proposals from the spike: a window configurable per stream (8 × 1,000 rows kept 7,000 rows in flight), and batches sized by bytes or time (for example 64 KB or 5 ms) rather than rows; batches of 1 were 20–130× slower.
  - **Trace field:** `-` when the call has no context; otherwise the `traceparent`, optionally followed by `;` and the URI-escaped `tracestate`. Streams always sent `-` (not instrumented). The host drops a `traceparent` over 128 characters and a `tracestate` over 8 KB; a malformed context starts a new trace and never fails the call. `traceparent` costs 55 bytes. Before the field existed, `q` and `s` had no `trace` position.
  - **Flush before close:** the desktop host sends `z`, the page runs its registered close work (the telemetry flush, `Promise.allSettled`) and answers `z`, and the host waits at most 1 s.
  - **Loopback endpoints** (`spikes/loopback/Loop.Loopback/LoopbackHost.cs`; every bridge call needs the bearer token, and the host's `Origin` when one is sent):

    | Endpoint | Use |
    |---|---|
    | `POST /_spar/call/{contract}/{command}` | unary call, with the fingerprint in `X-Spar-Contracts` (16 hex characters of the manifest's SHA-256) |
    | `POST /_spar/stream/{contract}/{command}` | one NDJSON response per stream call (superseded by the channel: 6-connection limit) |
    | `POST /_spar/channel` | one long-lived NDJSON response per document, host to page; Kestrel's `MinResponseDataRate` turned off |
    | `POST /_spar/send` | page-to-host lines (credits, cancel) |
    | `POST /_spar/ready` | `ui-ready` |

    Status codes: 200 result; 401 `unauthorized`; 403 `forbidden-origin` or `forbidden-host`; 409 `contract-mismatch` with the host's fingerprint; 400 `invalid-request` with the serializer's message, and 400 `cancelled-before-start`; 404 `unknown-command`; 499 cancelled; 500 outcome unknown. A closed connection cancels that document's calls. Assets are served without the token (they ship in the executable anyway).
- **Decided (user, 2026-11-14): encoding C.** Every bridge message is a JSON header, a TAB, then the payload's own JSON, following the measurements below. The letter-line tables above describe the spikes' messages, not the target encoding; M1 writes the header fields and the shared conformance vectors from them, together with the rules listed under "Whatever the choice".
- **Decided before the shared conformance vectors (section 2b); the comparisons are kept as evidence:**
  - **The encoding (decided, C; the comparison is kept as evidence):** the letter-prefixed lines above, or the JSON envelopes `spikes/wire-details` used (`{"id":7,"ok":…}`, stream items `{"type":"item","value":…}`, the end message `{"type":"end","ok":false,…}`, and an `"arg"` key only when the command takes one). Section 2's envelope examples carry no `id`.
    - **Measured by `spikes/bridge-encoding`** (WebView2, loopback Chromium, WebKitGTK codec only), comparing (A) letter lines, (B) JSON envelopes and (C) a JSON header, a TAB, then the payload's own JSON:
      - **Speed and size do not decide.** End to end the three are equal within noise: WebView2 unary 59–79 µs, 100k rows in 163–164 ms, push p99 1.2–1.4 ms. B and C add 20–90 bytes per message, +0.02% on batched streams.
      - **Recommendation: C.**
        - A cannot grow: an older decoder rejected a new header field, and a stray space shifted fields silently.
        - B loses the call id on a damaged frame, so that call hangs until its timeout. B also re-reads large arguments (533 µs against 220 µs for 223 KB).
        - C keeps the id, sends the buffered body unchanged, and its trace keys match the loopback headers.
      - **Whatever the choice, every encoding needs:**
        - version negotiation in `hello`/`open`;
        - "an unknown type for a pending call fails that call";
        - a safe-integer check on ids (B and C accepted Infinity, 2^53+1 and −7);
        - rejection of duplicate header keys (`"id":7,"id":8` cancelled call 8);
        - a tolerant read of trace values;
        - string messages only, never `PostWebMessageAsJson`, which halved stream speed, cost +50% host CPU and threw on `\ud800`.
  - **Stream failure delivery (decided, user, 2026-11-14: in-band).** Every item is a `Result`, so a terminal failure arrives in-band and a `for await` loop cannot skip it (`spikes/ts-results`, `docs/app-model.md` 6.2; decision "Stream failures in-band; start-up exit codes; the start-up hook"). The alternative, not chosen: the iterator yields the N items and then throws (`spikes/wire-details`).
    - **Measured by `spikes/stream-result-cost`** (WebView2 157, Node 24, WebKitGTK 2.52; 1M rows in batches of 1,000, credit window 8):
      - **Cost does not decide.**
        - One `Result` per item adds 3–10 ns per row on WebView2 (+8–16 on WebKitGTK) over plain items.
        - That is 0.4–0.6 % of the bridge's 1.63 µs per row.
        - No task over 50 ms.
        - A stalled consumer holds the same memory per buffered row as with plain items.
        - Push at 20,000 events/s costs +0.1–0.3 ms of page time per second.
      - **Allocation:** `match` with an inline handler object allocates +97 B per row on WebView2, mostly the literal and its closures. A shared handler or `unwrap` allocates +27–37 B.
      - **What does cost:** the prototype's `stream()`, an async generator layered over the transport's iterable. It costs +103 ns and +321 B per row on WebView2 (6.5 % of the bridge).
        - The generated client uses a hand-written iterator over the transport buffer, whichever delivery is chosen.
        - Items are wrapped when the iterator hands them out, not when a batch is decoded. Wrapping at decode keeps about 40 % more memory per buffered row in a stalled consumer.
      - **Hybrids (exploratory):**
        - One `Result` per batch was the fastest shape (−18 ns and −59 B per row), from skipping the per-row `next()`. It puts a nested loop and the batch size into app code, so at most it becomes a later bulk API.
        - Plain items plus a separate `stream.result` cost the same as the baseline, but a loop that never reads the result misses the failure silently.
  - **`CallError.kind` values (decided, user, 2026-11-14):** `cancelled`, `unavailable`, `rejected`, `failed`, `transport`, `response` and `protocol`, each with `code` and `execution` (see the decision log for the table). `execution` is `notStarted` only when the host is certain the command did not run; declared errors are never a `CallError`. Earlier state: the prototype in `spikes/contract-prototypes/ts-match/sdk.ts` had `transport`, `protocol`, `cancelled` and `internal`, and `application` for declared errors (with `execution: "completed"`). An undeclared code from a newer backend became `protocol` / `unknownErrorCase`. The writer failure that section 2 once called `response-serialization` is now `kind: "response"`, `code: "serialization"`. The reserved payload names come from this shape.

### 3. Desktop hosts: Windows (WebView2) and Linux (WebKitGTK)

The core (contracts, dispatch, protocol, SDK) is host-neutral. Each host provides the SDK's channel object, its transport, its origin checks, its automation and its native services, in `Spar.Windows` and `Spar.Linux`.

**The UI thread is never blocked** (user requirement, 2026-11-14, on both hosts). Work on the WebView2 STA or the GLib main loop must never block, and must take as little time as possible, so the window keeps painting, resizing, dragging and answering input while the backend works.
- **What may run on it:** window and WebView calls, marshalling bridge messages to and from the dispatcher, and short state updates.
- **What never runs on it:**
  - backend commands, including their DI scope;
  - file, network or database I/O;
  - synchronous waits (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `Thread.Sleep`, blocking locks, `g_main_loop_run` nested to wait);
  - CPU work beyond a small budget;
  - serializing or parsing a large payload.
- **Host callbacks that run on it must return quickly:** `WM_NCHITTEST` reads a cache, `IStream::Read` and the WebKit scheme handler serve memory already in the executable, and `decide-policy` and `NavigationStarting` decide from data already at hand. Anything slower takes a deferral and finishes off the thread.
- **Proposal, to settle in M1:**
  - Development and DesktopTest builds include a UI-thread stall probe: a timer posted through the pump or main loop that records each delay over a threshold (for example 50 ms) together with what was running.
  - Desktop tests fail on a stall over a budget, which M4 sets from measurements.
  - Production builds contain no probe.

**Hung pages are detected by a heartbeat** (user, 2026-11-14; confirmed by `spikes/page-heartbeat`, with the changes listed below). An idle page stuck in a loop raises no event: `ProcessFailed(RenderProcessUnresponsive)` needs unanswered input, and an idle hung page gave nothing in 60 s (earlier research).
- **Ping:** while a document is loaded and the window is visible, the host sends a small ping over the bridge every 5 s, and the injected script answers from its message handler.
- **Unresponsive:** three missed answers (15–20 s) mark the page unresponsive. The host then logs it, calls a C# hook, and by default shows a native "Wait / Reload page" dialog.
- **Limits:** the library never kills anything on its own. It makes at most one automatic reload, only after a document had loaded, and never replays backend commands.
- **Hidden or minimised windows** are not pinged, so a throttled background page is not reported.
- **The spike checks:**
  - detection of `while (true) {}` on both hosts;
  - no false alarms when the window is minimised, covered or busy with legitimate work;
  - whether WebKitGTK's `is-web-process-responsive` adds anything;
  - the intervals.
- **Measured by `spikes/page-heartbeat`** (WebView2 157 on the host and in Windows Sandbox, WebKitGTK 2.52 on 6.0). The design holds:
  - Every endless main-thread hang was caught: 163 of 163 runs with the heartbeat armed. No false alarm: 0 of 123 runs.
  - Detection takes 15–20 s, because the first missed ping can leave up to 5 s after the hang starts. Users are told "about 20 seconds".
  - Finite work of 3 s or 10 s never alarmed. A 20 s task alarmed and the page answered again about 2 s later.
  - A hung worker alone does not alarm, which is correct.
  - WebView2 never raised `ProcessFailed` for an idle hang in 72 s. It raises `RenderProcessUnresponsive` about 16 s after input reaches a hung page, including the focus change when a dialog opens.
  - WebKitGTK's `is-web-process-responsive` stayed true for every idle hang. It turned false only 3 s after a navigation request.
  - Pinging never touched the UI thread: 7–11 µs per post, pump p99 0.21–0.37 ms.
  - Messages queue inside a hung renderer, about 1.6 kB each.
  - **Only terminating the renderer recovers:** terminate, then reload once, gives a working page in 50 ms (Windows) or 100 ms (Linux). Reload, Stop + Reload and same-site navigation recovered 0 of 40 runs. WebView2 has no terminate API, so the host uses the process id from `GetProcessInfos`. Backend state survived, and no command was replayed.
- **Changes to the design, from the spike:**
  1. Keep 5 s × 3.
  2. Arm the heartbeat when the document is created, never at `NavigationCompleted`, which missed every hang during load.
  3. Disarm it for documents without the injected script, which otherwise give a false alarm at 20 s.
  4. Gate on minimised, hidden and controller-hidden windows. A suspended WebView answers nothing, and the controller-hidden gate covers it.
  5. Treat `ProcessFailed(RenderProcessUnresponsive)` and the heartbeat alarm as one state with one dialog.
  6. "Reload page" terminates the renderer, then reloads once.
  7. Keep at most one ping outstanding while the page is unresponsive.
  8. On Linux, rely on the heartbeat and `web-process-terminated`, not `is-web-process-responsive`, and terminate a hung web process before quitting. It otherwise outlives the app by about 10 s.
- **Open, for the user:** what "Wait" does next. One proposal is to ask again after another three misses; it was not tested.
- **Not tested:**
  - CPU stress;
  - hidden windows for longer than 60 s;
  - several WebViews sharing one browser;
  - the native dialog on Linux;
  - hangs in iframes;
  - sleep, lock and session switch.

**Missing prerequisites get a visible error** (user requirement; `spikes/startup-prerequisites`, proposal for M1). An app launched from an icon has no terminal. When something it needs is missing, too old or broken, it must show a native message saying what is missing and how to fix it, then exit with a defined code. It must never crash silently.
- **Exit codes, the same on both OSes (decided, user, 2026-11-14):**
  - 0 OK;
  - 2 bad command line;
  - 3 missing;
  - 4 too old;
  - 5 present but broken;
  - 6 no graphical session (Linux);
  - 7 OS too old.

  Apps cannot change them.
- **Windows, in order:**
  1. `[STAThread]` on `Main`. Without it, environment creation fails with `0x80010106`, which would wrongly blame the runtime.
  2. The OS build through `RtlGetVersion`, if Spar sets a minimum.
  3. `GetAvailableCoreWebView2BrowserVersionString`, never the registry: the loader follows `ClientState\…\EBWebView`, and a stale path reads as missing while the registry looks fine.
  4. `CompareBrowserVersions` against the app's minimum.
  5. Normal start. A controller creation failure maps to 5.

  Environment creation succeeds even with the browser deleted, and `BrowserProcessExited` never fired in any failure, so only controller creation detects a broken runtime.

  **Measured in Windows Sandbox** (each case 3 runs, real or simulated):
  - dialog after 46–128 ms when the runtime is missing (exit 3);
  - 49–104 ms when it is too old (4);
  - 71–285 ms when it is broken (5);
  - a healthy window was ready in 281–305 ms.
- **Windows dialog:**
  - **The dialog:** `TaskDialogIndirect`, which needs a manifest declaring Common Controls 6 and per-monitor DPI awareness. Without it the call throws `EntryPointNotFoundException`.
  - **Its content:** the missing item, a "Download the WebView2 Runtime" button and link to Microsoft's end-user page (https://developer.microsoft.com/microsoft-edge/webview2/consumer/), and expandable details. The host opens the link with `ShellExecuteW`; the dialog never opens links itself.
  - **Fallback:** `MessageBoxW` (25–81 ms, no links, only system-DPI-aware).
  - **Dark mode:** neither dialog follows it.
  - **Screen readers:** UI Automation exposes every text and button.
  - **The app window:** the app's empty window must be hidden or become the dialog's owner.
- **Linux, in order:**
  1. The display variables. With no display there is no graphical message: exit 6 within 6–23 ms, with stderr and the journal.
  2. GLib, then GTK 4.
  3. `g_set_prgname`. Without it the window has no class or app id and shows as "Unnamed Window".
  4. `GSK_RENDERER=cairo` for the startup error dialog, if the user hasn't set it. The app itself gets cairo only on a software GPU (decision "Guidance review applied", point 3).
  5. `gtk_init_check`.
  6. WebKitGTK and its version.
  7. The helper folder, `bwrap` and `xdg-dbus-proxy`.
  8. A trial `bwrap` run. A denied sandbox fails it in 1.5 ms; without the trial, WebKit aborts after 4.4–6.7 s with exit 133 and no message.
- **Linux message:**
  - **With GTK 4 initialised:** the GTK dialog (`GtkAlertDialog`). It takes 91–212 ms with cairo, against 420–820 ms with GTK's GL renderer on a software GPU.
  - **Otherwise,** in this order: kdialog (first on KDE), zenity, yad, a D-Bus notification, notify-send, then xmessage. zenity needs GTK 4 on Ubuntu 24.04, so it fails exactly when GTK 4 is missing.
  - **Packages:** the message names the package to install.
  - **What desktops ship:** Ubuntu 24.04 and Fedora 43 Workstation already have everything. Kubuntu 24.04 lacks WebKitGTK 6.0 and `xdg-dbus-proxy`, so it gets the GTK dialog asking for `libwebkitgtk-6.0-4`.
- **Both OSes:**
  - stderr is written whenever a console or redirected handle exists.
  - The system log is written too: the journal's native protocol or `/dev/log` on Linux. On Windows, a log file in the app's per-user folder, because an unregistered event-log source reads poorly.
  - A C# hook runs before the default dialog and receives the problem's details. It may replace the dialog with the app's own message, and suppress Spar's dialog only when it shows one. stderr and the system log are written regardless, and the exit code cannot change (decided, user, 2026-11-14). The hook was not prototyped.
- **Not tested:**
  - DPI above 100 %, and Narrator and Orca;
  - the download page opening;
  - a real older Windows or WebView2 Runtime, per-user installs, non-stable channels;
  - real GNOME and KDE desktops, and a `.desktop` launch.

  The checks that need the user are in the table "Checks that need the user at the PC".

**Windows:**
- **Threading:** one STA thread owns the window, environment, controller and their cleanup. Its message pump stays responsive. No synchronous waits on pump-dependent work.
- **Bindings:**
  - CsWin32 for Win32;
  - hand-declared `[GeneratedComInterface]` for WebView2 (CsWinRT 3.0 added 10–23 MB; not now), with a slot-order test;
  - the WebView2 loader linked statically under AOT, and `WebView2Loader.dll` published on CoreCLR.
  - Feature-detect newer interfaces, and test on the minimum supported Runtime.
  - **Interface slots M1 needs beyond the base set,** as the spikes declared them (0-based vtable slots, `IUnknown` included). The evidence is in the spike files named; the slot-order test should check each one against the WebView2 header:
    - `ICoreWebView2_22` (`db75dfc7-a857-4632-a398-6969dde26c0a`), declared flat: slots 3–122 are placeholders, and `AddWebResourceRequestedFilterWithRequestSourceKinds` is slot 123. It is called with context 0 (all) and source kinds `0xFFFFFFFF`. Also `ICoreWebView2WebResourceRequestedEventArgs2.GetRequestedSourceKind` (1 document, 2 shared worker, 4 service worker) and `ICoreWebView2PermissionRequestedEventArgs3.SavesInProfile` (`spikes/desktop-origin/Origin.Win/Interop/WebView2.cs`).
    - For the theme, called through raw vtables (`spikes/theme-startup/Theme.Desktop/Interop/WebView2.cs`, class `WebView2Raw`): `ICoreWebView2Controller2` (`c979903e-…`) 26/27 get/put `DefaultBackgroundColor`, the colour struct passed by value; `ICoreWebView2_13` (`f75f09a8-…`) 105 `get_Profile`; `ICoreWebView2Profile` (`79110ad3-…`) 8/9 `PreferredColorScheme`; `ICoreWebView2Environment10` (`ee0eb9df-…`) 20 `CreateCoreWebView2ControllerOptions` and 21 `CreateCoreWebView2ControllerWithOptions`; `ICoreWebView2ControllerOptions3` (`b32b191a-…`) 10 `put_DefaultBackgroundColor`.
- **Assets and the bridge:** `WebResourceRequested` on the app origin `https://app.localhost` (user decision; verified by `spikes/desktop-origin`), and `postMessage` for calls.
  - The host answers every request to its origin, with a 404 for unknown paths. An unanswered request opened 2 TLS connections to `[::1]:443`; a host 404 never leaked.
  - The filter is registered with `AddWebResourceRequestedFilterWithRequestSourceKinds(…, ALL)` (`ICoreWebView2_22`, feature-detected; minimum Runtime to check). The legacy filter missed shared and service workers.
  - WebSockets are never intercepted, and CSP `'self'` in `connect-src` still allows `wss://app.localhost`. So the policy names the origin with its scheme (section 3b).
  - **Defence in depth:** `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` stopped everything else, including the preconnects every navigation makes to `[::1]:443` before any request. It gave 0 loopback connections, with no load-time cost. It was tested only through the environment variable set in-process. To verify in M1: through `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`, and whether it is supported.
  - **Load times** (10 alternating runs each, median from `Navigate()` to `ui-ready`): `app.localhost` 68.5 ms, `app.spar.invalid` 67.3 ms, `app.example` 68.8 ms, `app.spar.local` 190 ms.
  - **Local network access** rules did nothing for host-served pages, for either `.localhost` or `.invalid`: loopback fetches got 200 with no prompt. CSP is the only gate protecting loopback services from the page.
  - The origin keys the page's stored data (`localStorage`, IndexedDB, cookies, remembered permissions), so it never changes between versions without a migration.
- **Instrumentation from the first slice:**
  - Monotonic timestamps, a run ID, and view, navigation and call IDs.
  - Milestones: window visible, environment and controller ready (with HRESULTs), navigation start/DOMContentLoaded/complete with status, `ui-ready`, each RPC (bytes, duration, handler time), and every shutdown phase.
  - Consumers: tests, devtools and measurements.
- **Teardown follows the runbook:**
  1. Mark the view closing, so a second close is harmless.
  2. Stop admitting calls, cancel, and settle pending promises.
  3. Keep pumping during bounded cleanup.
  4. Check view and document liveness before every late reply.
  5. Close the controller explicitly on the STA. `Close` is synchronous. On Runtime 157 it does run `beforeunload`, `visibilitychange`, `unload` (29 of 29) and `pagehide` (26 of 29), and their writes persisted (`spikes/webview-profiles`; this corrects an earlier note). Required cleanup still must not depend on the page.
  6. Observe `BrowserProcessExited` before deleting a profile.
- **Failure recovery:** handle `ProcessFailed` per view and `BrowserProcessExited` per environment, in either order. Reset the affected RPC and stream state. Coordinate recreation across views. No recovery during app shutdown.
- **Hardening:** `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` is appended even in Production, so Production ignores it and the other browser environment overrides unless the app opts in (section 3c).
- **Title bar drawn by the page** (`spikes/windows-titlebar`, real input in Windows Sandbox). **Decided (user, 2026-11-14): when an app uses a custom title bar, the page draws the caption buttons**, on Windows as on Linux:
  - **Controller: windowed**, as in M1. Visual hosting also passed every input check, but it needs mouse forwarding, DirectComposition and a host-drawn system menu. Drag and drop, touch, pen, the wheel and IME are untested there, so it stays a fallback.
  - **Frame:**
    - keep `WS_OVERLAPPEDWINDOW`;
    - in `WM_NCCALCSIZE`, call `DefWindowProc` and then restore the top edge. When maximized, add `SM_CYSIZEFRAME + SM_CXPADDEDBORDER`, so the content is exactly the work area;
    - the host answers the top resize edge in an 8 px band (96 DPI).

    No `DwmExtendFrameIntoClientArea` is needed.
  - **Page:**
    - `IsNonClientRegionSupportEnabled` with CSS `app-region: drag` on the bar and `no-drag` on its controls;
    - the page posts its button layout to the host whenever it changes;
    - the host's `WM_NCHITTEST` reads only that cache (p50 0.1–0.5 µs, p99 under 4 µs, unchanged while the page was busy).
  - **Caption buttons drawn by the page, by default.**
    - The host returns `HTMINBUTTON`, `HTMAXBUTTON` and `HTCLOSE` through one host-owned `WS_EX_TRANSPARENT` child window above the WebView, clipped to the top band and the buttons.
    - It is needed because WebView2's drag-region windows turn every non-resize code into `HTCAPTION`.
    - With it, the Windows 11 Snap Layouts flyout appears on the page's maximize button.
    - Theme: 0 wrong-colour frames at start and across 8 live switches.
  - **Windows' own buttons** work only restored. Maximized, `DwmDefWindowProc` stops reporting them and restore clicks do nothing; the `WM_GETTITLEBARINFOEX` workaround was not tried. In dark mode they also show DWM's `#4a4a4a` for about 350 ms at every start.
  - **Verified with real input:** drag, controls inside the bar, double-click, right-click system menu, the maximized state, the four resize edges, Snap, `Alt+Space`, `Alt+F4`, `Win+arrows`.
  - **Cost:** +4,096 bytes of AOT executable, no start-up difference (393 against 389 ms), about 170 lines of host code.
  - **Open:**
    - DPI other than 100 %, `WM_DPICHANGED`, mixed-DPI monitors, Windows 10 and the shadow were not tested. The Sandbox guest takes its scale from the client window on a 96 DPI monitor, and four ways to change it failed. This needs a host monitor at that scale, which is the user's setting, or a VM with a console session.
    - Narrator was not run, because its audio would play on the user's speakers.
  - **Accessibility, solved** (`spikes/windows-sandbox-checks`, native UIA client on the host and in the guest):
    - A UIA provider on the top-level window with `ProviderOptions_RefuseNonClientSupport` removes the empty native title bar.
    - Subclassing WebView2's drag-region windows, which run on our UI thread, and the host overlay hides the unnamed panes.
    - The control view drops from 45 to 24 elements, the page stays fully exposed, and every input check still passes.
  - **First click on an inactive window** was lost on the page's caption buttons, because activation sends `WM_NCMOUSELEAVE` between press and release. Fixed: keep the press across it.
  - **Windows' own buttons become viable when maximized** if the host hit-tests them from `WM_GETTITLEBARINFOEX` and handles the clicks only while maximized (restore, minimize, close and Snap Layouts work). Taking over while restored broke them. They stay invisible to UIA. Page-drawn buttons remain the proposed default.
    - The cache is kept in CSS pixels and scaled by the window's DPI at hit-test time.

**Linux:**
- **API: `webkitgtk-6.0` (GTK4) only** (user decision, 2026-11-14). There is no GTK3 build and no compile-time switch; the 4.1 measurements below stay as evidence. The Linux choices that depended on it are decided: acceleration off plus cairo on software GPUs, and XTEST for trusted test input (below). M4 checks the oldest distribution with a usable `webkitgtk-6.0` and GTK4: another framework documents Ubuntu 24.04 or Debian 13 as the minimum for the same stack, while the package pages list `webkitgtk-6.0` for Ubuntu 22.04.
- **Trusted input in desktop tests (decided, user, 2026-11-14): XTEST.**
  - Tests force `GDK_BACKEND=x11` and send input through XTEST. Under XWayland it delivered trusted Tab and clicks (`isTrusted: true`) on GTK3 and GTK4 (`spikes/linux-wsl-desktop`).
  - In CI they run on Xvfb in a container.
  - WebKitWebDriver is out: Ubuntu packages it only for 4.1, and it opens a TCP port.
  - The test framework sends input through one interface, so a Wayland route can be added later.
  - Wayland input stays a check that needs the user.
- **Threading:** GTK and WebKit on a dedicated thread with a GLib main loop. A `SynchronizationContext` posts through `g_idle_add_full`. The loop keeps running until the web processes exit.
- **Bindings:** `[LibraryImport]` and `[UnmanagedCallersOnly]` against gtk, glib, gio and webkit. The AOT exe links only glibc, and the libraries load at first use, so check for them at startup with a clear error.
- **Assets and the bridge:** a custom scheme (`spar://app/`), and the script message handler.
- **Origin policy:** messages and scheme requests carry no origin. Use a navigation lock, inject into the top frame only, and consider an isolated script world. Foreign iframes are to be tested.
- **Hardening:** ignore `WEBKIT_INSPECTOR_SERVER` and the other browser environment overrides in Production (section 3c). GStreamer, Mesa and dconf write to `~/.cache`.
- **The sandbox:** always on with GTK4 (a non-root user, user namespaces). It works in WSL, costing 5–22 ms.
- **Startup order** (`spikes/linux-wsl-desktop`): create and attach the web view, call `load_uri`, then show the window.
  - Shown first, GTK3 on X11 without the sandbox stalled about 0.5 s. WebKit's draw handler waits for the web process's first frame, while the web process waits for the UI thread to set up its network connection.
  - Shown first, the page also lacks focus, and the first Tab is lost.
  - With this order: 167–185 ms warm on GTK3, 214–261 ms on GTK4 (WSLg, software GPU).
- **Recovery:** handle `web-process-terminated` with a visible message and recovery.
- **Startup check:** see "Missing prerequisites get a visible error" above for the order, the messages and the exit codes. It covers the libraries, the WebKit helper folder (found through `/proc/self/maps`) and `bwrap`/`xdg-dbus-proxy`. An earlier prototype exited with code 3 within 7–102 ms, naming what is missing and the package to install. Before it, the app died with an unhandled `DllNotFoundException` after 253–813 ms (exit 134), or with a GLib abort (SIGTRAP, exit 133) after 1.9–4.3 s for a missing helper or `bwrap`.
- **API differences between 4.1 (GTK3) and 6.0 (GTK4)** (`spikes/linux-desktop`). One code base with a compile-time switch covered both, with about 20 `#if` blocks in the host:

  | Topic | 4.1 / GTK3 | 6.0 / GTK4 |
  |---|---|---|
  | Data and cache folders | `WebKitWebsiteDataManager` (`base-data-directory`, `base-cache-directory`), created through `g_object_new_with_properties` because the constructor is variadic, then `webkit_web_context_new_with_website_data_manager` | `webkit_network_session_new(data, cache)`, passed to the view as the construct property `network-session` |
  | Script message signal | `(manager, WebKitJavascriptResult*)`, then `webkit_javascript_result_get_js_value` | `(manager, JSCValue*)`; registration takes a world name |
  | Snapshot | `cairo_surface_t*` | `GdkTexture*` |
  | Window | `gtk_init_check(argc, argv)`, `gtk_window_new(TOPLEVEL)`, `gtk_container_add`, `gtk_widget_show_all` | `gtk_init_check()`, `gtk_window_new()`, `gtk_window_set_child`, `gtk_window_present` |
  | Main loop | `GMainLoop` (`gtk_main` not needed) | `GMainLoop` (`gtk_main` does not exist) |
  | Web-process sandbox | Off by default; `webkit_web_context_set_sandbox_enabled` | Always on (bubblewrap); as root under Docker's defaults it aborts with exit 133 |
  | Session bus | Not needed | Warns "Unable to acquire session bus" without one, and still works |

  Identical in both: the custom scheme, `WebKitURISchemeResponse` and the request body, `evaluate_javascript` and `call_async_javascript_function`, user scripts, the security manager, `load-changed`, `close` and the automation signals.
- **Runtime packages** (Ubuntu 24.04 names): `libwebkit2gtk-4.1-0` for the GTK3 build, `libwebkitgtk-6.0-4` for the GTK4 build. Their dependencies bring GTK, libsoup 3, JavaScriptCore, GStreamer base and good, `bubblewrap` and `xdg-dbus-proxy`. Each adds about 34 MB of download and 128 MB on disk to a system that already has GTK3 and GTK4. The executable itself needs only glibc.
  - **Minimum version:** the APIs used (request body, `evaluate_javascript`) need WebKitGTK 2.40 or later.
  - **Distributions** (package pages read 2026-11-14): both APIs are packaged in Debian 12 and later, Ubuntu 22.04 and later (jammy-updates has 2.50.4) and Fedora (`webkit2gtk4.1`, `webkitgtk6.0`). Ubuntu 24.04 has a WebDriver package for 4.1 only. Versions per distribution, other than jammy's, were not recorded; M4 sets the supported minimum.
- **Software GPUs:** on llvmpipe, WebKit's accelerated path cost about 300 ms on the cold first frame (30 ms warm), and GTK4's GL renderer more.
  - **Default on a software GPU (user decision, 2026-11-14):** WebKit acceleration off (`WEBKIT_HARDWARE_ACCELERATION_POLICY_NEVER`) plus GTK4's cairo renderer. Measured on WSLg: 213 MB in total against 379 MB, and a faster cold first frame. An app can turn acceleration back on from C#.
  - On a real GPU the defaults stay as they are (not measured).
  - The host sets `GSK_RENDERER` before `gtk_init` and leaves it alone if the user already set it. The startup error dialog always uses cairo; the app uses it only on a software GPU (decision "Guidance review applied", point 3).
  - To define in M1: how the host detects a software GPU (for example the GL renderer string, or no `/dev/dri` render node), and the C# option's name.
- **Memory** (`spikes/linux-memory`; WSLg with llvmpipe; total PSS of the app and its WebKit processes, Wayland / X11, in MB):

  | At ready, small page | Total | App process | Web process |
  |---|---|---|---|
  | 4.1, default | 320 / 297 | 115 / 92 | 183 |
  | 6.0, default (GTK4 GL renderer) | 379 / 359 | 181 / 161 | 175 |
  | 6.0, `GSK_RENDERER=cairo` | 310 / 301 | 106 / 96 | 181 |
  | 4.1, WebKit acceleration off | 220 / 181 | 111 / 72 | 88 |
  | 6.0, acceleration off + cairo | 213 / 201 | 104 / 92 | 87 |
  | Electron 45 (reference, all processes) | 298 / 285 | main 101 / 90 + GPU 94 | 42 |

  - **The software GL path is the largest item:** WebKit's acceleration costs about 90 MB per web process, and GTK4's GL renderer about 75 MB in the app process. Without it, WebKitGTK and GTK take about 140–155 MB on either API.
  - **.NET adds about 2 MB.**
  - **A heavy page** (React plus 10,000 rows) adds about 245 MB of web-process heap on both APIs, against 417 MB in Electron's renderer.
  - **30 s idle** changes nothing.
  - **On a software GPU, the levers are** WebKit acceleration off plus cairo on GTK4. They would also cut start-up time (see above), so they are candidates for the software-GPU default.
  - **A real GPU was not measured.**
- **Verified on a real desktop (WSLg):**
  - Both APIs map, render and theme correctly on Wayland and X11 (40 captured runs, 0 wrong frames).
  - Resize, maximize and restore showed no white or black frames. At maximize, one capture (35 ms or less) showed the new bounds before the content had grown, with the desktop visible behind it.
  - Animation runs at 61–62 fps.
  - Not covered: a real GPU, HiDPI, IME, screen readers, GNOME, KDE, libadwaita.
- **Desktop integrations** (`spikes/linux-integration`, WSLg plus xfce4-panel and openbox in Docker). Everything tested works on both APIs:
  - **Title bar drawn by the page:**
    - WebKitGTK ignores `app-region`, so the injected script asks the host to move the window. The host uses `gtk_window_begin_move_drag` on GTK3 and `gdk_toplevel_begin_move` on GTK4.
    - The move starts after a 4 px threshold, 16–30 ms after mousedown. Starting on press lost the double-click on GTK4.
    - On openbox, 6 of 6 drags moved the window exactly, and the edges resized it. Double-click maximize took 2–16 ms.
    - The window menu is unavailable on both window managers tested.
    - The window keeps its CSD shadow and edges with an empty title-bar widget, which GTK4 needs hidden.
    - Wayland drag is untested.
  - **Tray:** StatusNotifierItem plus dbusmenu, written directly on GDBus with no library.
    - On a real xfce4-panel: icon, tooltip, updates, left click (`Activate`) and the menu all work, and the item re-registers after a panel restart. The panel uses the grouped menu calls (`EventGroup`, `AboutToShowGroup`).
    - With no watcher, the tray reports "unavailable" without failing.
  - **Close to tray:** restore to the first frame in 18–25 ms, with the page's state kept.
  - **Placement:** size and maximized state on every backend. Position only on X11: exact with `XMoveWindow` on GTK4, while GTK3 drifts by the frame size. None on Wayland.
  - **Other services:**
    - file pickers (GTK4 always through the portal; cancel from code in about 710 ms);
    - clipboard (outside text needs WebKit's permission, denied by default);
    - print to PDF with no dialog in 82 ms;
    - the launcher progress signal.
  - **Single instance:** a lock file plus a Unix socket (decided, user, 2026-11-14). The handoff takes 4–10 ms, and a hung first instance gives a clear failure after 2 s. GApplication's D-Bus uniqueness waits forever on a hung first instance.
  - **GTK4-only problems:**
    - web processes outlive the window after a window-manager move or resize (5 of 5 runs, more than 10 s). Cause and fix found by `spikes/linux-memory`: reset the web view's event controllers (`gtk_event_controller_reset`) right after `gdk_toplevel_begin_move`/`begin_resize`, as GTK's own title bar does. The processes then exit in 24–26 ms (5 of 5 moves, 5 of 5 resizes);
    - on WSLg Wayland, a hidden window keeps showing its last frame (unrealize after hiding);
    - on WSLg Wayland, a window started maximized is killed by the compositor (maximize after the first map).

    The 6.0 recommendation stands.
  - **Not tested:** file drop, Wayland input, GNOME and KDE tray hosts, notifications (none present under WSLg).

**Native services, per host, behind one API:**
- **API quality comes first** (user requirement, 2026-11-14). The C# and TypeScript APIs for native features must be as easy and pleasant to use as Spar can make them. The features, with their state in `docs/research-review.md` section 6:
  - **Adopted (M3):**
    - file access: open and save dialogs with grants, as below;
    - the clipboard. **API decided (2026-11-14):**
      - The page uses the web's `navigator.clipboard`. Writing is allowed; reading needs `Permission.ClipboardRead` declared in C#, which sets the header and the host gate.
      - The backend gets an injectable `IClipboard` (`SetTextAsync`, `GetTextAsync`) returning `Result`s with `Busy` (after a short retry), `TooLarge` and `Unavailable`.
      - Text only at first. Real copy and paste, which needs focus, is checked in Windows Sandbox or a Linux VM.
      - The example is `docs/app-model.md` 3.7;
    - drag and drop, where dropped files become grants and the parent folder gets none. **API decided (2026-11-14):**
      - Off by default. The page declares drop areas with `files.dropTarget(element, { accept, folders, limit }, onDrop)`, which returns a function that removes the area.
      - `onDrop` gets `Result<DroppedItems, DropError>`: file grants, plus read-only folder grants with `folders: true`. The error cases are `notAccepted` and `tooMany`.
      - While a valid drag hovers, the SDK sets `data-spar-drop="over"` on the area.
      - Drag and drop inside the page keeps working.
      - How each host receives the files (WebView2 `postMessageWithAdditionalObjects`, a GTK drop target) is checked in M3. The example is `docs/app-model.md` 3.6;
  - **Measured, API to design:**
    - the tray icon and its menu, and close to tray;
    - the custom title bar (decided, page-drawn buttons);
    - window placement;
    - single instance and argument forwarding (decided mechanism);
    - print to PDF (82 ms with no dialog on Linux; WebView2 has `PrintToPdfAsync`), plus printing with the system dialog. **API decided (2026-11-14):**
      - On the page, `print.show()` opens the system dialog and `print.toPdf(options)` saves through the save dialog with a safe write. Both are `Call`s, and `window.print()` does the same as `show()`.
      - In C#, `IPrinting.ToPdfAsync(options, grant, name, ct)` writes without a dialog.
      - The cases are `Dismissed`, `NoPrinter`, `Unavailable` and `Failed`, plus the file cases.
      - The first version prints the current page, styled with `@media print`. The example is `docs/app-model.md` 3.8;
  - **New, from the user (2026-11-14):**
    - a folder picker. **Decided (2026-11-14):** `IFileDialogs.OpenFolderAsync` and `files.openFolder()` return `Result<FolderGrant, FileDialogError>`.
      - The grant declares `Read` (the default) or `ReadWrite`, and offers list, read, safe write, create folder and delete (write access only).
      - Every path is relative to the folder. Absolute paths, `..` and links leaving it give `FileAccessError.OutsideFolder`, checked on the final resolved path. A write through a read grant gives `ReadOnlyGrant`.
      - The page sees names, never full paths. Grants are tied to the window.
      - Remembering a folder across restarts is left out of the first version.
      - The Linux portal path is measured in M3. The example is `docs/app-model.md` 3.6;
    - resource usage.
  - **Later:** notifications (after the installer decision), deep links and file associations.
  - **To discuss with the user** (2026-11-14): menus and accelerators (window, context and tray menus), notifications (balloon or toast, and what an unpackaged app can do), deep links and file associations. They stay listed even where they ship later.
  - **Keyboard shortcuts** (user, 2026-11-14), to design with the menus:
- **Decided with context menus (2026-11-14):** actions are declared once with `actions.define({ id: { label, shortcut, run } })`, and menus reference the action objects, so a duplicate id (TS1117) or a misspelled action (TS2551) fails `tsc`. Native context menus come from `menus.show(event, items)`. WebView's default menu is off, and text fields keep a minimal native Cut/Copy/Paste/Select all. Shortcuts work while the window has focus; plain editing keys go to inputs; browser accelerators stay off. Scoped actions may shadow app-wide shortcuts while active. A shortcut is one string or one per OS (`{ windows, linux, default }`; user, 2026-11-14), checked for duplicates per OS. A duplicate id across `define` calls, or the same shortcut on two app-wide actions, throws at once. C# actions share the ids. A native menu bar and system-wide shortcuts are not in the first version. The example is `docs/app-model.md` 6.4.
    - shortcuts inside the app's windows (for example Ctrl+S, Ctrl+K), declared once and shown in menus and tooltips;
    - how they coexist with the page's own key handling and with the browser accelerators turned off by default (plan 3c);
    - platform conventions (Ctrl on Windows and Linux);
    - whether system-wide shortcuts are ever offered. The review marked them "ignore", and on Linux they work only on X11.
  - **Added by the user (2026-11-14), likely needed soon:**
    - **context menus:** turn off or replace WebView's default menu (Back, Reload, Save), and show the app's own native menu from the page;
    - **JavaScript dialogs. Decided (2026-11-14):**
      - `alert`, `confirm` and `prompt` are shown as the OS's message box, owned by the window, through a deferral.
      - New code uses `dialogs.message({ title, text, buttons, default })`, a `Call` typed by its button names whose only error case is `dismissed`. C# gets the same through `IDialogs`.
      - `beforeunload` never prompts; the two-stage close asks instead.
      - The example is `docs/app-model.md` 6.5;
    - **opening things outside the app. Decided (2026-11-14):**
      - `target="_blank"` links to allowed schemes open in the system browser by themselves, and `shell.openExternal(url)` serves links built in code.
      - Granted files and folders get `openWithDefaultApp()` and `reveal()`. C# gets `IShell` (`OpenExternalAsync`, `OpenAsync`, `RevealAsync`).
      - The cases are `notAllowed`, `noHandler` and `failed`.
      - **Opening files is allow-listed** (user, 2026-11-14), not block-listed:
        - nothing opens by default; the app declares its types with `spar.Shell(s => s.AllowOpening(…))`;
        - types the OS reports as dangerous (`AssocIsDangerous` on Windows; executables, `.desktop`, scripts and executable MIME types on Linux) stay refused even when declared, unless `AllowDangerous(ext)` is set, with the risk documented;
        - M3 checks whether Windows flags `.crt`, `.cer`, `.iso` and `.vhdx`. If not, Spar adds them to its own refused list.
      - The example is `docs/app-model.md` 6.6;
    - **taskbar progress and badges. Decided (2026-11-14):**
      - An injectable `ITaskbar` per window, and `taskbar` in the SDK: `SetProgress(0–1 | Indeterminate | Paused | Error)`, `ClearProgress()`, `SetBadge(count | null)`.
      - On Windows a count is a Spar-drawn overlay, or the app's own icon; on Linux the launcher shows it.
      - Where unsupported the calls do nothing and `IsAvailable` is false.
      - The example is `docs/app-model.md` 3.10;
    - **power and session events. Decided (2026-11-14):**
      - An injectable `ISystemEvents.Watch()` yields `Suspending`, `Resumed`, `Locked`, `Unlocked` and `SessionEnding(Deadline)`; the page gets `system.on(event, handler)`.
      - Session end is not cancellable and has a short fixed deadline.
      - `IPower.KeepAwake(reason)` holds the machine awake during long work (`SetThreadExecutionState`, a logind inhibitor).
      - The host listens to `WM_POWERBROADCAST`, session notifications and end-session messages on Windows, and to logind on Linux.
      - Tested in Windows Sandbox or a Linux VM. The example is `docs/app-model.md` 3.11;
    - **more windows. Decided (2026-11-14):**
      - Window kinds are declared in C# with `spar.Window(name, w => w.Route(…).Size(…).Modal().SingleInstance().Commands(…))`, and the generator emits their names as a TypeScript union, so a typo fails `tsc` (TS2345).
      - The page uses `windows.open(kind, params)` and `windows.showModal(kind, params)`, a `Call` with the case `dismissed`; inside a modal, `windows.closeWith(value)`.
      - Modals disable their owner. Single-instance kinds focus the open window.
      - Windows share data through the backend, and the main window closes its secondary ones first.
      - A duplicate kind fails at startup. The example is `docs/app-model.md` 6.7;
    - **zoom and find in page. Decided (2026-11-14):**
      - Zoom is on by default (Ctrl+wheel, Ctrl+plus/minus/0, handled by the host), saved per window with its placement, and limited or turned off from C# (`w.Zoom(min, max)`, `w.Zoom(false)`). The page has `zoom.get/set/onChange`.
      - Find in page is off by default. `w.FindInPage()` turns on Ctrl+F with an SDK find bar on both hosts; `find.start(text)` returns the match count, with `next`, `previous` and `stop`.
      - M1 checks the WebView2 find API's minimum Runtime. The example is `docs/app-model.md` 6.8;
    - **screen-capture protection. Decided (2026-11-14):**
      - `w.ProtectFromCapture()` per window kind, `IWindows.SetCaptureProtection(bool)` and `capture.protect(bool)` at run time, which report whether it took effect.
      - Windows uses `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, on 10 2004 and later. Linux cannot block capture, so `isAvailable` is false there.
      - It is documented as reducing casual leaks, not as a security boundary.
      - The example is `docs/app-model.md` 6.7;
    - **network status. Decided (2026-11-14):**
      - The page uses `navigator.onLine` and the `online`/`offline` events, with no SDK API.
      - The backend has an injectable `INetworkStatus` (`Current`, and `Watch()` debounced about 1 s) on .NET's `NetworkChange`, with the metered flag on Windows.
      - The documentation says that available is not reachable.
      - M1 checks the page events in Windows Sandbox or a Linux VM by dropping the virtual network. The example is `docs/app-model.md` 3.12;
    - **logs and crash reports for users. Decided (2026-11-14):**
      - `ILogger` with rotating files in Production (5 × 10 MB by default; level from configuration).
      - Page errors reach the same log with flood limits.
      - An unhandled exception writes a crash record, and the next start sees it through `IAppHealth.LastCrash`. Spar never uploads anything.
      - `diagnostics.collect()` and `IAppHealth.CollectAsync` pack a zip into a grant.
      - **Every folder is configurable** (user, 2026-11-14) with `spar.Folders(f => f.Data(…).Logs(…).Crashes(…))`, each with a per-user default, and test runs move them all under their own data root.
      - The example is `docs/app-model.md` 3.13.
  - **Added by the user (2026-11-14), later:**
    - automatic updates and relaunch (M4);
    - installers;
    - localisation, together with the invariant-globalisation default;
    - starting at login;
    - recent documents and jump lists;
    - always on top, fullscreen and kiosk mode;
    - system-wide shortcuts;
    - spellcheck;
    - sounds.
  - The common case takes one short, discoverable call. Defaults are sensible on both OSes, so platform differences never show in app code.
  - Every feature gets a compiled example in `docs/app-model.md` before it is implemented, and is designed with the user one feature at a time.
  - Ideas to discuss, not decided:
    - **decided (2026-11-14):** the tray is declared in `SparApplication.Create`: `spar.Tray(t => t.Icon(…).Tooltip(…).OnActivate(TrayAction.ShowWindow).Menu(m => m.Item("Open", TrayAction.ShowWindow).Item<IMonitor>("pause", "Pause monitoring", monitor => …).Separator().Item("Quit", TrayAction.Quit)))` and `w.CloseToTray()`. App services change it through the injectable `ITray` (tooltip, icon, checked and enabled items, `IsAvailable`); the page reaches it only through the app's contracts. On Linux, a diagnostic warns when the click action is missing from the menu, and `CloseToTray` minimises where no tray exists. The example is `docs/app-model.md` 3.5;
- **decided (2026-11-14):** window placement is `w.Size(1200, 800).MinSize(800, 600).RememberPlacement()`. Placement is remembered by default for the main window, in the app's data folder, and restored on a visible screen; Wayland restores size and maximized state only. App services change windows through the injectable `IWindows` (`Maximize`, `Restore`, `SetSize`, `Focus`). Test runs start from the declared size, because each has a fresh data root;
    - **decided (2026-11-14):** a custom title bar is one window option, `.TitleBar(TitleBar.Custom)`, plus markup: `data-spar-titlebar` on the drag area, and `data-spar-caption`, empty for the SDK's standard buttons or with `minimize`, `maximize` or `close` on the app's own buttons. The SDK posts the layout, and sets `data-spar-window` to `maximized` or `normal` on `<html>`. The example is `docs/app-model.md` 6.9;
    - (the markup idea above is now part of the title-bar decision).
- **File dialogs:** `IFileOpenDialog` and `IFileSaveDialog` on Windows (on the window's STA); GTK or the XDG portal on Linux. The page gets only opaque grants (`fg_…`, in memory, revoked when the window closes).
  - **API (decided, user, 2026-11-14):** `IFileDialogs.OpenAsync` (C#) and `files.open()` (page) return `Result<FileGrant, FileDialogError>`. The cases are `Dismissed`, `AccessDenied`, `Unavailable` and `Busy`; it is `dismissed`, not `cancelled`, so it is not confused with an aborted call.
  - Reads and writes return `Result`s with `FileAccessError`: `NotFound`, `AccessDenied`, `InUse`, `TooLarge`, `GrantExpired`.
  - A command can take a `FileGrant` parameter. Grants are opaque on the wire, tied to the window, limited in number, with an optional expiry.
  - Saves write to a temporary file, then replace the target.
  - The example is `docs/app-model.md` 3.6. A forgotten `Dismissed` fails C# (CS8509) and TypeScript (TS2741).
- **Credentials:**
  - Windows: Credential Manager (target `<AppId>/<id>`, 2,560-byte limit), or DPAPI for larger blobs.
  - Linux: `libsecret`, which needs a session bus and a Secret Service.
  - Page-facing secrets are write-only (`set`, `has`, `delete`).
  - Neither OS isolates apps run by the same user.
- **Resource usage** (user request, 2026-11-14; design and spike pending). A C# service reports how much disk the WebView data uses and how much memory the backend and the web engine use, so an app can show it in its frontend, for example on a settings or "about" page.
  - **Exposure:** the app exposes it through one of its own contracts, so the page sees nothing by default. Spar may also ship a ready-made contract the app opts into.
  - **Live values** (user, 2026-11-14): an app may show them continuously, for example in the main window's status bar.
    - So besides a one-shot read there is a subscription that samples on a timer and pushes the latest values. It keeps only the newest sample, so a slow page never builds a backlog.
    - Proposed:
      - sampling runs only while someone is subscribed;
      - memory and disk get separate intervals, because walking a data folder costs far more than reading process counters (for example about 1–2 s for memory and 30 s or more for disk);
      - a value is pushed only when it changes beyond a small threshold;
      - the interval is a C# option with a floor.
    - **Flexible** (user, 2026-11-14): an app chooses what it samples. For example it may subscribe to memory only and read the folder sizes only on request, such as when a settings page opens. Each measure, which is backend memory, web-engine memory and each data folder, can be subscribed to or read on its own.
- **API decided (2026-11-14):** an injectable `IResourceUsage` with `GetMemoryAsync`, `WatchMemory(interval)` (minimum 250 ms) and `GetDiskAsync` (returns `Result<DiskUsage, FileAccessError>`). `MemoryUsage` holds the app total, the engine (with its GPU process), the app process and each engine process by kind; `DiskUsage` holds profile, app data and cache. The page sees it only through a contract the app writes, typically a coalesced subscription for memory and a command for disk; Spar ships no ready-made contract. The example is `docs/app-model.md` 3.9.
    - The spike measures the sampling cost that sets these numbers.
  - **Windows, candidates to verify:**
    - the browser's processes and their kinds from `ICoreWebView2Environment` (`GetProcessInfos`, and the extended form that maps renderers to frames);
    - each process's private memory through `GetProcessMemoryInfo`;
    - the backend's own memory through `Environment.WorkingSet` and GC counters;
    - disk as the size of the user data folder, or of one profile's folder.
  - **Linux, candidates to verify:**
    - WebKit's web and network processes found under the app's process tree, with PSS from `/proc/<pid>/smaps_rollup` (as `spikes/linux-memory` measured);
    - the website data sizes from `webkit_website_data_manager_fetch`;
    - the folder sizes.
  - **Open:**
    - private bytes, working set or PSS, and the same meaning on both OSes;
    - cost and refresh rate;
    - whether to report the GPU process and shared memory;
    - folders locked by the running browser.
  - **Measured by `spikes/resource-usage`** (NativeAOT hosts; WebView2 157, WebKitGTK 2.52 on 6.0). It answers most of the open points above:
    - **One measure on both OSes: "private memory".** It is resident memory that only the app's processes hold and that no file backs.
      - Windows: `PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize`. It matched Task Manager's "Memory" column in 25 of 25 processes.
      - Linux: `statm` resident − shared, which is within 0.4 MB of `Private_Dirty`.
      - Small page: Windows 88.5 MB (engine 75.0 + host 13.5); Linux 70.7 MB (engine 28.3 + app process 42.4, which holds GTK and WebKit's UI side).
      - The headline is the app total. Engine and app process are its parts, with per-kind detail. The GPU process counts in the engine.
      - PSS and USS are Linux-only detail.
    - **Windows processes:**
      - `ICoreWebView2Environment8.GetProcessInfos` (slot 18) listed browser, renderer, GPU and two utilities. It omits the crashpad handler.
      - It works only on the STA (`UI_E_WRONG_THREAD` elsewhere) and takes 29 µs. So the host re-lists only on `ProcessInfosChanged`.
      - Memory is read with kept handles on a pool thread: 2.5 µs for all processes, 0 bytes allocated.
      - `GetProcessExtendedInfos` (Environment13) maps renderers to frames; feature-detect it.
      - A second instance on the same data folder shares the browser and counts both renderers, one more reason for single instance.
    - **Linux processes:**
      - The app's tree: network process, web process, `xdg-dbus-proxy`, `bwrap`. No GPU process with acceleration off, and no WebKit API for their pids.
      - Found through `/proc/PID/task/*/children` in 107 µs and re-found every few seconds. The sandboxed processes' `/proc` files were readable.
      - `statm` with kept handles: 4.6 µs per sample, 0 bytes. `smaps_rollup`: 0.55 ms.
      - Sampling on the main loop pushed its latency to 1.6 ms, against 0.4 ms off it.
    - **Backend:**
      - Windows: EX2 on the host process (135 ns). Linux: `statm`.
      - Not `Process.PrivateMemorySize64`, which costs 3.2 ms on Windows and reads a ~70 GB virtual size on Linux.
      - `GC.GetGCMemoryInfo` is 0 before the first GC.
    - **Disk:**
      - A folder walk took 0.9–1.1 ms on Windows for 26–149 MB (228–673 files), and 0.2–1.2 ms on Linux. 20,000 files took 3.4–5 ms on Windows and 19 ms on Linux.
      - The walk opens no files, so locked files don't matter. NTFS directory entries lag for files open for writing (1.0 MB short 13 s after the fill).
      - WebKitGTK's `webkit_website_data_manager_fetch` gives sizes for four data types in 1.7 ms, covering 81–84 % of the walk.
      - `navigator.storage.estimate()` is incomplete on WebView2 and missing on WebKitGTK.
    - **Sampling, proposed:**
      - memory every 1 s by default, with a 250 ms floor;
      - disk every 30 s by default, with a 5 s floor, or on request;
      - push only on a change over 1 % or 1 MiB.
      - The sampler allocated 0 bytes per tick.
    - **Decided (user, 2026-11-14):** live samples use a coalesced subscription (section 2c, "Subscriptions"). It keeps the newest value and sends no `gap` events.
    - **Not tested:**
      - a cold file-system cache, and Defender's effect;
      - several windows, a heavy page, crashes;
      - Linux with a GPU;
      - the crashpad handler's memory;
      - posting samples over the real bridge;
      - the oldest supported Runtime.

### 3b. Page security: navigation lock, Content Security Policy and response headers (verified by `spikes/csp-navigation`; some choices pending)

- **Navigation lock, always on in every host** (not configurable):
  - cancel top-level and frame navigations outside the app origin (`NavigationStarting` and frame navigation on WebView2, `decide-policy` on WebKitGTK);
  - block new windows (`NewWindowRequested`, `window.open`, `target="_blank"`);
  - external links open in the system browser only through an explicit SDK call, never by navigating the WebView;
  - **WebView2 also needs `AddWebResourceRequestedFilter("*", DOCUMENT)`,** answering foreign documents with 403. Without it, every cancelled GET navigation still reached the network, because the cancel races a request that has already started;
  - hosts never answer with redirects: WebKitGTK does not follow a redirect from a scheme handler.

  The lock held on both hosts for every case tested: `location` changes, meta refresh, unknown schemes, form posts, iframes, `window.open`, `target=_blank`, and middle, Ctrl and Shift clicks.
  - This sits under the bridge's origin and document checks, which stay; CSP does not replace either.
- **A strict CSP by default.** Everything an app needs comes from its own origin, so the base policy allows only `'self'`:

  ```
  default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self';
  connect-src https://app.localhost; worker-src 'self'; manifest-src 'self';
  base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none';
  report-uri /_spar/csp-report
  ```

  Anything else (a font CDN, CSS-in-JS needing a nonce or `'unsafe-inline'`) is a conscious addition.
  - **`connect-src` names the origin with its scheme** (`https://app.localhost` on Windows), because `'self'` also allows `wss://` to the same host, and WebSockets bypass the host (`spikes/desktop-origin`). The Linux value for `spar://app` still needs a check.
  - **Verified, with one difference:** the React production bundle ran with 0 violations on WebView2, Chromium (loopback) and WebKitGTK 4.1 and 6.0, honoured as a response header on every host; `<meta>` gave the same results. That run used `connect-src 'self'` on the origin `https://app.spar.invalid` (`spikes/csp-navigation`). The policy above, with `connect-src https://app.localhost` on `app.localhost`, has not been run with the bundle; `spikes/desktop-origin` checked only partial policies (the fetch directives). To run in M1.
  - **It also blocks** same-origin iframes, and WebAssembly. Both become C# opt-ins. Correction from `spikes/webkit-wasm-off`: WebKitGTK 2.52 does enforce the block, but only when a module is instantiated (`compile`, `Module` and streaming compile succeed; every instantiate path fails). 2.54 also blocks compiling. `validate` always succeeds.
- **Declared in C#,** where the Spar application is configured:

  ```csharp
  var app = SparApplication.Create(args, spar =>
  {
      spar.ContentSecurity(csp => csp.FontSrc("https://fonts.gstatic.com"));
  });
  ```

  - Values must be constants, and the call must sit directly in the `Create` configuration, so the build can read it without running code. Anything else is an analyzer error at its location.
  - The generator puts the policy in the generated catalog for the host. The client step writes it to `obj/spar/client/csp.json` inside `@spar/app`, where the Vite plugin reads it relative to itself on every page load.
  - In Debug the host checks that the policy it ran matches the generated one.
  - **Verified by prototype:** one analysis drives the generator, the analyzer and an extractor that writes `csp.json` without building, in 109–574 ms. The generated policy equalled the runtime one. Five misuses gave the same located errors from the build and from the extractor: a non-constant value, a call inside `if`, a helper method, an invalid source, a delegate variable. The extractor needs the project's implicit global usings. IDE squiggles were not checked.
- **Sent as a response header** on the HTML, by the host in Release and test variants and by the Vite plugin in Debug.
  - In Debug the plugin adds a per-request nonce (`html.cspNonce`) for Vite's inline preamble and styles, and, with the page kept on `https://app.localhost` (section 4), the HMR WebSocket `ws://127.0.0.1:<port>` in `connect-src`. Served directly from Vite, `'self'` already allowed it.
  - The dev HTML is sent `no-store` without an ETag, because Vite's ETag let a cached page keep an old nonce.
  - Measured in Chromium: Fast Refresh in 37–47 ms with state kept, CSS updates in 12–15 ms, the error overlay in 26–31 ms, 0 violations. A component update took about 90 ms in WebView2 and about 80 ms in WebKitGTK.
  - Violations should appear in development first, never only in Release.
- **The CSP goes on every host-served response, worker scripts included** (`spikes/webkit-wasm-off`). A worker takes its policy from its own response. On WebKitGTK a worker script served without the header ran `eval` and every WebAssembly path under a page with the base CSP, and it instantiated a module the page had compiled and posted to it. Frames inherit the page's policy. WebView2 and the Vite plugin are to check in M1.
- **Other headers on every host-served response** (and from the Vite plugin in Debug), next to `Cache-Control: no-store` (section 1):
  - `X-Content-Type-Options: nosniff`, with an exact `Content-Type` for every file type (`text/javascript`, `text/css`, `application/wasm`, `image/svg+xml`, `application/json`, fonts). Honoured on every engine.
    - **The Linux host must repeat `Content-Type` in the header set** passed to `webkit_uri_scheme_response_set_http_headers`. Otherwise WebKitGTK silently blocks the app's own CSS, classic scripts and workers.
  - `Referrer-Policy: no-referrer`. On the desktop no `Referer` was sent even without it; it matters on loopback.
  - `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Resource-Policy: same-origin`. These, and `frame-ancestors`, took effect only on loopback; they are cheap, so keep them everywhere.
  - `Permissions-Policy` denying powerful features (camera, microphone, geolocation, USB, serial, HID, Bluetooth, payment, display capture), with opt-ins declared in C# next to the CSP.
    - Honoured on WebView2 and Chromium. WebKitGTK ignores it, so on Linux the host's `permission-request` handler is the only gate.
    - The host's permission event (WebView2 `PermissionRequested`, WebKitGTK `permission-request`) is denied by default with the same opt-ins. Geolocation was denied in 1 ms with no prompt on both hosts.
    - **On WebView2 the camera never raised `PermissionRequested`.** A `getUserMedia` call without `Permissions-Policy` hung, and a prompt reached the user's desktop, most likely the Windows camera privacy prompt for unpackaged apps. With `camera=()` the call failed in about 250 ms with no host event. So the header is required on Windows, not optional.
    - **Followed up in Windows Sandbox** with real devices disabled (`spikes/windows-sandbox-checks`):
      - **Without a device:** `NotFoundError` in about 1 ms, with no host event.
      - **With Chromium's fake devices:** `PermissionRequested` fires per kind (camera 2, microphone 1). Allow gives a live track and deny gives `NotAllowedError`. Default shows WebView2's own in-window prompt and waits.
      - **`SavesInProfile` defaults to true.** The answer is remembered across processes, and a remembered allow starts the camera with no host event.
      - **`--use-fake-ui-for-media-stream` bypasses the handler entirely.** Only `Permissions-Policy` held under every condition.
      - **`ICoreWebView2Profile4`:** `GetNonDefaultPermissionSettings` lists the remembered answers. `SetPermissionState` to default forgets one, and to deny stops a running track at once.
      - No Windows privacy prompt appeared, because fake devices never open a real camera. The earlier incident with a real device stays untested.
    - **Host rules from this:**
      1. always send `Permissions-Policy`, denying everything not declared;
      2. answer `PermissionRequested` explicitly, and leave it at default only when the author chose the WebView's prompt;
      3. set `SavesInProfile = false` when the app stores the answer. Otherwise "turn the webcam off" calls `SetPermissionState`, which also stops a running camera;
      4. keep ignoring `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` in Production.
  - **Permissions, two layers** (decided, user, 2026-11-14):
    - The author declares which features the app may use at all, for example `spar.Permissions(p => p.Allow(Permission.Camera))`. This sets both the header (`camera=(self)`) and the host gate. Anything not declared is denied with no prompt.
    - **Declaring a feature grants it** when the page asks: the page is the app's own code, so the declaration is the consent. The OS privacy settings still apply.
    - The browser never remembers answers (`SavesInProfile = false`). Remembering is the app's choice.
    - For a declared feature, the author picks how a request is answered: grant it, let the WebView show its own prompt, or call a C# handler. The handler can ask through the app's UI and remember the answer.
    - The app can list and reset remembered answers (WebView2 profile permission state; the WebKitGTK equivalent to check), so an end user can turn the webcam off again from the app's settings.
    - The OS privacy settings (Windows camera and microphone access for desktop apps, the Linux camera portal) still apply on top and cannot be bypassed.
    - `spikes/csp-navigation` checked the deny path for geolocation; the camera result is described above. The grant path (a real camera on both hosts) is an M3 native-services check.
  - `Cross-Origin-Embedder-Policy: require-corp` only as an opt-in, for apps that need cross-origin isolation (`SharedArrayBuffer`, WebAssembly threads). It gave isolation on WebView2 and Chromium, not on WebKitGTK.
  - Not sent: `Strict-Transport-Security` (no network origin), `X-XSS-Protection` (obsolete). `X-Frame-Options: DENY` only if the CSP has to fall back to `<meta>`, which ignores `frame-ancestors`.
- **Requests checked on loopback and by the Vite dev server:** the `Host` header must be the loopback address or `localhost` with the right port (DNS rebinding), besides the token and the `Origin` check. Vite's `server.allowedHosts` stays at its default.
  - **The loopback bridge spike has no `Host` check:** `spikes/loopback` checks only the token and the `Origin`, and the `Origin` only when one is sent. It is not a reference for this rule. Two other spikes implement it, with different answers: `spikes/csp-navigation/Csp.Shared/LoopbackHost.cs` (421 for a wrong host or port) and `spikes/d8-automation/D8.App/Devtools/Server.cs` (403). **Decided (user, 2026-11-14): 403** with the code `forbidden-host` in the body, as Vite answers for a host it does not allow. 421 is avoided because browsers may retry it on a new connection.
  - Vite with the default `allowedHosts` answered 403 for `evil.example` and also for `host.docker.internal`, which the Linux container runs had to add (test only).
- **Trusted Types** (`require-trusted-types-for 'script'`) against DOM injection.
  - Supported by all four engines. The React production bundle works under it, and in Chromium so do the Vite dev client, Fast Refresh and the error overlay.
  - Cost: creating workers or script URLs from strings needs a policy.
  - **Decided (user, 2026-11-14): on by default**, with an explicit C# opt-out (level 2, section 3c). Angular, the dev loop on WebView2 and WebKitGTK, and other edge cases are tested when they come up.
- **Subresource Integrity:** the build adds `integrity` (sha384) to the entry script, CSS and `modulepreload` links, computed by the Spar Vite plugin from the final files, in 73–79 ms per build.
  - Enforced everywhere: a tampered entry stops the page, and tampered CSS is not applied.
  - Lazy chunks are covered through an import map with `integrity`, which works on WebView2, Chromium and WebKitGTK 2.52. The import map is an inline script, so its sha256 goes into `script-src`.
  - **Decided (user, 2026-11-14): on by default**, with a C# opt-out. Every external script or stylesheet an app allows must come with its hash (fonts cannot carry SRI).
  - For self-served files this mostly catches serving or packing faults: whoever can change the executable can change the HTML too.
  - Its real value is on external scripts and stylesheets the user allows. Spar should ask for a hash there (fonts cannot carry SRI).
- **Violations become diagnostics.** Each one goes to the backend log and the devtools `logs`, with the exact directive to add. `spar doctor` shows the effective policy.
  - **Where to listen:** in the host-injected script, not in an SDK module. A module listener missed the violations raised while the HTML was parsed (it saw 24 of 28 on WebView2, 20 of 25 on WebKitGTK).
  - The injected listener and `report-uri` received every violation on every engine. `report-to` delivered only on WebKitGTK.
  - **Decided (user, 2026-11-14): both channels.** The injected listener is the main one. `report-uri` is the backstop for contexts the listener may not see (workers, untested) and for a failed injected script. The host removes duplicates.
  - **On in every build, can be turned off** (user, 2026-11-14): both channels report by default in Development and Production. A C# option turns each channel off, or limits reporting to Development builds. Production logs the grouped summaries; Development also shows each new group with the directive to add.
  - **Flood limits** (user requirement: a badly made page can raise very many violations). Reports must never flood the log, the bridge or the UI thread:
    - **Grouping:** reports are grouped by directive, blocked URI and source location. The first report of a group is logged in full. Later ones only raise its count, written as a summary at most every few seconds.
    - **The page batches:** the injected listener groups and batches in the page too, and posts at most one message per interval. It never posts one message per violation.
    - **Bounds:** the number of distinct groups is bounded per document. Beyond it the host logs one "too many distinct violations" line and drops the rest.
    - **Size:** `/_spar/csp-report` caps the body size and the request rate, and answers 204 to anything over either limit without parsing it.
    - **Off the UI thread:** reports are parsed and logged away from the UI thread.
    - **Numbers:** the intervals and limits are set in M1 from a flood test, for example a page raising 100,000 violations.
- **A build diagnostic** when a Spar-added hash or nonce shares a directive with the author's `'unsafe-inline'`, which browsers then ignore (`docs/research-review.md`).
- **Defaults that avoid surprises:** `build.assetsInlineLimit = 0`, so Vite never turns small assets into `data:` URLs. Verified: with Vite's default the 137-byte logo became a `data:` URL and was blocked.
- **Host-injected scripts** (the bridge, the telemetry settings) run before every page script on both hosts, also under Vite, and the page reads `window.__spar` synchronously. WebView2 exempts them from the page's `eval` ban; WebKitGTK does not.
- **`isSecureContext` is true on `spar://`** even without registering it as a secure scheme.
- **Pending user decisions** (`spikes/csp-navigation/RESULTS.md`):
  - Trusted Types default (decided: on, with a C# opt-out);
  - same-origin iframes in the base policy (decided: blocked, with a C# opt-in);
  - WebAssembly (decided: blocked by default, with a C# opt-in adding `'wasm-unsafe-eval'`). The Linux host also sets `JSC_useWasm=false` through libc `setenv` before GTK and WebKit start, so the `WebAssembly` global is absent in the page, workers and frames (`spikes/webkit-wasm-off`). The opt-in sets it to `true` explicitly;
  - the reporting channel (decided: both, deduplicated, with flood limits);
  - SRI default (decided: on, with a C# opt-out; external scripts and stylesheets need a hash);
  - the camera and microphone on WebView2 (decided: the two-layer permission design and the four host rules);
- **Not tested:**
  - the camera path on either host;
  - `frame-ancestors` and COOP on the desktop;
  - redirects of subresources on WebKitGTK;
  - CSS and overlay hot updates inside the WebViews;
  - Vite `?worker` under Trusted Types;
  - SRI on a large bundle;
  - injected scripts in child frames;
  - a real Linux desktop.

### 3c. Security defaults and overrides (user decision)

The app must be secure by default and follow current best practice, and an author who really needs to can go past a default, consciously and visibly. **Rule (user, 2026-11-14): anything that is a security risk is blocked by default.** An app unblocks it only through an explicit option, and that option's documentation explains the risk it brings, when it is reasonable, and the safer alternative if one exists. Constraints fall into three levels:

1. **Always on.** These are controls that protect against other sites and other processes; turning them off serves no legitimate app:
   - the bridge's origin and document checks;
   - the loopback token, `Host` check and `Origin` rejection (development and tests only);
   - the devtools endpoint's token and lockfile;
   - no remote debugging port opened by Spar.

   **How a loopback page gets its token: decided (user, 2026-11-14), the URL fragment.** The host publishes `http://127.0.0.1:<port>/#token=…` on its ready line and in the devtools lockfile and writes the token nowhere else. The SDK moves the token to `sessionStorage` and removes it with `history.replaceState`. A fragment never reaches the server, a `Referer` or a request log. Rejected: an `HttpOnly` cookie (cookies are not separated by port, so parallel hosts would clash and other local servers would receive it) and injecting the token into the served HTML (anyone reaching the port would get it). Earlier state: in `spikes/loopback` the token (24 random bytes per run) arrives in the URL fragment (`#token=`, printed in the ready line), the SDK moves it to `sessionStorage` and removes it with `history.replaceState`. Through Vite, the dev server's proxy adds it (`spikes/react`). `spikes/browser-telemetry` added `hostHeaders()` so the page's exporter can call its own host. Assets are served without the token. Through Vite the proxy still adds it.
2. **Strict defaults with an explicit override in C#,** declared in the `SparApplication.Create` configuration like the CSP (section 3b), so overrides show in diffs. Spar logs every active override at startup and `spar doctor` lists them. This level covers:
   - the CSP and other headers;
   - permissions;
   - browser settings;
   - browser environment overrides;
   - browser devtools in Production.
3. **Limits on the page only.** File grants hide paths, and page-facing secrets are write-only. The backend can do anything, so an author goes past these by writing their own command. Ready-made options (visible paths, persistent grants for recent files) can come later.

**Production defaults, each with a C# opt-in:**
- **Browser settings:**
  - On Windows:
    - devtools (F12) and the context menu's "Inspect" are off. **This does not disable automation:** with `AreDevToolsEnabled=false`, in-process CDP still worked unchanged (`spikes/d8-automation`). Only a compile-time or trim-time gate keeps the devtools endpoint out of a build, and the production boundary test proves it;
    - browser accelerator keys are off (reload, print, devtools);
    - password saving and general autofill are off, so the SQL tool's password fields are never offered for saving;
    - downloads are denied (`DownloadStarting`);
    - swipe navigation is off;
    - no host objects (`AddHostObjectToScript`).
  - On Linux: no `enable-developer-extras`, no windows opened by JavaScript, no `file://` access, downloads denied.
  - Permission requests are denied without a prompt (section 3b).
- **Browser environment overrides are ignored in Production:**
  - `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER`, `WEBVIEW2_USER_DATA_FOLDER` and `WEBVIEW2_RELEASE_CHANNEL_PREFERENCE`;
  - `WEBKIT_INSPECTOR_SERVER`, `WEBKIT_INSPECTOR_HTTP_SERVER` and `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS`.

  The filter is a block-list of exactly these names: everything else passes. In particular the Linux GPU workarounds (`__NV_DISABLE_EXPLICIT_SYNC`, `WEBKIT_DISABLE_DMABUF_RENDERER`, `WEBKIT_DISABLE_COMPOSITING_MODE`) keep working, and `spar doctor` suggests them for blank or white windows.

  The opt-in is `AllowBrowserEnvironmentOverrides()`. Administrator policies set in the registry stay in force. Whether clearing a variable in-process is enough is checked in M1.
- **The browser profile of a shipped app** lives in a per-user, per-app folder, and its location is configurable:
  - Windows: `%LOCALAPPDATA%\<Company>\<App>\WebView2`;
  - Linux: the app's XDG data and cache folders.

  WebView2's own default, next to the executable, fails under `Program Files`. The `artifacts/webview2-profiles/` rule applies only to Spar's repository, its tests and local development.
- **No loopback in Production** (user decision). The desktop host is the only shipped surface.
  - The loopback host is compiled only into development and test variants, like the devtools, and the production boundary test proves it is absent.
  - No real production use was found. Remote use from another machine would be a different product: a web server with TLS and authentication. Revisit only if a real consumer needs it.
- **Publishing a non-Production variant** is a build error. An explicit property (`SparAllowNonProductionPublish`) turns it into a prominent warning, for example for a support build.
- **The devtools lockfile** is readable only by its user: `0600` in `XDG_RUNTIME_DIR` on Linux, the user's temp folder on Windows.
- **Supply chain:**
  - The build runs `npm ci --ignore-scripts --no-audit`, with a property to allow scripts.
    - **Verified** (`spikes/npm-ignore-scripts`): the installed tree was file-for-file identical to a normal `npm ci`, and the native packages (Rolldown, Lightning CSS, TypeScript 7) work. No package in either frontend needs scripts.
    - The flag goes on the `npm ci` command line only: in `.npmrc` it would also skip the app's own `pre`/`post` script hooks.
    - After install, the build warns about lockfile entries with `hasInstallScript` that match the current OS.
  - NuGet audit runs as an error in Spar's repository, and as warnings by default in apps.

**Later (M4):**
- signing the executable and the Linux packages;
- privacy defaults for telemetry (no SQL text or parameters in spans unless opted in);
- redaction of connection strings in logs.

### 3d. Light and dark theme without flicker (verified by `spikes/theme-startup`; choices decided 2026-11-14)

**Goal:** no white flash in light or dark mode at startup, on a theme change, or while the page loads.

**Today's host, dark mode (baseline):**
- **Windows:** the client area was white for 333–366 ms (20–22 frames) until the app's first frame, and the title bar stayed light. The white came from the window itself, which had no class brush, not from the WebView.
- **Linux:** the GTK window colour showed for about 60 ms, then the WebView's white for 14–25 ms, then the page in light colours even in dark mode.

With the layers below, both themes on all three hosts (WebView2, WebKitGTK GTK3 and GTK4) gave **0 white or wrong-colour frames**.

- **Declared in C#** next to the CSP, with constant values the build can read:

  ```csharp
  spar.Window(w => w.Background(light: "#ffffff", dark: "#1f1f1f"));
  ```

- **The host paints before the page exists.** The minimal set, measured:
  - **Windows:**
    - the window class brush, plus a `WM_ERASEBKGND` handler that paints the current colour; this alone removes all the white;
    - `DWMWA_USE_IMMERSIVE_DARK_MODE` set before showing, for a dark title bar from the first frame;
    - the profile's `PreferredColorScheme`, needed when the app's choice differs from the system;
    - a **transparent** WebView default background (`ICoreWebView2ControllerOptions3` at creation), so the window colour shows through.

      In dark mode, while no document has painted yet (slow HTML, or a controller created before navigating), WebView2 paints Chromium's dark canvas `#121212` for about 900 ms, whatever opaque default background is set. Transparent was the only setting with 0 wrong frames in every case.

      No `WEBVIEW2_DEFAULT_BACKGROUND_COLOR`: it was not needed, and it overrides the creation options.
  - **Linux:**
    - a GTK CSS provider for the window;
    - GTK's dark preference (`gtk-application-prefer-dark-theme`). `prefers-color-scheme` follows it on GTK3 and GTK4, including live changes;
    - an **opaque** `webkit_web_view_set_background_color`. Transparent shows black for 11–22 ms on GTK3 without a compositor.

    GTK4's header bar follows the dark preference; Ubuntu's Yaru theme keeps the GTK3 header bar light.
- **The page style:** the build adds `<meta name="color-scheme" content="light dark">` and a small style with the same colours to `index.html`, and adds that style's hash to the CSP automatically. The hash works on both engines.
  - It is not what removes the flash (no measurable effect once the host layers are in place). Keep it for the splash, form controls and as a safety net.
  - Prototype: a Vite plugin inserts the meta, the theme style (150 bytes) and the splash, hashes the styles as emitted, and writes the hashes for the host.
- **Which theme:**
  - **The system's,** by default: `AppsUseLightTheme` plus `WM_SETTINGCHANGE` (`ImmersiveColorSet`) on Windows. On Linux, read the XDG settings portal's `color-scheme` once and listen for its change signal, falling back to GTK's settings.
    - The fallback is required: under WSLg the portal runs but exports no Settings interface. With a Settings portal in a private session, the read took about 1 ms, and the page saw a change 23–28 ms after the signal.
    - Portal value 0 (no preference) means light. The fallback must never read back GTK settings the host set itself.
  - **Or the app's own choice:** the SDK's `setColorScheme("light" | "dark" | "system")`. The host saves the choice in the app's data folder and applies it at the next start, before the page loads. Measured: 0 wrong frames on all three hosts.
- **Live changes:** the host updates the window, title bar, WebView background and `prefers-color-scheme` together. The page reacts in 6–21 ms. Expect at most one mixed frame: 3 of 12 switches had the title bar one frame ahead.
- **Showing the window:** show it at once (visible in 1–5 ms on Windows, 35–60 ms on GTK3, 120–190 ms on GTK4, in the right colour).
  - A hidden window does not render on either engine, so `ui-ready` never arrives.
  - On Windows, a window cloaked with DWM until ready works, but a slow start then shows nothing for over a second.
- **Decided (user, 2026-11-14):**
  - **Showing:** the window shows at once, in the theme colour, never cloaked.
  - **WebView2 background:** transparent, not `#121212`. M1 measures what it costs.
  - **Splash delay:** it runs from the page's first frame, through a CSS animation delay.
  - **Blocked rendering:** the SDK requests an animation frame as soon as the bundle starts. M1 confirms why this fixes the minimum-visible count.
  - **Live theme changes:** at most one mixed frame (the title bar one frame ahead) is accepted.
  - **GTK theme name:** no dark theme name is set. GTK4's header bar follows the dark preference, and Spar uses GTK4 only.
- **Not tested:** a real light Windows theme and a real system `WM_SETTINGCHANGE`; an on-screen window, several monitors or DPI scales; Windows 10 and WebView2 Runtimes older than 157; the cost of a transparent WebView2 background; Linux on a real desktop (Wayland, compositor, portal, libadwaita); the Vite dev path; the C# declaration of the colours.

**Optional splash screen** (user decision; measured by `spikes/theme-startup`):

- **Where it lives:** static HTML and CSS in `index.html`, inside an element with a known id. It shows as soon as the HTML is parsed, before any JS runs.
- **The splash is an opaque cover in the theme colour from the first frame; only its mark fades in after the delay.** The first design, a transparent element that faded in, showed app → splash → app in manual mode.
- **Measured:**
  - a fast app (bundle +300 ms) showed 0 splash frames on all hosts;
  - slow apps always showed the splash exactly once (109 runs, never twice), for at least about 470 ms;
  - the host's `ui-ready` came 16–34 ms after removal;
  - `aria-busy` and reduced motion worked; without the CSP hash the splash simply doesn't show.
- **The delay runs from the page's first frame.** WebView2 holds the first frame of a page with nothing contentful for about 550 ms, so the effective threshold there is about 0.9–1.2 s after the window. Static text in the HTML brought the first frame to 172 ms.
- **Blocked rendering:** 1500 ms of synchronous start-up work showed no splash on WebView2, because no frames render; the plain theme colour shows instead. The minimum-visible guard miscounted under late work until the SDK requested an animation frame at bundle start (fixed in 12 of 12 runs; mechanism unconfirmed).
- **Proposed defaults:** 350 ms delay from the first frame, 400 ms minimum visible, 120 ms fade-in, 150 ms fade-out.
  - It is not native, because that would be a second visual system on two hosts, and the window colour already covers the WebView start.
  - It is not a SPA component, because that would appear only after the bundle has run, which is the wait it should hide.
- **No splash on a fast start,** to avoid a second kind of flicker: the mark appears only after the delay, through a CSS animation delay, so no JS is needed. Once visible, it stays for the minimum time before fading out. Both values are configurable.
- **Removal:** the SDK removes it at `ui-ready` by default. An app that wants to wait for its first data switches to manual mode and calls `spar.ready()`. The `ui-ready` the host reports (and `spar snap`, tests and agents wait for) comes after the splash is gone.
- **Uses the theme:** the colours declared in C#, `prefers-color-scheme`, and images from the app's own origin. Its styles go through the same build step as the theme style, so their CSP hash is added automatically.
- **Accessibility:** `aria-busy` while shown, and no animation under `prefers-reduced-motion`.
- **Template:** `dotnet new spar-app` ships a commented-out example.
- **Later, if the SQL tool needs it:** start-up progress messages from the backend.

### 4. Development loop

- **Debug embeds no bundle.** The app, desktop or loopback, starts or reuses the Vite dev server and serves the page from it. The desktop bridge stays in-process.
  - Vite records itself in `obj/spar-dev-server.json`. It is reused across backend restarts, ends with its launcher's standard input, and has a 60 s idle watchdog.
  - Measured: Fast Refresh inside WebView2 in 39 ms; a contract change restarts in about 5.4–6.1 s with Vite reused.
- **Node at run time in Debug is accepted** (user decision).
  - Without Node, the app stops with a clear error saying how to install it, or how to use the Release bundle; `spar doctor` checks the Node version.
  - `-p:SparDevServer=false` makes Debug embed the bundle through the same code as Release. It has no hot reload, and shows startup, theme, CSP and headers exactly as users will see them.
  - **Debug can keep `https://app.localhost`** (verified by `spikes/desktop-origin`). The host fetches each request from Vite in-process, so Debug and Release share storage, CSP and secure-context behaviour.
    - Medians, direct against proxied: Fast Refresh 25 and 23 ms, state kept 5 of 5 times, CSS 5 and 9 ms, error overlay 8 and 9 ms; a full page load costs about 35 ms more (117 against 153 ms).
    - Vite's `server.hmr` must point the client at `ws://127.0.0.1:<port>`. With the defaults it tries `wss://app.localhost`, which leaks and gets no updates. The Debug CSP adds that WebSocket to `connect-src`.
    - **Decided (user, 2026-11-14): this is the Debug default.** Serving straight from Vite stays an option. Still to check in M1: Vite's nonce under the strict CSP through the proxy, and the Linux path through the custom scheme.
    - **What proxies:** the host itself, with no proxy server or extra package. On Windows a `WebResourceRequested` filter for `https://app.localhost/*` takes each request, the host fetches it from Vite's loopback URL with `HttpClient` and answers with `CreateWebResourceResponse` through a deferral. On Linux the `spar://app` scheme handler does the same and answers with a `WebKitURISchemeResponse`. The HMR WebSocket is not proxied: the page connects to `ws://127.0.0.1:<port>` directly. All of it is Development-variant code, absent from Production.
    - **Proxy requirements** (`spikes/desktop-origin/Origin.Win/Host.cs`): forward `Accept` and the `Sec-Fetch-*` headers, pass `Content-Type` through, never cache; complete each request through a deferral on the STA, so a slow Vite holds the request but not the pump; `HttpClient` with `UseProxy=false` and no automatic decompression. Each proxied request took 0.5–15.5 ms.
    - **For HTML, drop `If-None-Match` and `ETag`.** The spike's proxy forwards `If-None-Match` to Vite. `spikes/csp-navigation` showed that a 304 then keeps a page whose nonce no longer matches the new header (2 violations, page stuck), so that proxy code as written would bring the bug back.
- **Hot Reload signal (decided, user, 2026-11-14), Debug only:**
  - After each C# edit that Hot Reload applies, the host's `MetadataUpdateHandler` sends a `backend-updated` event to the open pages. It carries a revision number, the time and the names of the updated types.
  - The SDK offers `onBackendUpdate(handler)`, for example to refetch the current view's data. In Release it is a no-op that the bundler removes.
  - **A toast by default:** Spar's development overlay shows a small, dismissable toast for each update, such as "Backend updated: CopyJobsService (revision 3)". An app can turn it off or show its own from `onBackendUpdate`.
  - **Limits:**
    - The event can arrive after the patched code already ran once.
    - Rejected or failed edits are known only to `dotnet watch`, so they stay in its terminal.
    - Contract and DTO edits still restart the app, as before.
- **Release and test variants embed the bundle.** Each variant has its own `obj`/`bin`.
- **Launch profiles:** `Desktop` (the default, first in `launchSettings.json`; user decision) and `Loopback`. If the desktop cannot start (no display), the app stops with a clear error suggesting `dotnet run -lp Loopback` and never falls back on its own; `spar doctor` reports the same. The loopback dev session keeps one port and token across restarts. An orchestrator such as Aspire may assign the port, token and dev-server URL through the environment.
- **`dotnet watch`** does not mark its builds, so the build never tries to detect it. The runtime decision above makes detection unnecessary.
- **Agents:** after external edits, the CLI build or `spar dev` is the loop. Start real processes directly, so their whole tree can be stopped.

### 5. Testing

Pick the smallest level that can show the failure:

1. **Direct:** call services for domain rules.
2. **Managed:** run the real `Main` in the test process, with service overrides, through real serialization and dispatch. An in-process factory (with test hooks in development builds only) is under evaluation.
3. **Loopback and browser:** the backend serves the bridge over authenticated loopback HTTP, and Playwright drives the real SPA in Chromium. It runs on Windows and Linux.
4. **Desktop:** an instrumented NativeAOT executable in an offscreen window, on both hosts.
   - Windows: in-process CDP (D8).
   - Linux: Xvfb in CI, JavaScript evaluation, and trusted input through XTEST with `GDK_BACKEND=x11` (decision "Trusted input in Linux desktop tests: XTEST").
   - Service overrides come only from explicit `TestConfiguration.cs`.
5. **Integration with real resources (optional):** an Aspire AppHost with containers (SQL Server) driven by `DistributedApplicationTestingBuilder`. Spar never depends on Aspire at run time.
6. **Sandbox:** the exact production executable in an offline Windows Sandbox (M4).

**Rules:**
- **Projects:** `MyApp.Tests` (fast, levels 1–2 and API-level loopback, run by every `dotnet test`) and `MyApp.EndToEndTests` (Playwright, the desktop exe, Aspire; run explicitly and in its own CI stage).
- **Framework:** plain `xunit.v3`; `xunit.v3.aot` for a future AOT test project. Playwright tests stay on CoreCLR.
  - xUnit writes no report files, but `dotnet test` (MTP) still leaves an empty `TestResults/` folder in the working directory (`spikes/test-frameworks`, `spikes/d3-variants`; `spikes/TestResults/` is one). The test-project defaults must keep it out of authored folders; how (for example `--results-directory` under `obj/`) is open for M1.
- **Paths:** no absolute paths in tests and scripts; the app path is stored relative to the test output.
- **Required host tests** (from the runbook): close during controller creation, close during a long RPC or stream, navigate with an RPC in flight, close one of two windows sharing an environment, and repeated open/close. Each ends with all pending work final, stale replies dropped, and the profile reusable.

**Test types** (2026-11-14, from the spike review; ⚠ marks a type with an open issue, listed under the table).

| # | Type | Who | What it checks | Runs with | Speed | Spar provides | |
|---|---|---|---|---|---|---|---|
| 1 | C# unit | App, Spar | Services and domain rules, called directly | xUnit | ms | Nothing special; a `Result` reads with `switch` or `Assert` | |
| 2 | Command (managed) | App, Spar | A command through real serialization and dispatch, services replaced, no window | In-process factory, or the exe on loopback | ms (factory) / ~0.6 s (exe) | The factory or the launched-app fixture | ⚠ A |
| 3 | Page | App, Spar | Page code against a fake backend: errors, failures, cancellation, stream events | Vitest/Node or a real browser | ms | `@spar/app/testing`: fake transport, `Result` factories | ⚠ B |
| 4 | Loopback end-to-end | App, Spar | Real backend and page together, Chromium via Playwright over the loopback bridge | The exe or an in-process backend, plus Playwright | 0.6–1.4 s | Launched-app fixture, profile per test, locators | ⚠ C |
| 5 | Desktop end-to-end | App, Spar | The real window (WebView2, WebKitGTK under Xvfb): selectors, trusted input, screenshots | Offscreen window | seconds | `WaitForUiReady`, selectors, input, screenshots, scenes, fixed clock | ⚠ C, D |
| 6 | Integration with real resources | App | The app against disposable real services (a SQL Server container) | Aspire's test builder or Testcontainers | 42–48 s per run (`spikes/aspire`) | Port, token and URL through the environment | ⚠ E |
| — | Contract compatibility | App, Spar | The committed contract snapshot | The build (SPAR100 in CI) | build time | Built in | |
| — | Visual snapshot | App, Spar | `spar snap` with scenes: PNG and accessibility tree | `spar snap` | seconds | Built in (M2) | ⚠ F |
| 7 | Generator and analyzers | Spar | Exact diagnostics and locations, the analyzer corpus, code fixes, false positives on a sample app | xUnit + Roslyn, SARIF | ms–s | — | |
| 8 | Generated client | Spar | `.d.ts` on TypeScript 6 and 7 at the strictest settings, deliberate mistakes with exact codes, `.js` against `.d.ts` | tsc, Node | s | — | |
| 9 | Serializers and wire | Spar | Differential against System.Text.Json, seeded fuzzing, golden vectors shared by C# and TypeScript, JIT/AOT parity | xUnit, Node | s–min | — | ⚠ G |
| 10 | Runtime and protocol | Spar | Dispatcher, bridge codec, cancellation, `Result`, stream rules | xUnit | ms | — | |
| 11 | Build | Spar | Incremental and no-op builds, variants, the production boundary, a clean clone, `-mt` | MSBuild with binlogs | min | — | ⚠ H |
| 12 | Package | Spar | A consumer using Spar through packages only, isolated caches | `dotnet` in a temp folder | 36–62 s cold restore | — | ⚠ H |
| 13 | Host lifecycle | Spar | Close during startup or a call, navigation with a call in flight, two windows, shutdown, profiles, startup errors, the heartbeat | Both hosts | seconds | — | ⚠ I |
| 14 | Windows Sandbox | Spar | Real input, dialogs, tray, focus-dependent features, the production exe offline | Windows Sandbox | minutes | — | ⚠ J |
| 15 | Performance regression | Spar | BenchmarkDotNet, page benchmarks on three engines, build timing, against baselines | BenchmarkDotNet, runners | minutes | — | ⚠ K |
| 16 | Documentation | Spar | `docs/app-model.md` examples compile and match their copies | `npm run check`, `dotnet build` | s | — | |
| — | Needs the user | Spar | DPI, Narrator, a real camera, Wayland input, real desktops | The user at the PC | — | — | manual |

**Open issues** (to investigate further, with more spikes where needed; user, 2026-11-14):
- **A. The managed level's form.** The in-process factory is fast but shares the process's console, environment, current directory and statics. The exe is isolated but about 25 times slower. The factory decision was withdrawn; open.
- **B. Where page tests run.** In Node with Vitest, in a real engine, or both. Also whether the fake transport behaves closely enough to the real bridge.
- **C. Replacing services in end-to-end tests (user concern, 2026-11-14).**
  - A test method cannot replace services in a separate exe: its lambdas cannot cross the process. The earlier experiment's source extraction cost about 670 lines.
  - Running the real app risks touching real data unless isolated.
  - **Ideas, none decided:**
    - a fresh data root per run, from which every Spar location derives (profile, app data, settings, logs, credential namespace, single-instance name);
    - the `Testing` environment;
    - named configurations in `TestConfiguration.cs`;
    - an in-process backend for loopback and desktop tests, with the real exe kept for a few smoke tests.
  - In-process desktop hosting is unproven.
  - Also open: whether loopback tests share one browser.
- **D. Desktop specifics.**
  - Focus-dependent features (clipboard, user activation, the first click on an inactive window) fail offscreen.
  - Linux desktop tests need the Docker image. Whether Windows machines may use WSLg, and whether Spar publishes the 7 GB image or only its Dockerfile, are open.
  - Selectors (role and name only, or test ids too) and how scenes are declared are open.
- **E. Integration cost and quirks.** 42–48 s per run. A `localhost` → `::1` hang. Aspire writes keys to `%LOCALAPPDATA%`.
- **F. Visual snapshots** need determinism (clock, fonts, DPI), which is not proven.
- **G. The fuzz budget:** a fixed seed budget on every run or nightly, and whether a minimised corpus is committed.
- **H. Build and package tests** are slow and noisy: 5 s process stalls, cold restores. They need isolation (MSBuild server off, no node reuse, unique compiler id, isolated caches) and medians with ranges.
- **I. Profiles for parallel tests:** one folder per process or a shared folder, cleanup after crashed runs, and the browser-exit wait.
- **J. Windows Sandbox:** fixed at 96 DPI, no network, minutes per run. Whether app authors get the runner is open.
- **K. Performance:** which suites run in CI, ratio or absolute thresholds, which machine sets the baselines, and the noise policy.

**Test framework proposal** (from the spike review in `docs/evidence/test-framework-review/`, 2026-11-14; each point is decided with the user one at a time):

- **What app authors get:**
  - **`Spar.Testing`** (NuGet), for the managed, loopback and desktop levels:
    - the in-process factory with `ConfigureServices` overrides, `Services`, an injectable `TimeProvider` and a per-app log sink;
    - a launched-app fixture: ready line, stdin lifecycle, kill with the parent and after a deadline, output capture, an app path that also works from a publish folder;
    - desktop helpers: `WaitForUiReady`, tree and selectors, trusted click and fill, screenshots, JavaScript evaluation, scenes and the fixed clock shared with `spar snap`.
  - **`@spar/app/testing`** for pages:
    - the scripted fake transport (success, declared error, failure, hang, delays, stream events, abort semantics, a call log);
    - `Result` factories for test doubles;
    - a reset for `configureSpar` state;
    - framework notes for React StrictMode, Angular zones and `OnPush`, Vue scope disposal, and TanStack `retry: false`.
  - **Template defaults:**
    - results under `obj/`;
    - a `Browser` trait;
    - app-launching tests not run in parallel;
    - `CaptureConsole`, with xUnit1051 as an error;
    - `TestContext.Current.CancellationToken`.
- **Shared infrastructure** (used by both Spar's tests and the app-facing kit):
  - **Profile manager:** InPrivate on Windows or ephemeral data on Linux per test, inside one folder per test process, created with identical options: under `artifacts/webview2-profiles/<run>/` on Windows, and under `artifacts/linux-homes/<run>/` on Linux (decision "Guidance review applied", point 4). It makes a bounded wait for `BrowserProcessExited`, and cleans up folders left by crashed runs only when no process uses them.
  - **Process ownership:** processes are tracked by job object or profile folder. A leftover-process assertion runs after each host test.
  - **Linux isolation:** a C# helper builds the child environment: private `HOME` and every `XDG_*` folder under `artifacts/linux-homes/<run>/`, `XDG_RUNTIME_DIR` and a D-Bus session. It clears `JSC_*` and inspector variables.
  - **Windows:** offscreen, shown, never-activated windows with a first-frame gate. Focus-dependent checks go to Windows Sandbox or the user.
  - **Waits:**
    - every wait awaits its predicate and has a deadline;
    - a timed-out call is abandoned, never replayed;
    - output is structured (JSON lines) and kept in files.
- **Spar's own tests:**
  - **Generator and analyzers:**
    - exact `(id, located text)` diagnostics for the generator;
    - one analyzer corpus format (`//~ ID` markers, `Kind` headers, an exact set over SARIF), with a fix-and-recompile harness and a false-positive run on a sample app;
    - the TypeScript exact-code negative runner on TypeScript 6 and 7.
  - **Serializers and wire:** a differential test against System.Text.Json, a seeded fuzz lane with JIT/AOT pairs, and shared golden wire vectors read by the C# and TypeScript tests.
  - **`Spar.BuildTests`:**
    - isolation (MSBuild server off, no node reuse, unique compiler id);
    - a step matrix from binlog replay;
    - "authored files untouched";
    - a clean clone;
    - the marker-byte production-boundary test;
    - a `-mt` strict lane.
  - **`Spar.PackageTests`:** the package-consumer recipe.
  - **Host-level runners:**
    - a Windows Sandbox runner;
    - one Linux CI image (non-root uid 1000, `--init`, the bwrap settings, Xvfb with an optional window manager);
    - library hiding for the startup-error paths.
  - **Performance:**
    - BenchmarkDotNet with a standard configuration;
    - a cross-engine page benchmark runner (Node, WebView2, WebKitGTK);
    - a build-timing harness with medians and ranges that logs stalls instead of averaging them.
- **Rules:**
  - a check asserts; it never only prints;
  - a missing tool (Node, a browser, Visual Studio) is an explicit skip, never a pass;
  - tests never touch the user's desktop, settings or home folder.

### 6. Agent facilities

- **Devtools:** an in-process devtools endpoint, only in Development and DesktopTest builds, enabled per session. A lockfile with a bearer token. Small tools:
  - `tree`, `screenshot`;
  - `click`/`fill`/`waitFor`;
  - `contracts`, `call`, `calls`, `logs`;
  - `version`.

  They read the host's instrumentation, so `calls` and readiness come for free. On Linux the same tools need a non-CDP implementation.
- **`spar dev`:** builds, launches, and reloads on request. A failed reload returns compiler diagnostics at file:line in-band.
- **`spar snap`:** a one-shot capture with named scenes and a fixed `TimeProvider`. It builds or reuses the development build, starts it offscreen, waits for UI ready, optionally replays an input script (steps addressed by accessible name or test id), writes a PNG, the accessibility tree and the bridge call log, then exits. No session, no port.
- **`spar doctor`** (user decision): checks the machine and the project, and reports each finding with a stable id, a severity and the fix. Text by default; `--json` and distinct exit codes for agents.
  - **Machine:**
    - the .NET SDK against `global.json`, and `Spar.Sdk` resolution;
    - Node and npm versions;
    - on Windows, the WebView2 Runtime, the AOT prerequisites and long paths;
    - on Linux, WebKitGTK, GTK, ICU, OpenSSL, plus `clang` and `zlib` for AOT, each with the install command;
    - Docker and Playwright browsers, only when the end-to-end tests need them.
  - **Project:** the `SparFrontendPath` folder, the `@spar/app` dependency, the Vite plugin, the `build` script, the lockfile and `node_modules`, the contract snapshot, the launch profiles.
  - **Shared checks:** the project checks are the same code the build uses for its diagnostics.
- **`spar init frontend`** (user decision): wires an existing frontend (made with `npm create vite`, a Vue scaffolder, …) into a Spar app.
  - It adds the dependency, the Vite plugin and, if needed, `SparFrontendPath`.
  - It shows the diff before writing, or prints the snippet when `vite.config` is too unusual.
  - `--framework react|vue` can run the official scaffolder first.
- **The `spar` tool** ships with the same version as `Spar.Sdk` through the local tool manifest (`dnx spar …` also works).
- **Agent guide:** ships in the SDK in Agent Skills format.
- **Skills for app authors, as a plugin** (user, 2026-11-14): besides the copy inside the SDK, the app-author skills are published as an agent plugin that users install, from a marketplace in this repository: the plugin in `plugins/spar/`, the marketplace file at the root (decided, user, 2026-11-14).
  - These are different from `.claude/skills/`, which guide work on Spar itself.
  - The plugin version follows the SDK version, so a skill never describes an API the installed SDK lacks.
  - Its skills get the same evaluations as the other skills.
- **Evaluation:** uses the .NET skills `eval.yaml` format (prompts, graders, tokens, time, activation, pass rate).
  - Profiles compared: plain `dotnet` tools, then `spar` CLI, then `spar` MCP. Only one interactive surface survives. Unused tools are removed.
  - Spar's own skills get evaluations too, and a skill that doesn't help is removed.

### 6b. Observability: optional OpenTelemetry, backend and client (user decisions)

- **Always on, BCL only (M1 core):** one `Activity` and one `spar.call.duration` sample per bridge command. No listener means no work; about +105 KB.
- **Optional layer (an opt-in package, never required):** the OpenTelemetry SDK plus the OTLP exporter, for traces, metrics and logs.
  - It starts only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; no endpoint is configured by default.
  - +4.3 MB on an app that already uses HTTPS; AOT-clean, with export verified from the AOT exe.
- **Client telemetry through the backend:**
  1. The page uses the OpenTelemetry JS web SDK (opt-in module, for example `@spar/app/telemetry`), exporting to a relative path such as `/_spar/otlp/v1/traces`.
  2. Each host serves that path, and the backend forwards the bytes unchanged to the configured OTLP endpoint, only when the layer is enabled.
  3. `traceparent` on every call message gives one trace from a page interaction to backend spans (HTTP, SQL).

  No CORS, and no credentials in the page. Verified on Windows, loopback and Linux: every page call span had its backend child.
- **Split:**
  - The core SDK and hosts carry the tracing hook, trace context on every call, `/_spar/otlp/*` routing (404 without a relay), and a bounded flush before a desktop window closes (without it no page spans arrived).
  - The optional `Spar.OpenTelemetry` package holds the SDK, exporter and relay. The relay forwards bytes unchanged (http/protobuf or gRPC), adds `OTEL_EXPORTER_OTLP_HEADERS`, and backs off with an immediate 503 after a failure.
  - The optional page module loads lazily (27 KB gzip), only when the host reports telemetry on. Nothing waits before mount (user decision):
    - desktop hosts put the settings in the script they inject at document creation, read synchronously;
    - loopback asks `POST /_spar/otlp/config` in parallel with the mount;
    - an app without the layer has no telemetry code and makes no request.
  - No click spans by default (an app opt-in); the SDK offers an explicit span to group an interaction's calls. No `zone.js`.
  - Service names come from `OTEL_SERVICE_NAME`; the page reports as `<name>-page`.
  - Still to cover: trace context on streams and subscriptions.
- **Milestones:**
  - M1: the BCL instrumentation and `traceparent` in the protocol;
  - M2: the optional layer, client export and the Aspire dev experience.
- **Later:** a Spar-owned minimal OTLP exporter (parked).

### 7. Repository and guidance

- **Consumer model:** one app project, one fast test project, and one optional end-to-end project. No app-specific branches in the framework.
- **Agent files:** `AGENTS.md` (`CLAUDE.md` contains `@AGENTS.md`), plus skills in `.claude/skills/` (to be reviewed against this plan; see M0). Portable skills may move to `.agents/skills/`.
- **Plugins:** dotnet/skills plugins are enabled in `.claude/settings.json`.
- **Readable code first,** in C#, TypeScript and generated output (in the skills).
- **Size limits:** 800 lines per file soft, 1,500 hard, 40 source files per folder. `eng/check-size.cs` enforces them, and exception ceilings only go down.
- **Analyzers:** trim, AOT and single-file analyzers run in every project, including tests. Builds are `Deterministic`, and `.editorconfig` sets `end_of_line = lf`. Overrides are agreed and recorded in `.editorconfig`. Today: CA2016 at error in every project (decision "Guidance review applied", point 2), RSEXPERIMENTAL006 off in the generator, and the MSBuild task analyzer's option and severities for the task project (decision "Same-origin iframes blocked; MSBuild task analyzer settings").
- **Warnings as errors covers compiler warnings only.** The root `Directory.Build.props` sets `TreatWarningsAsErrors`, which does not apply to MSBuild and SDK warnings (`MSB*`, `NETSDK*`): NETSDK1229 passed every build in `spikes/cswinrt`. Only `-warnaserror` or `MSBuildTreatWarningsAsErrors` fails them. **Decided (user, 2026-11-14):** the root `Directory.Build.props` sets `MSBuildTreatWarningsAsErrors` for every project outside `spikes/`. A probe warning (MSB9999) failed a build under `tests/` and stayed a warning under `spikes/`. Exceptions go in `MSBuildWarningsNotAsErrors`, agreed ID by ID with their reason. The app template sets it too; `Spar.Sdk` does not force it on apps, because a new NuGet advisory warning would break existing builds.
- **Docs:**
  - `docs/decisions.md` (append-only log), `docs/pitfalls.md` (known pitfalls), `docs/evidence/` (measurements), investigation guides in `docs/`.
  - User-facing docs cite official platform docs only and don't name other frameworks.
  - **A documentation website** (user, 2026-11-14): a good-looking, searchable site for app authors, built from the user-facing docs in this repository and published with each release. **Decided:** Astro Starlight, published to GitHub Pages from CI on each release. The pages stay plain Markdown, readable in the repository. The TypeScript reference comes from TypeDoc. The C# reference comes from the XML doc comments, through DocFX's Markdown output (to verify) or a small Spar generator.
    - It holds the guides, the API reference generated from the C# and TypeScript doc comments, and the compiled examples.
    - Its examples are compiled like `docs/app-model.md`, so they never go stale.

## Milestones

### Checks that need the user at the PC

Agents run nothing here unattended: each check needs the user present, real hardware, sound, or a change to the user's own settings. **Real input** (mouse, keyboard, drag and drop, focus) is not a hard requirement (user, 2026-11-14): it may be simulated or driven in Windows Sandbox, a Linux container or VM, or WSL, or on the user's desktop through computer use after asking whether they are using the monitor. Rows that need only real input can move out of this table once one of those routes covers them. Run them together when the user is at the computer, record the results in `docs/decisions.md`, and remove the item.

| Check | Why a person is needed | Source |
|---|---|---|
| Narrator (and NVDA if installed) on the page-drawn title bar and a sample page | Speech plays on the speakers; listening is the check | `spikes/windows-sandbox-checks` |
| Title bar at 125 %, 150 % and 200 %, a DPI change while open, and two monitors at different scales | Needs the user's display scaling; Windows Sandbox follows the host monitor | `spikes/windows-titlebar`, `spikes/windows-sandbox-checks` |
| The custom frame's drop shadow and DWM hover visuals on native buttons | Visual judgement on the real desktop; the Sandbox shows no shadow | `spikes/windows-titlebar` |
| Real webcam and microphone on WebView2: the Windows privacy prompt for unpackaged apps, allow, deny, and turning the camera off from the app | A real device and the user's privacy prompt | plan 3b, `spikes/csp-navigation` |
| Real webcam and microphone on WebKitGTK and the camera portal | Same, on Linux | plan 3b |
| A light Windows theme and a real system theme switch (`WM_SETTINGCHANGE`) | Changes the user's theme | plan 3d |
| Wayland input under WSLg: dragging the page-drawn title bar, double-click, edge resize, GTK3 and GTK4 | Only real mouse input works on WSLg Wayland | `spikes/linux-integration` |
| Dropping files from Explorer or a Linux file manager onto the page | Real drag and drop | `spikes/linux-integration`, M3 |
| VS Code: contract diagnostics and CSP analyzer squiggles, completion popups, the lightbulb fixes, rename preview, the TypeScript Native Preview extension, read-only generated packages | Interactive editor UI | decisions "Opaque results in TypeScript", plan 3b |
| Notifications as they appear on screen (Windows toast or balloon, Linux) | Visual, and they reach the user's desktop | research review, M3 |
| On a real Linux desktop, if one is available: GNOME and KDE tray hosts, a real GPU, HiDPI, IME | Hardware and desktops not reachable from WSLg | `spikes/linux-wsl-desktop`, `spikes/linux-integration` |
| The start-up error dialogs (task dialog and message box) at 125 %, 150 % and 200 %, and across monitors with different scales | Needs the user's display scaling; the Sandbox stays at 96 DPI | `spikes/startup-prerequisites` |
| Narrator reading the start-up error dialog | Speech plays on the speakers | `spikes/startup-prerequisites` |
| The start-up dialog's link and download button opening Microsoft's WebView2 page | Needs a connected PC; the Sandbox guest had no network | `spikes/startup-prerequisites` |
| On a real GNOME or KDE desktop: the GTK and kdialog start-up messages, Orca, a critical notification staying until dismissed, the journal entry after a `.desktop` launch | Desktops not reachable from WSL or Docker | `spikes/startup-prerequisites` |
| Task Manager's app-group "Memory" total against the reported app total | Visual comparison on the user's desktop | `spikes/resource-usage` |
| GNOME and KDE System Monitor "Memory" against the Linux app total | Real desktops not reachable from WSL | `spikes/resource-usage` |
| The "Wait / Reload page" dialog over a hung page on the real desktop, and a real click into a hung page | Visual judgement, and the focus and input behaviour on the user's own desktop | `spikes/page-heartbeat` |
| Sleep and resume, lock and session switch with a page open | Puts the user's machine to sleep or locks it | `spikes/page-heartbeat` |

### M0 close-out

1. **Serializer and error syntax:** confirmed (generated code; `closed record`).
2. **Review all guidance once this plan is revised** (user rule). **Done on 2026-11-15** (decision "Guidance review applied"); repeat it whenever the plan changes materially.
   - Check every skill, `AGENTS.md`, `.claude/settings.json`, the README and `.editorconfig` against this plan and `docs/decisions.md`.
   - Update what is stale, remove what no longer applies, and add what the decisions require.
   - Repeat the review whenever the plan changes materially.
   - Add a Linux host skill next to `spar-webview2-hosting`.
3. **Rebuild the guidance on the earlier attempt's skeleton:** `AGENTS.md`, `CLAUDE.md`, skills, `eng/check-size.cs`, analyzer settings, `agent-setup.md`, merged with this session's content.
4. **Docs:**
   - spike results move to `docs/evidence/`;
   - the runbook is added as `docs/webview2-investigation.md`.
5. **D8 automation:** done (in-process CDP).
6. **Linux real-desktop spike** in the user's WSL Ubuntu with WSLg: done (`spikes/linux-wsl-desktop`). It explained the slow GTK3 start and recommended 6.0. Remaining: the user's choice of API, and real GPU, HiDPI and IME on a native desktop.
7. **Decide the remaining user questions** in the open-questions table that block M1: Node in Debug, the contract snapshot, the wire details.
8. **Delete `spikes/`** once its evidence is in `docs/evidence/`.
   - **How evidence is kept (user decision, 2026-11-14):**
     - `spikes/` is versioned in git without its generated folders, and raw evidence lives in each spike's `results/`.
     - Deleting a spike first moves its `RESULTS.md` and `results/` to `docs/evidence/<name>/` and updates the links, which `docs/decisions.md` and `docs/pitfalls.md` use throughout.
     - Before the M0 commits are squashed, the full history is kept in an archive tag or a bundle (proposal).
   - **Raw evidence inside generated-looking folders,** which a clean of `obj/` or `artifacts/` would lose: `d3-variants/obj/` (CSV, logs, a binlog), `ts-results/obj/editor/`, `ts-compiled-contracts/obj/` (`editor/`, `matrix.md`, `bundle.md`), `d8-automation/artifacts/results/`. `npm-ignore-scripts/*/obj/spar/client` holds inputs that spike cannot rerun without. The root `artifacts/` folders hold the raw data behind several RESULTS tables, for example `artifacts/react/` and `artifacts/browser-telemetry/`.
   - **Dependencies between spikes,** which constrain the deletion order (delete together, or the providers last):
     - `d1-pipeline` is used by `react` and `loopback` (`Spike.targets`, `Spike.Runtime`) and by `linux-desktop` (`Spike.Generator`, `Spike.Runtime`). The bridge protocol prototype lives there (section 2c). `browser-telemetry` holds its own newer copies of `d1-pipeline`, `react`, `loopback` and `linux-desktop`.
     - `react` is used by `aspire` (its `React.App` and `frontend/`).
     - `loopback` is used by `test-frameworks` (`AppPathMetadata.targets`, `LoopbackApp.cs`, the built `Loop.App` Debug exe), and `linux-loopback/linux-changes.diff` patches it.
     - `linux-loopback/Dockerfile` builds the image `spar-linux-loopback:sdk11.0.100`, on which `linux-desktop/Dockerfile` builds `spar-linux-desktop:webkit2.52`. The Linux container runs of `asset-packing`, `csp-navigation`, `theme-startup`, `sqlclient-linux` and `browser-telemetry` use that image. These two Dockerfiles are the only recipe for the Linux CI image (section 5, M1).
     - `linux-wsl-desktop` copies `theme-startup/frontend/dist`; `sqlclient-linux` runs the built exes of `sqlclient-tool`.
     - `contract-syntax` borrowed `tsc` from `react/React.App/frontend/node_modules`, and `wire-details` from `npm-ignore-scripts/react/frontend/node_modules`.
9. **Before the first commit: references and provenance review** (user request, 2026-11-14). **Scan done on 2026-11-15:** no copied code (decision "Provenance scan before the first commit").
   - **What may stay (user, 2026-11-14):**
     - references to official documentation;
     - references to earlier Spar versions, unless what they describe came from another framework.
   - **References to other frameworks** (Tauri, Electron, Wails and the others the research cites):
     - **They stay in the design records as evidence** (user decision, 2026-11-14), and never appear in user-facing documents (`AGENTS.md`).
     - At the first commit, check that skills, code comments and file names name no framework.
   - **Licences found:** Tauri MIT and Apache-2.0; Wails MIT (registry pages, to verify against the official repositories); Electron MIT.
   - **Found on 2026-11-14,** excluding `node_modules`, `obj`, `bin` and `artifacts`:
     - **Tauri:** a `T` column in `docs/research-review.md` (about 15) and a few mentions in the plan and decisions.
     - **Electron:** only as a measured reference in `spikes/linux-memory` (a small Electron app written for the comparison), its numbers in the plan, decisions and pitfalls, and VS Code's `ELECTRON_RUN_AS_NODE` in two TypeScript spikes, which is tool usage.
     - **webview/webview and saucer:** once each, in the WebKitGTK decision entry.
     - **Wails:** in the WebKitGTK decision entry.
     - No other framework (Photino, Neutralino, Flutter, MAUI, Avalonia, Blazor, Dioxus) is mentioned.
   - **No copied code from other frameworks:** check that no spike or guidance file copies code from Tauri or the other frameworks.
     - Method: a similarity scan (for example a copy-paste detector run against their sources) plus a manual look at the hits.
     - Code shared between spikes inside this repository, and with earlier Spar versions, is not the target of this check.
   - The results are recorded in `docs/decisions.md` before the squash and first push.

**Exit:** every open question that M0 owns is answered and recorded. The repository builds, and `dotnet test` and `eng/check-size.cs` pass.

### M1: build machinery, then a thin Notes slice on both hosts

1. **SDK and tasks:**
   - `Spar.Sdk` with `SparVariant` and the task assembly (inventory, client, pack, lease, in-proc variant builds);
   - the generator, the analyzer and their tests (diagnostics with locations, the clash matrix with a referenced assembly, generated-output snapshots, the differential `tsc` test);
   - `src/Spar.Client` (section 2b);
   - Brotli frontend packing;
   - a package-only consumer test.
2. **Core:**
   - dispatch, the three outcomes, DI scope per command, cancellation (parameter plus `ISparCall`);
   - the bridge protocol with `traceparent`;
   - the BCL instrumentation;
   - the managed-level test factory.
3. **Desktop hosts, Windows and Linux:**
   - **Windows:** STA ownership, instrumentation, asset serving, the bridge with document scoping, teardown, failure recovery, and the required host tests.
   - **Security defaults on both hosts (section 3c):** the Production browser settings, ignored environment overrides, the per-user profile folder, the deny-by-default permission gate, each verified on the published executable together with its C# opt-in.
   - **Linux:** the same over WebKitGTK (main loop thread, custom scheme, script-message channel, origin policy, main loop kept running until the web processes exit, startup library checks).
   - **Both:** the navigation lock and the CSP from C# (section 3b, once verified), with the Vite plugin sending the same policy in Debug. The Notes sample and the desktop tests run on both; Linux runs headlessly in a container in CI.
   - **WebAssembly and worker CSP follow-ups** (`spikes/webkit-wasm-off`, recorded for M1):
     - the Linux host sets `JSC_useWasm` through libc `setenv` before GTK starts, `false` by default and `true` with the opt-in;
     - a real-host test asserts `typeof WebAssembly` both ways, in the page and in a worker;
     - the CSP header on every host-served response, worker scripts included, checked on WebView2, WebKitGTK and the Vite plugin;
     - try `--js-flags=--noexpose_wasm` on WebView2, so both hosts behave the same;
     - shared and service workers and web-process restarts;
     - the minimum WebKitGTK becomes 2.48 for the option name.
4. **Notes sample:** save and load, a typed error, the generated client, `ui-ready`.
5. **Test levels:** managed, loopback with browser, desktop.
6. **Development loop:** Debug served by the dev server, the `Desktop` and `Loopback` profiles, `dotnet watch`, and the Hot Reload check.
7. **User project and tooling:**
   - the template `dotnet new spar-app` (React + TypeScript + Vite first, `--platforms windows,linux`), with `TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors` on: `frontend/` inside the app project (`SparFrontendPath`), `Spar.Sdk` in `global.json`, the launch profiles, the local tool manifest;
   - the fixed `@spar/app` `file:` dependency and the Spar Vite plugin;
   - the two test projects;
   - the basic `spar doctor` and `spar init frontend`;
   - a from-scratch guide.
8. **Runtime choices:** documented and tested. They are per-app settings in the app's own project file; Spar never turns them on for an app (user, 2026-11-14). The SqlClient items are the SQL Server tool's settings, shown as a documented recipe, not Spar defaults.
   - CoreCLR publication (framework-dependent, and self-contained single file);
   - an app accepting its own AOT warnings. With the SDK default `TrimmerSingleWarn=true`, SqlClient 7.1.2 reports only 4 assembly-level warnings (IL2104 ×3, IL3053), so a suppression of those hides whole assemblies (`spikes/sqlclient-aot`);
   - `InvariantGlobalization=false`;
   - **SqlClient as a single file on Windows** (`spikes/sqlclient-aot`): `<RuntimeHostConfigurationOption Include="Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows" Value="true" Trim="true" />` plus `<PackageReference Include="Microsoft.Data.SqlClient.SNI.runtime" ExcludeAssets="all" PrivateAssets="all" />`. The publish folder is then the exe alone, and every feature check passed. Without the switch the SNI DLL is a mandatory sidecar: the exe alone fails at the first `Open` with `DllNotFoundException` for `Microsoft.Data.SqlClient.SNI.dll`, and the package has no static library. Not tested with managed networking: integrated authentication, named pipes, shared memory.

**Exit:**
- `dotnet run`, `watch`, `test` and `publish` work in the repo and in the package-only consumer, on Windows and Linux.
- A single-file AOT executable is produced per OS, with its size recorded.
- The host tests pass on both hosts.
- Visual Studio and VS Code show contract diagnostics as you type, with squiggles in both.
- Evidence is recorded for each result.

### M2: agent facilities

- Devtools endpoint, `spar dev` with reload, `spar snap`, scenes and `TimeProvider`, the agent guide, and the evaluation. Exactly one interactive surface survives.
- The optional OpenTelemetry layer, client telemetry through the backend, and the Aspire dev experience.
- If M1 shows agents need it sooner, a minimal `spar dev` (build plus reload) can move into M1.

### M3: communication breadth and the SQL Server tool

- Streams, subscriptions, and native services (file dialogs with grants, credentials) on both hosts.
- **The second consumer is the SQL Server tool** (its own repository, Spar through packages only). It shows that no framework code is app-specific, and exercises long cancellable copies, large result sets, live monitoring and credentials.
- **Its own settings stay in its own project** (user, 2026-11-14): `InvariantGlobalization=false`, the single-file SqlClient switch and SNI exclusion, and the AOT warnings it accepts are the SQL Server tool's choices. They are not Spar defaults or assumptions. Spar documents them as a recipe for apps that use SqlClient.
- Its data-layer findings are already known (see `docs/pitfalls.md`):
  - transactions for cancellable bulk copies. `BatchSize = 0` is not atomic: without a transaction a cancel still kept about 310,000 rows, and it was slow to cancel (0.8–1.0 s, 0.3–0.4 s with `TableLock`) against 34–51 ms with 50,000-row batches. A large copy that must cancel quickly uses bounded batches (`spikes/sqlclient-tool`);
  - `SqlCommand.Cancel()` on the source;
  - `nvarchar` parameters;
  - Kerberos-only integrated authentication on Linux;
  - the ICU and OpenSSL checks.

**Subscription resumption** (user decision):

| Level | Who | Setting | Default |
|---|---|---|---|
| Contract | Backend author, in C# | `[SparSubscription(Resume = SubscriptionResume.Automatic \| Manual)]` | `Automatic`: a subscription must not have side effects. Anything that does, such as draining a queue or creating server-side sessions, is marked `Manual`. |
| Call | Page, in TypeScript | `{ resume: "automatic" \| "manual" }` | The contract's value. The page may only be more cautious: `Manual` subscriptions are typed as `resume?: "manual"`. |
| App | SDK configuration | `configureSpar({ resume: "manual" })` | Lowers the default; it never raises it above a contract's value. |

- **Automatic resume:** re-subscribe with backoff (for example 250 ms doubling to 5 s) when the bridge is back. Emit `reconnecting`, then `reset` before the first new event, then `resumed`. Give up after a limit (for example 30 s) with an outcome-unknown `CallError` (`lost`).
- **One-shot streams are never resumed, and unary commands are never retried.**
- **A resume cursor** is a later, author-opt-in extension.

### M4: release qualification

- **Production run:** the Sandbox test level (Windows), and a clean-machine Linux run.
- **Performance budgets:** measured with the runbook's protocol (milestones, release builds, one change per comparison, medians and ranges). Regression suites (BenchmarkDotNet for runtime paths, a build-timing harness for builds) compare each change with stored baselines; see the test-framework topic.
- **Runtime support:** the minimum supported WebView2 Runtime and WebKitGTK versions, with fallback when newer interfaces are missing.
- **Distribution decision:** Windows (single exe, installer?) and Linux (deb, rpm, Flatpak or AppImage, with WebKitGTK as a dependency).
- **Signing and privacy (section 3c):** a signed executable and Linux packages; privacy defaults for telemetry and log redaction.

## Verification

Each milestone's exit lists observed results only. Each result has an evidence file in `docs/evidence/` giving the environment (SDK, Runtime, OS, hardware), the commands, the numbers, and what wasn't verified. Earlier results don't verify later changes.

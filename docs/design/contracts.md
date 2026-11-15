# Contracts, the generated client and the TypeScript SDK

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 2" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 2. Contracts and the generated client

- **Contracts:** an attributed class (`[SparContract]`; only `[SparCommand]` methods are commands; one instance per call from the DI scope), or an attributed interface (every method is a command; implementation from DI). Mixing the two is a diagnostic.
- **`Program.cs`** (decided, user, 2026-11-15; decision "`Program.cs` shape confirmed"): exactly one `SparApplication.Create` and one `Run`, directly in the top-level statements, so the build can read the constant declarations without running code. Anything else is an analyzer error at its location.
  - **Read by the build, so constants only:** the CSP additions and other header opt-ins (section 3b), the permissions (section 3b), the window kinds, whose names become the `@spar/app/windows` union, and the window colours (section 3d).
  - **Run at start-up, so ordinary values are fine:** everything else in the configuration, such as services, folders, the tray and its menu handlers.
- **Commands:**
  - Zero or one request (a positional record or a scalar), then an optional `CancellationToken`. The scalars accepted in `spikes/contract-syntax` were `string`, `int`, `long`, `double`, `bool` and `Guid`, nullable allowed.
  - Returns: `Task`, `Task<T>`, `Task<Result<T, TError>>`, `Task<Result<TError>>`, their `ValueTask` forms, `IAsyncEnumerable<T>`, `IAsyncEnumerable<Result<T, TError>>` (a stream that declares errors: yielding an error item ends it, Spar disposes the iterator, and a diagnostic flags code that yields after the error; decided 2026-11-14), or a `[SparSubscription(Capacity, Resume)]` subscription.
  - The scoped `ISparCall` (token, call id, names) is injectable.
- **Names:**
  - Derived: a class keeps its name, an interface drops one `I`, both drop `Service`, then acronym-aware camelCase (`IOStatsService` → `ioStats`). Commands drop `Async`.
  - Overrides are `[SparContract("…")]`, `[SparCommand("…")]` and `[SparName("…")]`.
  - Duplicates are errors, including contracts from referenced assemblies.
  - Explicit names are camelCase ASCII identifiers. Contract names are compared ignoring case, because they become folders.
  - **Reserved contract names:** JavaScript reserved words, and every subpath that `@spar/app` uses itself. The spike reserved `sdk`, `shared` and `spar`; the decided layout also has `vite` (below), `telemetry` (section 6b) and `windows` (decision "Secondary and modal windows"), which a contract must not take either, and `testing` (the page-test helpers of section 5). The list follows the SDK's subpaths.
- **Errors:** C# 15 `closed record` hierarchies with nested sealed cases. Union conversions into `Result` (`return new PlanError.X(…)`). On the wire a declared error is a `result` (or `end`) message with `"ok":false` in its header and `{"code":"x",…}` as its payload (section 2c, "The messages"; this replaced the `{"ok":false,"error":…}` envelope on 2026-11-15), with reserved payload names (`kind`, `code`, `message`, `execution`). Roslyn's experimental closed-type API is allowed in the generator (RSEXPERIMENTAL006, agreed).
  - A case without a payload (`sealed record Forbidden : PlanError;`) is `{"code":"forbidden"}`. `[SparName("…")]` on a case renames its code. Its TypeScript interface-map entry is `undefined`: the handler takes no parameter (`forbidden: () => …`), and in the error union the case is just `{ code: "forbidden" }` (decision "M1a names"; checked in `spikes/app-model-check`, mistake 21).
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
    - **No value:** `Result<TError>`, returned with `return Result.Success;`. On the wire it is a `result` with `"ok":true` and an empty payload; in TypeScript, `Call<void, E>`.
    - **`default`** is a distinct invalid state, on which every member throws:
      - the struct form is guarded by the Spar analyzers above (SPAR050–SPAR054, new rules, not severity overrides), which catch all 6 original cases. The first prototype, about 40 lines, caught 4 of 6;
      - the class form got 5 of 6 from nullable analysis.

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
  - **Writer failures:** every unary response is buffered, and each stream item has its own buffer. A failure resets the pooled writer and sends a `failure` message with `"kind":"response","code":"serialization","execution":"completed"` in its header (section 2c). A stream sends `"execution":"stopped","itemsSent":N` (decided). Unbuffered writing had already sent 278,560 bytes of invalid JSON when it failed.
  - **Why:**
    - Sent as numbers, only 603 of 100,000 random 64-bit ids survived `JSON.parse`. SQL Server values do cross 2^53: CPU ticks after about 35 days of uptime, query hashes.
    - `JSON.parse` changed 5 of 7 `decimal` samples.
    - SqlClient already replaces lone surrogates in `nvarchar`.
  - **SQL `decimal(38)`** overflows `System.Decimal` (only `GetSqlDecimal` reads it). Decided: Spar adds no SQL-specific wire type; an app that needs such values sends them as `string`.
- **The fingerprint hashes full wire shapes.** A mismatch returns `contract-mismatch` (409 on loopback) before any work.
- **Bundle budget (decided, user, 2026-11-14):** optional and off by default. `<SparBundleBudget>300KB</SparBundleBudget>` (name confirmed, decision "M1a names") makes the build warn or fail when the compressed frontend grows past it. Spar has no built-in licence check for npm dependencies; the docs point to existing tools. Revised (user, 2026-11-15; decision "Source maps stay out of the executable; third-party notices are reported and copyleft is flagged"): `spar doctor` flags bundled packages with no licence or a copyleft one (GPL, AGPL, LGPL); a full licence policy stays with dedicated tools.
- **Generated client:**
  - **Built in M1a** (decision "Client step: the generated TypeScript modules", choices for review): the `SparClient` task in `src/Spar.Sdk.Tasks` writes the package with `@spar/app/spar` (the page's fingerprint), a module per contract, `shared` for types several contracts use, and the SDK under `sdk/` (its `wire` module reads and writes values field by field). A request the generated encoder refuses ends `rejected` / `invalid-request` / `notStarted` without leaving the page. Each `.d.ts` has a `.d.ts.map` to the C# declarations (go-to-definition checked through `tsserver`), and the contract snapshot is written and verified (decisions "Declaration maps and the contract snapshot" and "Contract snapshot choices accepted"). The manifest is still to build.
  - One fixed `file:` dependency in the app's `package.json`: `"@spar/app": "file:../obj/spar/client"`, with subpaths `@spar/app/<contract>` and `/windows` (generated), `/sdk` and `/vite` (shipped compiled), the optional `/telemetry` (section 6b), and `/testing` (the page-test helpers of section 5). It is never generated into `node_modules`.
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

## 2b. TypeScript SDK (`src/Spar.Client`)

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

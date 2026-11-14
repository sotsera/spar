# Opaque results in the generated TypeScript client

Spike for the decision "Results are opaque" (`docs/decisions.md`, 2026-11-13 Contract syntax): which `match`/`unwrap` form to generate, and what `tsc` and editors do with it.

## Versions

| What | Version | How it was run |
|---|---|---|
| TypeScript 7 (native) | 7.0.2 (`serverInfo: typescript-go 7.0.2`) | `tsc -p …`, and `tsc.exe --lsp --stdio` from `@typescript/typescript-win32-x64` (this spike's `node_modules`, installed `--offline` from the npm cache) |
| VS Code | 1.146.0 | not opened |
| VS Code's bundled TypeScript | 6.0.3 | `resources/app/extensions/node_modules/typescript/lib/tsserver.js`, run by `Code.exe` with `ELECTRON_RUN_AS_NODE=1` (as VS Code runs it), tsserver protocol over stdio, with the completion preferences VS Code sends by default; `typescript.js` compiler API for the `tsc`-equivalent check |
| Node | 24.21.0 | tools are `.ts` run directly (type stripping) |
| rolldown | 1.2.19 | size bundles (minified) and the Node bundles of `bench/` |
| @types/react | 19.3.0 | `react.tsx` type checks |

**VS Code does not use TypeScript 7 by default.** VS Code 1.146 uses its bundled 6.0.3 unless the *TypeScript Native Preview* extension (`typescriptteam.native-preview`) is installed and `js/ts.experimental.useTsgo` is on; it is not installed here. TypeScript 7's npm package has no `tsserver.js`, so `typescript.tsdk` cannot point at it either. A Spar frontend that builds with `tsc` 7 therefore shows 6.0.3 diagnostics in the editor unless the user installs that extension. Both are covered below.

## Layout

- `forms/core.ts`: shared types (`CallError`, handler types) and the runtime that picks a handler (`settle`).
- `forms/<form>/`: `sdk.ts` (the form), `copyJobs.ts` (what the generator would emit), `app.ts` (usage with compile-time type assertions), `react.tsx` (async event handler, two `useEffect`s with cleanup), `bad.ts` (11 mistakes), `probe.ts` (editor probes, cursor markers in comments). `c-call/docs.ts` holds the documentation snippets below, type-checked.
- `forms/b-proto/`: the earlier non-opaque prototype, made opaque with a brand, unchanged otherwise (union-of-tagged-types errors, one generic `H` over the whole handler object). Baseline only; no React or streams.
- `tools/probe.ts` drives both language servers (`tools/clients.ts`: an LSP client and a tsserver client; `tools/locate.ts` finds VS Code at run time from `VSCODE_HOME`, `code` on PATH, or the per-user install). Raw output: `obj/editor/{tsgo,vscode}.md` (and `unlinked-*.md` for the earlier signature, see below).
- `tools/check-vscode-ts.ts`: type-checks every form with VS Code's TypeScript 6.0.3. `bench/`: size entries, `perf.ts`, `behaviour.ts`.
- `npm run check | check:vscode | probe | size | perf | behaviour`.

Compiler settings for everything (`tsconfig.base.json`): `strict`, `noUnusedLocals`, `noUnusedParameters`, `verbatimModuleSyntax`, `isolatedModules`, `exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`, `moduleResolution: bundler`, `jsx: react-jsx`. All forms compile clean under them, React included.

## Forms compared

All three use the same handler type, so type checking and editor behaviour inside the handler object are the same; they differ in where `match` lives.

```ts
// A: method on the awaited result
const plan = (await copyJobs.plan(recipe, { signal })).match({ success: …, errors: { … } });

// B: free function over an opaque branded value
const plan = match(await copyJobs.plan(recipe, { signal }), { success: …, errors: { … } });

// C: method on the call itself (a PromiseLike) and on the awaited result
const plan = await copyJobs.plan(recipe, { signal }).match({ success: …, errors: { … } });
for await (const item of copyJobs.preview(recipe, { signal })) item.match({ … });
```

- **A**: `class Result<T, E>` with a `#outcome` private field; `match`, `unwrap` methods. Generated commands return `Promise<Result<T, E>>`.
- **B**: `interface Result<T, E> { readonly [brand]: … }` with a non-exported `unique symbol`; the runtime value is the plain wire outcome. `match(result, handlers)`, `unwrap(result)`.
- **C**: A's `Result`, plus `class Call<T, E> implements PromiseLike<Result<T, E>>` whose `match`/`unwrap` return promises. Generated unary commands return `Call<T, E>`; streams and subscriptions are `AsyncIterable<Result<…>>` as in A.

### The handler type (decided by the spike, same for every form)

The prototype's single generic over the whole handler object (`b-proto`) works for `tsc`, but a first "one type parameter per slot" version (`errors: { [K in keyof E]: (e: E[K]) => X }`) failed the requirement: with mixed return types `X` had no common supertype (`TS2322: Type 'boolean' is not assignable to type 'string'`), and a typo produced `Type 'number' is not assignable to type 'never'` on unrelated handlers. `experiments/signatures.ts` compares five signatures. The one adopted:

```ts
export type ErrorHandlers<E> = { readonly [K in keyof E]: (error: E[K]) => unknown };
type NoExtraCases<H, E> = NoInfer<[Exclude<keyof H, keyof E>] extends [never] ? unknown : { readonly [K in Exclude<keyof H, keyof E>]: never }>;
export type Handlers<T, E, S, H, F> =
  & { readonly success: (value: T) => S; readonly failure?: (error: CallError) => F }
  & ([keyof E] extends [never] ? unknown : { readonly errors: ErrorHandlers<E> & H & NoExtraCases<H, E> });
// method (A, C) or function (B):
match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H, F>): S | ErrorResults<H> | F
```

- `S` and `F` are separate type parameters; `F` defaults to `undefined`, so `undefined` joins the result only when `failure` is omitted.
- The error handlers are one inferred object `H`; `ErrorResults<H>` is the union of their return types, written as a distributive conditional (`Returns<H[keyof H]>`) so hovers show `string | number | boolean | null | undefined` instead of an unevaluated alias.
- `NoInfer` on the exactness part is required: without it, handlers that use their parameter (`e => e.table`) lost contextual typing (`TS7006: Parameter 'e' implicitly has an 'any' type`, V4 in the experiment).
- `ErrorHandlers<E> &` in front of `H` ("linked") ties each handler key to the generated declaration. Without it ("unlinked", first run, `obj/editor/unlinked-*.md`), go-to-definition still worked, but hover showed no doc comment and rename/find from the generated declaration found only the call sites where inference had failed (3 of 10 locations). Linked costs a noisier hover on the key (an intersection of two function types).
- A command without declared errors has no `errors` property at all (`unknown` instead of `errors?: never`): completions do not offer `errors`, and `errors: {}` gets TS2353 instead of `Type '{}' is not assignable to type 'never'`.
- Generated errors are an interface map (case name to payload, C# XML docs as JSDoc). The discriminated union the decision mentions is derived: `ApplicationError<PlanError>` = `{ code: "tableMissing" } & {…} | …` (used by `SparError.error` for `unwrap`). The prototype's union-of-tagged-types shape (`b-proto`) loses go-to-definition, docs, rename and the "Add missing properties" fix (below).

The 11 mistakes in `bad.ts`, with `tsc` 7.0.2 (form C; A and B are the same text unless noted):

| # | Mistake | Diagnostic |
|---|---|---|
| 1 | a declared case not handled | `TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.` |
| 2 | typo in a case name | `TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?` |
| 3 | nonexistent payload field | `TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?` |
| 4 | `errors` forgotten | `TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.` / `Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.` |
| 5 | reading the value | `TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.` |
| 6 | `errors` on a command without errors | `TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.` |
| 7 | `success` forgotten | `TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.` |
| 8 | no `await` | A: `TS2339: Property 'match' does not exist on type 'Promise<Result<number, {}>>'.` (+ `TS7006` on the handler parameter). B: `TS2741: Property '[result]' is missing in type 'Promise<Result<number, {}>>' but required in type 'Result<unknown, object>'.` C: calling `.match` without `await` is valid (it returns a promise); using its value as a number gives `TS2322: Type 'Promise<number \| undefined>' is not assignable to type 'number'.` |
| 9 | every case plus an undeclared one | `TS2322: Type '() => number' is not assignable to type 'never'.` (on the extra key) |
| 10 | control: a type without its import | `TS2304: Cannot find name 'CopyReport'.` |
| 11 | `errors: {}` | `TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch` |

VS Code's TypeScript 6.0.3 reports the same codes, positions and text for A and C. Differences: B #8 is `TS2345: Argument of type 'Promise<Result<number, {}>>' is not assignable to parameter of type 'Result<unknown, object>'.` with the TS2741 text as elaboration; in `b-proto` the properties inside the long intersection type are printed in a different order. `app.ts`, `react.tsx` and `docs.ts` have 0 diagnostics in both.

`b-proto` messages are longer for #1, #2 and #4 (`… is not assignable to type 'ErrorHandlers<PlanError> & { readonly columnsMismatch: (error: ApplicationError<"columnsMismatch", …>) => unknown; … un...'`) and payload types read `ApplicationError<"connectionNotFound", { readonly connectionId: string; }>`.

## Editor behaviour (headless)

Both servers, all forms, `probe.ts` cursor positions. Identical across A, B and C except where noted.

### Completions

| Position | TypeScript 7.0.2 LSP | VS Code TypeScript 6.0.3 |
|---|---|---|
| `match({ | })` | `success`, `failure?`, `errors` | `errors`, `success`, `success(value)` snippet, `failure`, `failure(error)` snippet |
| `match({ success: p => p, | })` | `failure?`, `errors` | `errors`, `failure`, `failure(error)` snippet |
| `errors: { | }` | `connectionNotFound`, `tableMissing`, `columnsMismatch` | the same three, each also as a method snippet, e.g. `tableMissing(error) {⏎ $0⏎},` |
| `errors: { connectionNotFound: …, | }` | `tableMissing`, `columnsMismatch` | the same two, plus their method snippets |
| `count.match({ | })` (no errors) | `success`, `failure?` | `success`, `success(value)`, `failure`, `failure(error)` |
| C: `copyJobs.plan(r).match({ success, errors: { | } })` | the three cases | the three cases plus snippets |
| `result.|` | A, C: `match`, `unwrap`. B: nothing (the brand is not listed) | same |

The case lists are exactly the declared cases minus those already written. Completion details for `tableMissing` show `(property) tableMissing: (error: { readonly connection: string; readonly table: string; }) => unknown`; **6.0.3 adds the case's doc comment** ("The table does not exist on that connection."), **7.0.2 does not** (`completionItem/resolve` returns `detail` only). 7.0.2 offered no object-literal method snippets; it asks `workspace/configuration` for `js/ts`, `typescript`, `javascript` and `editor`, and answering with guessed preference names did not enable them. `b-proto` gets no method snippets for cases in 6.0.3 either.

### Hover

| Target | Both servers |
|---|---|
| `success: p` | `(parameter) p: CopyPlan` |
| `tableMissing: e` | `(parameter) e: { readonly connection: string; readonly table: string; }` (`b-proto`: `ApplicationError<"tableMissing", {…}>`) |
| `failure: e` | `(parameter) e: CallError` |
| the `tableMissing` key | `(property) tableMissing: ((error: {…}) => unknown) & ((e: {…}) => string)`; 6.0.3 adds the doc comment, 7.0.2 does not |
| the result variable | `const shown: string \| number \| boolean \| null \| undefined` (`b-proto`: `MatchResult<{ readonly success: () => string; readonly errors: {…} }>`) |
| `match` | the instantiated signature and the SDK doc comment |

### Quick fixes

- **TypeScript 7.0.2 offers no quick fix for any of the result mistakes.** For the control (#10) it offers `Update import from "./copyJobs.js"`, so the client is right; the other fixes are not offered by this server version.
- **6.0.3** (titles as tsserver gives them):
  - #1 and #11 `Add missing properties`: it adds only the missing cases, each as `columnsMismatch: function(error: { readonly table: string; readonly differences: readonly ColumnDiff[]; }): unknown { throw new Error("Function not implemented."); }`, reformats the object over several lines, and adds `type ColumnDiff` to the import. For #11 it fills all three cases. A template to edit, not finished code; its `unknown` return type widens the result to `unknown` until edited.
  - #4 `Add missing properties` writes `errors: undefined` (still an error).
  - #7 adds `success: function(value: number): unknown { … }`.
  - #3 `Change spelling to 'connectionId'` (correct), plus `Declare property 'connection'` and `Add index signature`, which edit the generated `copyJobs.ts`.
  - #5 `Declare method/property 'value'`, `Add index signature`, which edit `sdk.ts`.
  - #8 A and B `Add 'await'` (A wraps the comment above into the parentheses: `(await⏎ // 8. …⏎ copyJobs.count()).match(…)`); C has no error to fix.
  - #2 and #9: no fix (the TS2561 message already names the right case).
  - `b-proto` #1: no fix at all (the union shape).
- Several offered fixes edit generated files. Making the generated packages read-only in the editor is worth a human check.

### Go to definition and rename

- From a case key in user code, both servers go to the generated declaration (`copyJobs.ts:17:12`, `readonly tableMissing: …`). From a payload field (`e.table`), both go to the field in the generated interface. `b-proto`: the key goes nowhere (to itself).
- Rename at a case key in user code, and rename or find at the generated declaration: both servers return the same 10–11 locations (the declaration plus every handler, in `app.ts`, `react.tsx`, `docs.ts`, `probe.ts`, `bad.ts`). Unlinked signature: 1 location (the key only), and from the declaration 3. `b-proto`: renaming from the declaration is refused (`You cannot rename this element.`).
- **Rename is not safe in either direction, and the compiler only partly protects it.** A TypeScript-side rename edits the generated file, compiles, and then sends the declared error to `failure`/the interceptor as `unknownErrorCase`, because the backend still sends the old name. The next generation restores the name, and `tsc` then flags every renamed handler (TS2561 + TS2741). Payload fields behave the same, like any generated DTO property. The safe rename is in C#, followed by regeneration; the compiler then lists every handler to update.

## Runtime and size

`bench/behaviour.ts` runs every form against a stand-in transport:

- success, a declared error, an infrastructure failure with and without `failure` (the interceptor receives `lost/unknown`; `match` returns `undefined`);
- an undeclared code from a newer backend (it becomes `protocol unknownErrorCase`, `execution: completed`, and goes to `failure` or the interceptor);
- `unwrap` rejecting with `SparError` carrying `{ code: "tableMissing", table: "t", … }`;
- a stream of two rows and a terminal failure.

All passed. At run time A and C have no own enumerable keys (`#private`); B is the plain wire object.

Minified bundle of a small page (two unary calls, one stream, interceptor; generated client included), rolldown 1.2.19, gzip level 9:

| Entry | Without `unwrap` | With `unwrap` |
|---|---|---|
| A | 1590 B, gzip 749 B | 1621 B, gzip 755 B |
| B | 1274 B, gzip 617 B | 1515 B, gzip 717 B |
| C | 1757 B, gzip 788 B | 1786 B, gzip 793 B |

B drops `unwrap` and `SparError` when unused (tree shaking); methods cannot be dropped. C costs about 170 B gzip more than B in total.

`bench/perf.ts`, Node 24, median of 7 runs (5 for async), three runs agreed within 5%:

| Path | Cost |
|---|---|
| baseline: plain `switch` on the outcome | 5.2–5.6 ns/result |
| A or C: `new Result(outcome).match(h)` | 8.7–9.2 ns/result |
| B: `match(outcome, h)` | 7.6–7.9 ns/result |
| A: `(await invoke()).match(h)` | 89–91 ns/call |
| B: `match(await invoke(), h)` | 75–78 ns/call |
| C: `await invoke().match(h)` | 95–100 ns/call |

Against the measured bridge (0.2 ms median unary on desktop, about 1.6 µs per streamed row at 620k rows/s), the class wrapper adds about 0.01% per call and under 0.1% per streamed row. Size and speed do not decide between the forms.

## Recommendation

**Form C**: generated unary commands return a `Call<T, E>` (a `PromiseLike<Result<T, E>>` with `match` and `unwrap`), and the awaited `Result<T, E>` and every stream or subscription item have the same `match` and `unwrap`. Use the linked handler signature above with error maps.

- It reads as one expression with no extra parentheses: `await copyJobs.plan(r).match({…})`. A needs `(await …).match`; B puts the outcome and the handlers in separate arguments and needs an import.
- It is discoverable: `.` lists `match` and `unwrap`. With B a user must know to import `match`.
- It is opaque at run time as well (`#private`), not only in types. Test doubles need factories from the SDK (`Result.success(…)`, `.error("tableMissing", {…})`, `.failure(…)`); that is the same for every form.
- One handler object shape everywhere: unary, stream items, subscription items.
- The costs are small: about 170 B gzip, about 20 ns per call, and #8 is no longer a compile error. Also, `Call` is a thenable, not a `Promise`: `instanceof Promise` is false; `await`, `Promise.all` and `.then` work. If lint rules or libraries need a real `Promise`, a `Promise` subclass with `Symbol.species` set to `Promise` is the alternative. Not tested.

## Documentation snippets (form C)

These are type-checked in `forms/c-call/docs.ts` and `react.tsx`.

**A command with declared errors**

```ts
const message = await copyJobs.run(request, { signal }).match({
  success: r => `Copied ${r.rows} rows in ${r.tables} tables`,
  errors: {
    planOutdated: e => `Plan outdated: ${e.reason}`,
    targetNotEmpty: e => `${e.table} already has ${e.rows} rows`,
    constraintViolation: e => `${e.table}: SQL error ${e.sqlNumber}`,
    partiallyCopied: e => `Stopped after ${e.rowsCommitted} rows: ${e.reason}`,
  },
  failure: e => e.execution === "notStarted" ? "Nothing was copied" : "Outcome unknown: check the target",
});
// message: string
```

**A command without declared errors**

```ts
const total = await copyJobs.count().match({ success: n => n });
// total: number | undefined (undefined: a failure went to onFailure)

const rows = await copyJobs.count().unwrap();
// rows: number; rejects with SparError
```

**A stream**

```ts
for await (const item of copyJobs.preview(recipe, { signal })) {
  item.match({
    success: row => { rows.push(row); },
    errors: { tableMissing: e => notify(`${e.table} is missing`) },
  });
}
```

**A subscription**

```ts
for await (const item of copyJobs.progress({ jobId }, { signal })) {
  item.match({
    success: event => {
      if (event.type === "event") setProgress(event.value.table, event.value.rows);
      else if (event.type === "reset") reload();
      else if (event.type === "gap") notify(`${event.dropped} updates skipped`);
    },
    failure: e => notify(`Progress stopped: ${e.message}`),
  });
}
```

**App-wide failures**

```ts
configureSpar({ onFailure: e => notify(e.execution === "unknown" ? `${e.call}: outcome unknown` : e.message) });
```

**In React**

```tsx
useEffect(() => {
  const controller = new AbortController();
  void copyJobs.count({ signal: controller.signal }).match({
    success: setTotal,
    failure: e => { if (e.kind !== "cancelled") setProblem(e.message); },
  });

  return () => controller.abort();
}, []);
```

**What the generator emits for errors**

```ts
/** Errors of `copyJobs.plan` (C# `PlanError`). */
export interface PlanError {
  /** The named connection is not configured. */
  readonly connectionNotFound: { readonly connectionId: string };
  /** The table does not exist on that connection. */
  readonly tableMissing: { readonly connection: string; readonly table: string };
  readonly columnsMismatch: { readonly table: string; readonly differences: readonly ColumnDiff[] };
}

export const copyJobs = {
  plan: (request: CopyRecipe, options?: CallOptions): Call<CopyPlan, PlanError> => invoke("copyJobs.plan", request, options),
  count: (options?: CallOptions): Call<number> => invoke("copyJobs.count", undefined, options),
  preview: (request: CopyRecipe, options?: CallOptions): AsyncIterable<Result<PreviewRow, PreviewError>> => stream("copyJobs.preview", request, options),
  progress: (request: { readonly jobId: string }, options?: CallOptions): AsyncIterable<Result<SubscriptionEvent<CopyProgress>>> => stream("copyJobs.progress", request, options),
} as const;
```

## Open points

- **Stream items as results.** This spike makes every stream item a `Result`, so a terminal failure (and declared errors, if streams may declare them) arrives in-band and cannot be skipped by a `for await` loop. Whether C# streams can declare errors (`IAsyncEnumerable<Result<T, E>>`?) is not decided.
- **Subscription events.** Subscriptions use `success` for the `SubscriptionEvent<T>` union (`event | gap | reconnecting | reset | resumed`). The `switch` over `event.type` is not forced to be exhaustive. A dedicated `events: {…}` handler slot is the alternative.
- **Throwing handlers.** By construction (not tested), a handler that throws makes the `Call` promise reject, and in a `void`-ed call that is an unhandled rejection.

## Observed, skipped, not tested

**Observed (this run):**

- `tsc` 7.0.2 and VS Code's TypeScript 6.0.3 (`typescript.js` API): every form's `app`/`react`/`docs` has 0 diagnostics, and the type assertions in `app.ts` hold: inferred unions, `undefined` only without `failure`, no `errors` key for commands without errors.
- The `bad.ts` diagnostics listed above.
- Both language servers, driven headlessly over all four forms: completions, completion details, hover, definition, rename (from usage and from the generated declaration), diagnostics and quick fixes. The servers started and stopped cleanly, and no `tsc.exe` or tsserver process remained.
- Runtime behaviour in Node for A, B and C; bundle sizes; micro-benchmarks.

**Skipped:**

- `@types/node` is not in the offline npm cache. `bench/behaviour.ts` uses small local asserts instead of `node:assert`, and the `tools/*.ts` scripts are run but not type-checked.
- The tsgo method-snippet setting was not found (one guess tried).
- `b-proto` has no React or stream usage.

**Not tested, needs a human in VS Code:**

- What the IntelliSense popup, the lightbulb and the rename preview look like. The servers' answers are verified, but not VS Code's filtering, sorting and rendering.
- The *TypeScript Native Preview* extension (not installed). Its behaviour should match the 7.0.2 LSP results, but that was not checked.
- Whether marking the generated packages read-only (`files.readonlyInclude`) blocks the quick fixes and renames that edit them.
- The React component in a browser (only type-checked).
- Form C with lint rules such as `no-floating-promises`, React 19 `use()` and TanStack Query.
- The `Promise`-subclass variant of `Call`.

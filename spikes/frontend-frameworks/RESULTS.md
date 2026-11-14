# The `Call`/`Result` SDK in React, Vue and Angular

Spike for the open point of the decision "Opaque results in TypeScript" (form C, leaning): does `Call<T, E>` (a thenable with `match`/`unwrap`) and its opaque `Result` with `#private` fields work naturally in the frameworks app authors use, under the strict CSP of plan 3b? The SDK prototype is copied unchanged from `spikes/ts-results/forms/core.ts` and `forms/c-call/sdk.ts`, with the generated `copyJobs.d.ts` from `docs/app-model.md`.

## Summary

| Framework | Verdict | Evidence (details below) |
|---|---|---|
| **React 19.3.1** | **Works**, with two awkward spots | `use(call)` suspends and resolves; declared errors render through `match` without an error boundary; `use(call.unwrap())` reaches the boundary. TanStack Query works with `unwrap`, `match` and the `Call` itself; cancellation through its `signal` aborts the call. **But** `queryFn: () => copyJobs.plan(…)` types `data` as `Call` while it is a `Result` at run time, TanStack retries `unwrap` errors 3 times by default, and `no-floating-promises` does not flag a bare `Call` statement unless `checkThenables` is on. |
| **Vue 3.5.45** | **Broken by default, works with a workaround or an SDK change** | `ref(result)`, `reactive({ result })`, `ref(call)`, a `ref([])` of stream items and a Pinia option store all throw `TypeError: Cannot read private member #e from an object whose class did not declare it`, because Vue proxies the class instance. `shallowRef`, `markRaw` and `toRaw` fix it in app code. **A `Symbol.toStringTag` getter on `Result` and `Call` fixes it in the SDK** (Vue then never proxies them); so do a frozen `Result` or a `Promise`-subclass `Call`. Types: `ref(result).value` loses `#private` and is not assignable to `Result` (TS2345) unless Vue's `RefUnwrapBailTypes` is augmented. `<Suspense>` with top-level `await` of a `Call` works. |
| **Angular 22.2.6** | **Works** (CSP needs configuration, unrelated to the SDK) | `resource()` takes the `Call` as its loader (it accepts `PromiseLike`), aborts the previous call on a params change and on destroy; the `async` pipe takes the thenable; strict templates type it as `Result`. Zone.js and zoneless behave exactly as with a native `Promise` (zone: plain fields update; zoneless: only signals, `async` pipe and `resource` update). `from()` over a subscription does not abort on unsubscribe (the backend keeps running until the next item); a 10-line `Observable` wrapper with an `AbortController` fixes it. |

**Strict CSP** (plan 3b policy as a response header, production builds, `assetsInlineLimit: 0`, headless Chromium 1248): React and Vue (runtime-only build) had **0 violations**, also with `require-trusted-types-for 'script'`. Vue's full build (runtime template compiler) and Angular JIT need `'unsafe-eval'`. Angular's AOT production build had 2–4 violations by default (`<base href>` against `base-uri 'none'`, component styles injected as `<style>`, and with default optimization the critical-CSS inline `<style>` and `<script>`); without `<base>`, without component styles and with `inlineCritical: false` it had 0.

**Downloads** (growth of the npm cache): React extras 35 MB, Vue 20 MB, Angular 83 MB (`@angular/cli` + `@angular/build` toolchain); everything else was in the offline cache.

## Recommendation

Keep the opaque `Result` with `#private`, and change the pending call:

1. **Make `Call` a real `Promise` subclass** (`class Call<T, E> extends Promise<Result<T, E>>` with `match` and `unwrap`, `Symbol.species` = `Promise`). It needs no private state of its own. Evidence, tested here as a subclass beside the unchanged prototype (`shared/promise-call.ts`):
   - **TanStack Query** infers `data: Result<…>` from `queryFn: ({ signal }) => copyJobs.plan(r, { signal })`, matching run time. With the thenable it infers `Call<…>`, so `q.data.match(…)` is typed as returning a `Promise` while at run time it is synchronous (`react/src/types-check.ts`). React `use()`, TanStack at run time, Angular `resource()`, the `async` pipe, zone and zoneless all worked with the subclass.
   - **typescript-eslint** `no-floating-promises` flags a forgotten `copyJobs.plan(r);` by default (case 01). With the thenable it is silent unless the app turns on `checkThenables`; a forgotten call never reports its declared errors or failures, because only `match` reaches the interceptor. `promise-function-async` also applies (case 16).
   - **Vue** never proxies a `Promise` (its `toString` tag is `Promise`), so `ref(call)` works.
   - `instanceof Promise` is true, for libraries that check it.
   - Not measured: the cost of a `Promise` subclass per call (form C's thenable cost about 20 ns more than form A in `spikes/ts-results`).
2. **Give `Result` a `Symbol.toStringTag` getter** (for example `"SparResult"`). Vue's `reactive()` proxies only objects whose tag is `Object`, `Array` or a collection, so tagged results stay raw in `ref`, `reactive`, arrays and Pinia state, and `#private` keeps working. No effect on React or Angular. It also prints `[object SparResult]` in logs. Freezing the `Result` works in Vue too (Vue skips non-extensible objects), but **do not freeze `Call`**: React's `use()` writes `status`, `value` (and `_debugInfo` in development) onto the thenable and threw `TypeError: Cannot add property status, object is not extensible`.
3. **Optional, for Vue types:** ship (or document) the augmentation below, so `ref(result).value` keeps the `Result` type. Without it, vue-tsc reports TS2345 "Property '#private' is missing" when such a value is passed to a function typed `Result`. The augmentation must target `@vue/reactivity`; the same block on `"vue"` had no effect.

   ```ts
   declare module "@vue/reactivity" {
     interface RefUnwrapBailTypes { spar: Result<any, any> | Call<any, any> }
   }
   ```

Keeping `#private` is right: React, Angular, TanStack, RxJS and Pinia (with the tag) never needed field access. Symbol-keyed or WeakMap-held state was not needed: a WeakMap keyed by the instance fails through a Vue proxy just as `#private` does (the proxy is a different object), and a symbol key would hand handlers reactive proxies of the payloads.

**SDK and documentation guidance, whatever the form** (to carry into `docs/app-model.md` and the user guides):

- **TanStack Query:** set `retry: false` for queries in the `QueryClient` defaults when `queryFn` uses `unwrap` (default: 4 backend calls for one declared error; with `retry: false`: 1). Mutations do not retry by default. Prefer returning the `Call` (or `await`ing it) and matching in render: declared errors and failures are then data, never retried. `match` without `failure` resolves `undefined` on a failure, which TanStack v5 turns into an error state ("Query data cannot be undefined").
- **React:** never create a call during render. `use()` with an uncached `Call` (or `Promise`) suspended forever and started the backend command 30 times in 600 ms; for a command with side effects that is a replay.
- **The `docs/app-model.md` 6.2 subscription example shows "copyJobs.progress was cancelled" under StrictMode** (development), before the second subscription's first event, because the first effect's aborted stream ends with an in-band `cancelled` failure. Its `failure` handler should ignore the effect's own abort (`if (!abort.signal.aborted) …`), as the unary example already ignores `cancelled`. Production builds run effects once.
- **Lint:** recommend `@typescript-eslint/no-floating-promises` (with `checkThenables: true` if `Call` stays a thenable) and `no-misused-promises`; both already catch `Call.match(…)` left floating and `onClick={() => copyJobs.cancelAll()}`.
- **Angular:** wrap streams and subscriptions in an `Observable` whose teardown aborts (`fromSpar` in `angular/src/components.ts`), or the SDK could export one. Do not rely on `from(asyncIterable)` to stop a subscription. Use signals, `resource()` or the `async` pipe for zoneless change detection; that is Angular's rule, not Spar's.
- **Vue:** keep results in `shallowRef` or match them on arrival and store plain values; composables abort in `onScopeDispose` (`vue/src/composables.ts`).

## Method

- **Layout:** `spar-app-src/` is the stand-in `@spar/app` package: the copied SDK (`sdk/core.ts`, `sdk/c-call/sdk.ts`, `sdk/app-sdk.ts` from `spikes/app-model-check`, all unchanged), `sdk/testing.ts` (a scripted fake transport through `setTransport`), `copyJobs.d.ts` (copied from `docs/app-model.md`) and a hand-written `copyJobs.js`. `tools/build-client.mjs` compiles it with TypeScript 7.0.2 to `.js` + `.d.ts` + maps in `obj/spar/client/`, and the apps depend on it as `"@spar/app": "file:obj/spar/client"`, as the real client is linked.
- **Fake transport:** per command, a queue of steps (success, declared error, infrastructure failure, delay, hang until aborted); streams play a list of items, including the subscription events `gap`, `reconnecting`, `resumed` and `reset`. Every start, settle, abort and early return is logged, so tests count backend calls (retries) and aborts. An aborted unary call resolves as `cancelled` (`notStarted` before start, `unknown` after); an aborted stream yields one in-band `cancelled` failure and ends.
- **In-page tests:** each app runs its tests in the page (`shared/harness.ts`) and publishes `window.__results`. A test passes when it ran to the end; most record what they observed (shown text, transport counts, error messages) rather than assert, so the pass counts are not verdicts by themselves. The details are in `results/<app>.json`.
- **Builds:** Vite 8.3.8 production builds for React (plus a development build for StrictMode) and Vue (runtime-only, plus the full build for the template-compiler check); Angular with the CLI's `@angular/build:application` builder in five configurations (zoneless, zone.js, JIT, default optimization, CSP-clean). All outputs in `artifacts/frontend-frameworks/`.
- **CSP run** (`tools/run-csp.mjs`): a Node server sends the plan 3b policy verbatim as a header on the HTML, with `X-Content-Type-Options`, exact content types, `Referrer-Policy`, COOP and CORP, and collects `report-uri` posts. Playwright 1.64.1 runs headless Chromium (`chromium_headless_shell-1248`, already installed); an init script stands in for the host-injected violation listener. Each app runs twice: the base policy, and the base policy plus `require-trusted-types-for 'script'`.
- **Types and lint** (`tools/check-all.mjs`): React with `tsc` 6.0.3 and 7.0.2 under a Vite-template config and a strictest config (`skipLibCheck: false`, `exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`, …); Vue with vue-tsc 3.3.14; Angular with `ngc` under its strict template options. ESLint 10.14.0 with `typescript-eslint` 8.72.1 `strictTypeChecked` and `eslint-plugin-react-hooks` 7.1.1, on 23 one-line cases against the `Call` and against a `Promise` subclass with the same methods (`react/src/lint-cases*.tsx`).
- **Run:** `npm run build`, `npm run check`, `npm run csp` (from this folder, after `npm install --offline --ignore-scripts --no-audit --no-fund`).

## Results

### React 19.3.1 (react-dom 19.3.1, TanStack Query 5.107.1)

Production and development builds gave the same results except where noted (`results/react.json`, `results/react-dev.json`).

| Check | Observed |
|---|---|
| `use(call)` in `<Suspense>` | fallback, then the success text; 1 backend call. React added own properties `status`, `value` (development also `_debugInfo`) to the `Call` instance; `#private` is unaffected. |
| `use(call)`, declared error | rendered by `match` (`no table dbo.Orders`), no error boundary involved |
| `use(call)`, failure, no `failure` handler | the interceptor received `lost`; `match` returned `undefined` |
| `use(call.unwrap())`, declared error | the error boundary got `SparError` with `error.code = "connectionNotFound"` |
| `use()` with a call created during render | suspended forever; 30–31 backend calls in 600 ms. A native `Promise` created the same way: identical (30). Not SDK-specific. |
| `use()` with a frozen `Call` | `TypeError: Cannot add property status, object is not extensible` (development: `_debugInfo`) |
| `use()` with a `Call` that has `Symbol.toStringTag` | works |
| TanStack `queryFn` → `unwrap()`, declared error, default retry | error state after **4** backend calls (`failureCount` 4; `retryDelay` set to 1 ms for the test) |
| same with `retry: false` | 1 backend call |
| TanStack `queryFn` → the `Call` | `data` is the `Result` at run time (`instanceof Call` false), a declared error is `success` data, 1 call. Types say `Call` (see Types). |
| TanStack `queryFn` → `async` + `await` the `Call` | `data: Result`, typed and at run time |
| TanStack `queryFn` → `match` without `failure`, failure | interceptor called once; query in error state: "Query data cannot be undefined"; 1 call |
| `cancelQueries` while pending | the call's signal aborted it; status `pending`, `fetchStatus: idle`, no error, no retry |
| unmount of the only observer while pending | aborted |
| `useMutation` + `unwrap`, declared error | 1 call (mutations do not retry by default) |
| subscription in `useEffect` under StrictMode, `docs/app-model.md` 6.2 as written | development: 2 starts, the first aborted at once; status history `copyJobs.progress was cancelled`, `live`, `3 updates skipped`, `reconnecting (attempt 1)`, `live`, `live`; the first visible status was "copyJobs.progress was cancelled". Production: 1 start; the cancelled status came only after unmount (a state update on an unmounted component, harmless). Unmount aborted the remaining subscription in both. |
| same, `failure` ignoring its own abort | no cancelled status in either build |
| unary call in `useEffect` under StrictMode (`cancelled` ignored) | development: 2 starts, 1 aborted, no problem reported |
| `Promise`-subclass `Call` | `use()` and TanStack worked; declared error as data, 2 calls for 2 uses, no retry; `instanceof Promise` true |

**typescript-eslint** (`results/react-eslint.txt`, `results/react-eslint-thenables.txt`), `strictTypeChecked`:

| Case | `Call` (thenable) | `Promise` subclass |
|---|---|---|
| 01 `copyJobs.plan(r);` | **not flagged**; flagged with `checkThenables: true` | `no-floating-promises` |
| 02 `copyJobs.plan(r).match(…);` | `no-floating-promises` | `no-floating-promises` |
| 07 `.then(…)` without a rejection handler | `no-floating-promises` | same |
| 08 `.unwrap();` as a statement | `no-floating-promises` | same |
| 09 `if (copyJobs.plan(r))` | `no-misused-promises`, `no-unnecessary-condition`; `tsc` itself reports TS2801 for both forms | same |
| 13 async `success` handler in a stream loop | `no-floating-promises` | same |
| 14 `forEach(() => copyJobs.cancelAll())` | `no-misused-promises` | same plus `promise-function-async` |
| 16 function returning a call without `async` | not flagged by `promise-function-async` | flagged |
| 19 `match` in an effect without `void` | `no-floating-promises` | same |
| 20 `onClick={() => copyJobs.cancelAll()}` | `no-misused-promises` | same plus `promise-function-async` |
| 21 `onClick={() => copyJobs.cancelAll().match(…)}` | `no-misused-promises`, `promise-function-async`, `no-confusing-void-expression` | same |
| 03–06, 10–12 (`void`, `await`, `Result.match`, assigned, `Promise.all`, stream item `match`) | nothing | nothing |

`no-misused-promises` and `return-await` already treat the thenable like a promise; only `no-floating-promises` (by default) and `promise-function-async` differ.

### Vue 3.5.45 (Pinia 4.1.0, @vitejs/plugin-vue 6.0.11, vue-tsc 3.3.14)

`results/vue.json`:

| Check | Observed |
|---|---|
| `ref(result).value.match` / `.unwrap` | `isReactive` true; both throw `TypeError: Cannot read private member #e from an object whose class did not declare it` |
| `reactive({ result }).result.match` | same `TypeError` |
| `ref(call)`: `await r.value` and `r.value.match` | same `TypeError` (the proxy is the receiver of `then`) |
| stream items pushed into `ref([])`, read back | same `TypeError`; a `shallowRef` array replaced on each item works; matching on arrival and storing plain rows works |
| `shallowRef`, `ref(markRaw(…))`, `toRaw(ref.value)` | work |
| `Result` with `Symbol.toStringTag`, in `ref`, in a `reactive` array, in a Pinia option store | not proxied (`isReactive` false); work |
| `Call` with `Symbol.toStringTag` in `ref` | `await` and `match` work |
| frozen `Result` in `ref` | not proxied; works |
| `Promise`-subclass call in `ref` | not proxied; `await` works |
| Pinia option store holding a `Result` | `TypeError`; with `markRaw` works; a setup store with `shallowRef` works |
| component: `Result` in `ref`, `match` in the template | nothing rendered; `app.config.errorHandler` got the `TypeError` |
| component: `Result` in `shallowRef` | renders |
| `<Suspense>` with top-level `await copyJobs.plan(…)` in `<script setup>` | fallback, then the declared error text; `onUnmounted` registered after the `await` ran. Vue's `isPromise` needs a `catch` method, which `Call` lacks, so `withAsyncContext` skips its rejection hook; harmless because a `Call` never rejects. |
| composables (`useCall`, `useSubscription`) with `onScopeDispose` | the subscription (after `event`, `gap`, `reconnecting`, `resumed`, `reset`, `event`) and a pending call were both aborted on unmount |
| `:style` string binding and `v-bind()` in `<style>` | applied under `style-src 'self'` (Vue sets them through CSSOM) |

**vue-tsc** (`results/vue-tsc-bad.txt`): `show(ref(result).value)` and `show(reactive({ result }).result)` give TS2345 "Property '#private' is missing in type '{ match: …; unwrap: … }' but required in type 'Result<CopyPlan, PlanError>'". `match` on the unwrapped value still type-checks, `shallowRef`, `markRaw` and `ref<Result<…>>()` keep the type, and with `RefUnwrapBailTypes` augmented on `@vue/reactivity` (`vue/src/bad-bail/`) the same lines compile. In templates, a missing case is TS2741 and a misspelled case TS2561 with "Did you mean 'tableMissing'?", as in plain TypeScript.

### Angular 22.2.6 (CLI and `@angular/build` 22.2.6, zone.js 0.16.3, RxJS 7.8.2, TypeScript 6.0.3)

Angular 22 makes `OnPush` the default change detection; the plain-field components set `ChangeDetectionStrategy.Eager` to test zone behaviour. Tests run inside `NgZone.run`, as bootstrap and event code do. `results/angular-zone.json`, `results/angular-zoneless.json`:

| Check | zone.js | zoneless |
|---|---|---|
| signal set after `await` of a `Call` (OnPush) | updates | updates |
| plain field set after `await` of a `Call` | updates | does not update |
| baseline: plain field after `await` of a native `Promise` | updates | does not update |
| plain field in the `Call`'s `then` callback | updates | does not update |
| plain field after `await` of a `Call` settled from the root zone (as a bridge listener registered before bootstrap would) | updates | does not update |
| `resource({ loader: … => copyJobs.plan(…, { signal: abortSignal }) })` | value is the `Result`; a params change aborted the hanging first call; 2 calls | same |
| `resource()` destroyed while loading | aborted | aborted |
| `resource()` with an `unwrap` loader, declared error | status `error`, `error()` is the `SparError`; 1 call | same |
| `async` pipe with a `Call` | renders the `Result` | same |
| `from(copyJobs.progress(…))`, unsubscribe after the first item | nothing stopped after 100 ms; the iterator was returned 256–262 ms later, when the next item arrived | same |
| `fromSpar` (an `Observable` with an `AbortController` teardown) | aborted at once | same |
| component subscription with `DestroyRef`, and `toSignal(fromSpar(…))` | rendered `live: 3000 rows` / `rows 3000`; aborted on destroy | same |
| `Promise`-subclass call in `resource()`, the `async` pipe and an awaited field | all three rendered | same (the field was refreshed by the other bindings' change detection) |

The zone build downlevels `async`/`await` to generators (30 `function*` and 2 `async` in the bundle, against 4 and 17 in the zoneless one), so `await` goes through zone.js's patched `Promise`; the thenable behaved like a native promise in both modes.

**Strict templates** (`results/angular-ngc-bad.txt`): the `async` pipe types `call | async` as `Result<CopyPlan, PlanError>`, so `r.value` is TS2339. Angular 22 accepts arrow functions in template expressions, so `match` can be written in the template; a missing case is TS2741, and a misspelled case TS2353 (no "Did you mean" suggestion, unlike TS2561 in plain TypeScript).

## Strict CSP results

`results/csp-summary.txt`, each app with the base policy and with Trusted Types added:

| Build | Violations | Cause, and the configuration that avoids it |
|---|---|---|
| React production and development | 0 / 0 | – (the SVG asset was a file URL, not `data:`, with `assetsInlineLimit: 0`) |
| Vue runtime-only build (Vite default) | 0 / 0 | – |
| Vue full build (`vue/dist/vue.esm-bundler.js`, `template:` option) | 1 / 1 | `script-src` `eval` (with Trusted Types: the `Function` sink). Mounting threw `EvalError`. Use SFCs or render functions with the default runtime-only build. |
| Angular zoneless and zone.js (AOT) | 2 / 2 | `base-uri` blocked the template's `<base href="/">`: remove it (or set `APP_BASE_HREF` if the router needs it). `style-src-elem` blocked the `<style>` Angular injects for component `styles`, which were not applied: keep styles in global stylesheets, or the policy needs a nonce, which a constant Release header cannot carry. |
| Angular JIT (`aot: false`) | 2 / 2, app did not start | `script-src` `eval` from the JIT compiler (`EvalError` at startup), plus `base-uri`. AOT is required. |
| Angular default production optimization | 4 / 4 | the two above, plus critical-CSS inlining (Beasties): an inline `<style>` and an inline `<script>` that switches the stylesheet from `media="print"`; with that script blocked, **the global stylesheet was never applied**. Set `optimization.styles.inlineCritical: false`. |
| Angular CSP-clean (no `<base>`, no component styles, `inlineCritical: false`) | 0 / 0 | – |

Every violation was seen both by the injected listener and through `report-uri`. React, Vue and Angular (AOT) all ran under `require-trusted-types-for 'script'` without violations.

## Download sizes

`results/downloads.txt` (growth of the npm cache, `du`, including registry metadata):

| Step | MB | Notes |
|---|---|---|
| base tools | 0 | TypeScript 6.0.3 and 7.0.2, Vite 8.3.8, Playwright 1.64.1 from the offline cache |
| React | 35 | `@tanstack/react-query`, `eslint-plugin-react-hooks` 7.1.1 (which pulls in Babel); React, ESLint 10 and typescript-eslint were cached |
| Vue | 20 | `vue`, `@vitejs/plugin-vue`, `vue-tsc`, `pinia` |
| Angular | 83 | `@angular/*` 22.2.6 with `@angular/cli` and `@angular/build`; includes one failed attempt (below) |

`node_modules` is 374 MB on disk. No Playwright browser was downloaded.

## Problems met

- **`@angular/build` 22.2.7 could not be installed `--prefer-offline`:** it needs `@angular-devkit/architect` 0.2202.7, which the cached registry metadata did not list (ETARGET). 22.2.6 (matching the cached CLI) installed.
- **Angular 22 defaults to `OnPush`.** The first zone.js run showed plain fields not updating even for a native `Promise`; the cause was the new default, not the thenable. The tests also first ran outside the Angular zone (in the bootstrap promise's callback).
- **Angular names the output after the index input file** (`index.clean.html`); `index: { input, output: "index.html" }` fixed it. The CLI writes its cache to `.angular/` in the workspace by default; `cli.cache.path` moves it under `obj/`.
- **`RefUnwrapBailTypes` augmented on `"vue"` had no effect;** on `"@vue/reactivity"` it worked.
- **Bash heredocs on this machine dropped a backslash** in a regex (`/[\\/]/` became `/[\/]/`); edited with the editor instead.

Candidate pitfalls for `docs/pitfalls.md` (not edited by this spike): Vue proxies class instances with `#private` fields; React's `use()` writes to the thenable; TanStack's `queryFn` types a thenable as the data; `no-floating-promises` ignores thenables by default; `from(asyncIterable)` does not stop on unsubscribe; Angular's `<base href>`, component styles and critical-CSS inlining violate the strict CSP; Angular 22's `OnPush` default.

## Not tested

- The real WebView2 and WebKitGTK hosts and the real bridge transport: everything here ran in headless Chromium against the fake transport, served from `http://127.0.0.1` (so `connect-src https://app.localhost` was never exercised).
- Development servers (Vite dev, `ng serve`) under the CSP, HMR, and React Fast Refresh with `use()`.
- The `Promise`-subclass `Call` as generated code: it was a wrapper over the prototype's `Call`, not a rewrite of `sdk.ts`; its per-call cost and bundle size were not measured. No editor (language server) probes were run for it.
- Angular's `resource({ stream })` and `rxResource` as subscription adapters; Angular SSR; `@angular/forms` and the router.
- Vue development builds, Vue devtools, Nuxt; React Server Components, the React Compiler, Next.js; `useSyncExternalStore` adapters.
- Other state libraries that proxy objects (MobX, Valtio, Immer's frozen drafts) and Svelte or Solid.
- Editor behaviour (hover, completion) inside `.vue` and Angular templates; only compiler diagnostics were captured.
- Throwing handlers inside `match` in each framework (unhandled rejections).

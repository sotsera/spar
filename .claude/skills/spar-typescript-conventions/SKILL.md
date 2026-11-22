---
name: spar-typescript-conventions
description: "Write or review Spar TypeScript and JavaScript: the SDK and Vite plugin in `src/Spar.Client` (bridge protocol, transports, `Call`/`Result`, failure hooks, streams, subscriptions, page APIs), their source style, and the public shape of the `@spar/app` package and its generated modules."
---

# Write Spar frontend code

The design is plan sections 2b (SDK) and 2 ("Generated client" and the wire-rules table) in `docs/design/contracts.md`, and 2c (bridge) in `docs/design/bridge.md`. `docs/app-model.md` sections 5 and 6 show the public shape, compiled; a change to that shape updates it and its check in the same change (`tests/AppModel`, which `dotnet test` runs through `tests/AppModel.Tests`). Generator work also needs `spar-contract-generation`.

## Style

**Readable first.** This covers `src/Spar.Client` and the generated modules, which users open through go-to-definition.
- Small functions with one purpose, early returns, and named intermediate values instead of dense expressions or long chains.
- One statement per line, lines of about 120 characters, a blank line between distinct steps.
- Advanced types (mapped, conditional, `NoInfer`) only at public boundaries that need them, with a doc comment and a usage example; name helper types instead of nesting them.
- Comment why, not what. Follow the nearby formatting, module resolution and browser targets.
- Named exports, type-only imports, explicitly typed public boundaries.

## Package

- **`@spar/app`** is one `file:` package under `obj/spar/client`: `/<contract>` and `/windows` are generated, `/sdk` and `/vite` ship compiled, `/telemetry` is optional and loads lazily, and `/testing` (generated) gives page tests `fakeSpar` (`docs/app-model.md` section 6.10), whose `requestClose` drives the page's close guards as the host does. `/windows` holds `windows.onCloseRequested`: guards run in the order added, the first keep or ask ends the run, a throw or a non-answer is `failed`; the connection answers `close-request` with `close-answer` and aborts the guard's `signal` on `close-ended`, and lists both host types in `hello`.
- **Shape:** `"type": "module"`, `"sideEffects": false`, subpath exports `{ "types": "./x.d.ts", "default": "./x.js" }`, `.js` extensions in relative imports. Whether the package also gets a root barrel is settled in M1a: the spike had one, and no decision asks for it.
- **The SDK ships compiled:** ES modules, `.d.ts`, `.d.ts.map`, source maps, and its `.ts` sources under `src/`, never beside a `.d.ts` of the same name. It is compiled with its own strict settings, separately for the browser (DOM) and the Vite plugin (Node); the app's `tsconfig` never compiles it.
- **Tree shaking:** no top-level side effects. Optional parts are separate modules or free functions, never methods or properties of an exported object, which bundlers always keep.
- **Development-only code** (the Hot Reload toast, the development overlay) is a no-op in Release that the bundler removes. Production bundles carry none.
  - Guard it with `import.meta.env?.DEV === true` at each use, never through an alias or a helper: Vite's development server makes it `true` and `vite build`, which every bundle Spar embeds goes through, folds it to `false` (Vite 8.3.8 drops the branch with or without `?.`), so the branch and the modules only it imports are dropped. The `?.` matters: a page that loads the SDK without Vite (WireShapes' test page) has no `import.meta.env`, and `import.meta.env.DEV` threw there. The SDK's `tsconfig.json` and `tsconfig.test.json` list `vite/client` for its type; Vitest makes it `true`, and `vi.stubEnv("DEV", false)` tests the production path.
  - As built (M1d step 3): `onBackendUpdate` (`src/sdk.ts`) and `configureSpar({ backendUpdateToast })` (`src/results.ts`) reach `src/backendUpdate.ts` (the handlers, the payload read field by field) and `src/overlay.ts` (the toast) only behind the guard; `connectSpar` listens for `backend-updated` only there. `fakeSpar().backendUpdate({ types })` sends the event in page tests. The build tests' page markers (`backend-updated`, `data-spar-dev-overlay`, `Backend updated`) prove a Production bundle has none of it; a new development-only module adds its own marker there.
  - **Overlay UI** under the strict policy: a host element with an open shadow root that adopts a constructed stylesheet (`new CSSStyleSheet()`, `replaceSync`, `adoptedStyleSheets`), elements from `createElement`, text from `textContent`; never a `<style>` element, a `style` attribute or an HTML string.

## Calls and results

- **Results are opaque:** the value is reachable only through `match` and `unwrap`. Keep `#private` fields; a WeakMap fails through Proxies the same way.
- **`Call<T, E>` extends `Promise<Result<T, E>>`** and adds `match` and `unwrap`. Construct it directly (no wrapping `of` form), keep `Symbol.species` = `Promise` so `then`/`catch`/`finally` return plain promises, and export `Call` as a type only. Never freeze it: React's `use()` writes onto it.
- **`Result` and `Call` have a `Symbol.toStringTag` getter,** so Vue never proxies them. The `@vue/reactivity` `RefUnwrapBailTypes` augmentation is a separate, optional module.
- **`match({ success, errors, failure })`:** `errors` has one handler per declared case and is absent when there are none. `success` and `failure` get their own type parameters, `NoInfer` guards the error keys, and handlers are anchored on the generated `ErrorHandlers<E>` (pitfalls, generic `match`). A `failure` function handles every failure; an object handles the `CallError` kinds it lists. The rest go to `onFailure`, `match` returns `undefined` for them, and its type says so.
- **`unwrap`** throws `SparError` and never reaches the interceptor.
- **`configureSpar({ onFailure, observe, resume })`:** `onFailure` gets unhandled failures, `observe` sees every failure. Neither can retry, change a result, or turn a failure into a success.
- **`CallError`** has `kind`, `code` and `execution`, with the kinds and codes of the decision "`CallError` kinds", plus `contract` and `command`, which the SDK fills from its own record of the call id (decision "Bridge messages: encoding C header fields"). `execution` is `notStarted` only when the host is certain. Declared errors are never a `CallError`; an error code the SDK does not know is `protocol` / `unknown-error-case`. Optional `correlation` (a 32-hex trace id, in every build) and `detail` (Development only) come from the failure's payload, read as untrusted: own string properties only, `correlation` matched against its shape.
- **Cancellation:** pass `AbortSignal` through. A deadline is `AbortSignal.timeout(ms)`; there is no default deadline or idle timeout. Aborting ends the caller's wait. Report `cancelled` only when the backend confirms it, otherwise outcome unknown. Never retry or replay a call.

## Streams and subscriptions

- **Every item is a `Result`,** and a terminal failure is the last item, in-band. Streams that declare errors give their `match` an `errors` object.
- **Iterate with a hand-written iterator** over the transport's buffer, and wrap each item when it is handed out. Never layer async generators.
- **`Subscription<T>` items** match on `success`, `events`, `errors` and `failure`. `events.reset` is required; `gap`, `reconnecting` and `resumed` are optional, and each subscription offers only the events it can emit (a coalesced one has no `gap`). Events never reach the failure hooks.
- **Resume** (plan M3, "Subscription resumption"): the contract sets the most a subscription may do, and the page option and `configureSpar` can only be more cautious. An automatic resume emits `reconnecting`, then `reset` before the first new value, then `resumed`, and gives up with `transport` / `lost`. One-shot streams never resume.

## Bridge and wire values

- **Messages use encoding C:** a JSON header, a TAB, then the payload's own JSON, always as strings, with the types and fields of plan section 2c ("The messages"). Negotiate the version in `hello`/`open`, fail a pending call that receives an unknown message type, accept only safe-integer ids, reject duplicate header keys, and read trace values tolerantly. `traceparent` is reserved on every call, and streams use credits on every host. The letter-line tables in plan 2c are spike evidence, not the format.
- **A transport keeps the page's messages in order:** the host drops a call whose id is not higher than every earlier one. On loopback (`src/loopback.ts`) every page message goes through one request at a time to `/_spar/send`, batched one per line, and every host message comes on the document's channel; never a `fetch` per call (decision "Loopback transport: one ordered send path"). Split lines on LF only. A refused or failed request closes the channel, so sent calls end `transport` / `lost`.
- **A dev session's restart** (`src/restart.ts`, M1d step 2): a loopback channel whose response names the backend's run (`X-Spar-Run`; only a Development loopback host sends it) waits, once it ends and only if the document received `open`, for `POST /_spar/session` to answer another run, then reloads once; the same run, 401/403 and 404 stop the wait with one console line, and a reload counts against the loop guard only when the document it makes gets no backend (no `open`; M1d step 3 follow-up): `backendOpened` at `open` starts the count again, a document that never got `open` waits again only when an automatic reload made it (`spar.restart.run`), and the third such reload in a row stops the reloads (session storage). Never resend a call; the reload starts afresh. Tests pass `restart: RestartPage` (or `false`) to `loopbackChannel`.
- **The heartbeat** (plan 3, "Hung pages"): the desktop host's injected script answers each `ping` with a `pong` of the same `seq`, from the document's creation; the SDK neither lists `ping` in `hello` nor answers it, and ignores the message. The injected script (`src/Spar.Windows/PageScript.js`, tested by `test/pageScript.test.ts`) follows the same rules as SDK code, and defines nothing on `window` (`spar-page-security`).
- **Narrow untrusted data** before using it. Keep wire names exactly, and keep optional, nullable and required properties distinct.
- **Build objects from wire data field by field,** never with `Object.assign` or spread: a parsed `__proto__` key then sets the copy's prototype.
- **Follow the wire-rules table:** `long`/`ulong` as `bigint`, written only by the generated encoder (`JSON.stringify` throws on it); `byte[]` as `Uint8Array`; `Number.isFinite` before sending a number; lone surrogates replaced with U+FFFD before sending.

## Page APIs

- **Page APIs return `Call`s with declared errors,** not `null` or booleans. A closed dialog is `dismissed`, never `cancelled`, which is the `CallError` of an aborted call. Grants show the page only names.
- **Make mistakes compile errors:** generated unions for declared names (window kinds), keyed objects for kinds and buttons, action objects instead of string ids. Where the type cannot catch a duplicate, throw at once and name both.
- **SDK code runs under the default CSP and Trusted Types:** no `eval`, `new Function` or string timers, no HTML string sinks, no injected `<style>` elements or `style` attributes, no `data:` URLs, no `new Worker(url)` without a Trusted Types policy.
- **The Vite plugin** (`src/vite/`): Subresource Integrity on the Release bundle and the external-integrity check live in `integrity.ts` as pure functions (`test/integrity.test.ts`); `index.ts` reads `csp.json` relative to itself (`../../csp.json` in the package) on every use. The start-up styles (theme and splash, plan 3d) are `pageStyle.ts` (`test/pageStyle.test.ts`), from `theme.json` read the same way, in an `order: "post"` `transformIndexHtml`; `spar({ splash: { delay, minimumVisible, fadeIn, fadeOut } })` sets the timing, checked when the plugin is created.
- **Start-up** (M1d step 5, `src/splash.ts`): while the page has `#spar-splash`, `connectSpar` requests animation frames until the first one at or after the mark's scheduled appearance (its entrance animation's start plus its delay), noting `performance.now()` in the callback (never its time stamp, which Chromium takes when the frame began); the minimum visible time counts from that frame, or from now when no frame has rendered since it was due (the document timeline reads the current time inside a long task on both engines, so it cannot tell) (WebKitGTK under Xvfb draws animations only in the page's frames; WebView2 on its compositor); `connection.ready()` sends `ready` once per document, at once without `#spar-splash`, else after `finishSplash()` removed it (at once before the mark appeared, else after its minimum time and a Web Animations fade-out) and two frames passed, each frame waited for at most 100 ms.
- **Host commands:** an SDK page API the host answers itself goes through `invoke` with the contract name `spar` (a reserved subpath, so no app contract has it), as `setColorScheme` (`src/colorScheme.ts`) does: a `Call<void, ColorSchemeError>` whose request the encoder checks before it leaves. `createFakeSpar` adds its codec to the app's commands, answering success until a test scripts it.
- **Stay independent of UI frameworks.** The React, Vue and Angular checks are in the SDK's own tests (`test/frameworks/`, run by `npm test` and `npm run check`): React's `use()` and error boundaries, Vue's `ref`/`reactive`/Pinia holding a raw `Result`, Angular's `resource()` with a `Call` loader, and the TanStack Query and Angular types. A change to `Call` or `Result` must keep them passing. Framework guidance for the docs is in plan 2b.

## Verify

- Vitest with fake channels for each host type, the fake backend of `src/testing.ts` checked through a hand-written contract module (`test/testing.test.ts`, its type tests included), type tests (code that must compile, and code that must fail with the expected error), the `tsconfig` × TypeScript 6/7 matrix, and the conformance vectors shared with the C# tests.
- Change generated modules in their generator. When an export or import shape changes, load the built JavaScript in a real consumer.
- The evidence for these rules is in `docs/pitfalls.md` ("npm, Vite and TypeScript", "Bridge and loopback", "Page security", "Wire format and serialization").

---
name: spar-typescript-conventions
description: "Write or review Spar TypeScript and JavaScript: the SDK and Vite plugin in `src/Spar.Client` (bridge protocol, transports, `Call`/`Result`, failure hooks, streams, subscriptions, page APIs), their source style, and the public shape of the `@spar/app` package and its generated modules."
---

# Write Spar frontend code

The design is plan sections 2b (SDK) and 2 ("Generated client" and the wire-rules table) in `docs/design/contracts.md`, and 2c (bridge) in `docs/design/bridge.md`. `docs/app-model.md` sections 5 and 6 show the public shape, compiled; a change to that shape updates it and its check in the same change (`spikes/app-model-check` until M1a rebuilds it under `tests/`). Generator work also needs `spar-contract-generation`.

## Style

**Readable first.** This covers `src/Spar.Client` and the generated modules, which users open through go-to-definition.
- Small functions with one purpose, early returns, and named intermediate values instead of dense expressions or long chains.
- One statement per line, lines of about 120 characters, a blank line between distinct steps.
- Advanced types (mapped, conditional, `NoInfer`) only at public boundaries that need them, with a doc comment and a usage example; name helper types instead of nesting them.
- Comment why, not what. Follow the nearby formatting, module resolution and browser targets.
- Named exports, type-only imports, explicitly typed public boundaries.

## Package

- **`@spar/app`** is one `file:` package under `obj/spar/client`: `/<contract>` and `/windows` are generated, `/sdk` and `/vite` ship compiled, `/telemetry` is optional and loads lazily.
- **Shape:** `"type": "module"`, `"sideEffects": false`, subpath exports `{ "types": "./x.d.ts", "default": "./x.js" }`, `.js` extensions in relative imports. Whether the package also gets a root barrel is settled in M1a: the spike had one, and no decision asks for it.
- **The SDK ships compiled:** ES modules, `.d.ts`, `.d.ts.map`, source maps, and its `.ts` sources under `src/`, never beside a `.d.ts` of the same name. It is compiled with its own strict settings, separately for the browser (DOM) and the Vite plugin (Node); the app's `tsconfig` never compiles it.
- **Tree shaking:** no top-level side effects. Optional parts are separate modules or free functions, never methods or properties of an exported object, which bundlers always keep.
- **Development-only code** (the Hot Reload toast, the development overlay) is a no-op in Release that the bundler removes. Production bundles carry none.

## Calls and results

- **Results are opaque:** the value is reachable only through `match` and `unwrap`. Keep `#private` fields; a WeakMap fails through Proxies the same way.
- **`Call<T, E>` extends `Promise<Result<T, E>>`** and adds `match` and `unwrap`. Construct it directly (no wrapping `of` form), keep `Symbol.species` = `Promise` so `then`/`catch`/`finally` return plain promises, and export `Call` as a type only. Never freeze it: React's `use()` writes onto it.
- **`Result` and `Call` have a `Symbol.toStringTag` getter,** so Vue never proxies them. The `@vue/reactivity` `RefUnwrapBailTypes` augmentation is a separate, optional module.
- **`match({ success, errors, failure })`:** `errors` has one handler per declared case and is absent when there are none. `success` and `failure` get their own type parameters, `NoInfer` guards the error keys, and handlers are anchored on the generated `ErrorHandlers<E>` (pitfalls, generic `match`). A `failure` function handles every failure; an object handles the `CallError` kinds it lists. The rest go to `onFailure`, `match` returns `undefined` for them, and its type says so.
- **`unwrap`** throws `SparError` and never reaches the interceptor.
- **`configureSpar({ onFailure, observe, resume })`:** `onFailure` gets unhandled failures, `observe` sees every failure. Neither can retry, change a result, or turn a failure into a success.
- **`CallError`** has `kind`, `code` and `execution`, with the kinds and codes of the decision "`CallError` kinds". `execution` is `notStarted` only when the host is certain. Declared errors are never a `CallError`; an error code the SDK does not know is `protocol` / `unknown-error-case`.
- **Cancellation:** pass `AbortSignal` through. A deadline is `AbortSignal.timeout(ms)`; there is no default deadline or idle timeout. Aborting ends the caller's wait. Report `cancelled` only when the backend confirms it, otherwise outcome unknown. Never retry or replay a call.

## Streams and subscriptions

- **Every item is a `Result`,** and a terminal failure is the last item, in-band. Streams that declare errors give their `match` an `errors` object.
- **Iterate with a hand-written iterator** over the transport's buffer, and wrap each item when it is handed out. Never layer async generators.
- **`Subscription<T>` items** match on `success`, `events`, `errors` and `failure`. `events.reset` is required; `gap`, `reconnecting` and `resumed` are optional, and each subscription offers only the events it can emit (a coalesced one has no `gap`). Events never reach the failure hooks.
- **Resume** (plan M3, "Subscription resumption"): the contract sets the most a subscription may do, and the page option and `configureSpar` can only be more cautious. An automatic resume emits `reconnecting`, then `reset` before the first new value, then `resumed`, and gives up with `transport` / `lost`. One-shot streams never resume.

## Bridge and wire values

- **Messages use encoding C:** a JSON header, a TAB, then the payload's own JSON, always as strings. Negotiate the version in `hello`/`open`, fail a pending call that receives an unknown message type, accept only safe-integer ids, reject duplicate header keys, and read trace values tolerantly. `traceparent` is reserved on every call, and streams use credits on every host. The letter-line tables in plan 2c are spike evidence, not the format.
- **Narrow untrusted data** before using it. Keep wire names exactly, and keep optional, nullable and required properties distinct.
- **Build objects from wire data field by field,** never with `Object.assign` or spread: a parsed `__proto__` key then sets the copy's prototype.
- **Follow the wire-rules table:** `long`/`ulong` as `bigint`, written only by the generated encoder (`JSON.stringify` throws on it); `byte[]` as `Uint8Array`; `Number.isFinite` before sending a number; lone surrogates replaced with U+FFFD before sending.

## Page APIs

- **Page APIs return `Call`s with declared errors,** not `null` or booleans. A closed dialog is `dismissed`, never `cancelled`, which is the `CallError` of an aborted call. Grants show the page only names.
- **Make mistakes compile errors:** generated unions for declared names (window kinds), keyed objects for kinds and buttons, action objects instead of string ids. Where the type cannot catch a duplicate, throw at once and name both.
- **SDK code runs under the default CSP and Trusted Types:** no `eval`, `new Function` or string timers, no HTML string sinks, no injected `<style>` elements or `style` attributes, no `data:` URLs.
- **Stay independent of UI frameworks.** When `Call` or `Result` changes, rerun the React, Angular and Vue checks of `spikes/frontend-frameworks`, which M1a moves into the SDK's tests. Framework guidance for the docs is in plan 2b.

## Verify

- Vitest with fake channels for each host type, type tests (code that must compile, and code that must fail with the expected error), the `tsconfig` × TypeScript 6/7 matrix, and the conformance vectors shared with the C# tests.
- Change generated modules in their generator. When an export or import shape changes, load the built JavaScript in a real consumer.
- The evidence for these rules is in `docs/pitfalls.md` ("npm, Vite and TypeScript", "Bridge and loopback", "Page security", "Wire format and serialization").

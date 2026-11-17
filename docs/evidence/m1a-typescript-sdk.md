# M1a: the TypeScript SDK's core

Recorded 2026-11-15. What it shows: the page side of the bridge reads and writes the decided messages exactly as the C# side does (the shared vectors), and `Call`, `Result`, `CallError` and the failure hooks behave as decided. Decision: "TypeScript SDK core".

## Environment

Node 24.21.0, TypeScript 6.0.3 and 7.0.2, Vitest 4.1.10, `@types/node` 24.19.2 (47 + 2 packages, 68 MB under the git-ignored `src/Spar.Client/node_modules`). Windows 11 only.

## What was added (`src/Spar.Client/`)

| File | Contents |
|---|---|
| `src/protocol.ts` | Encoding C. The decoder scans the header's top-level keys itself, because `JSON.parse` keeps only the last of two equal keys, and checks integers as written: `7.0` and `4e0` are not integers, matching .NET's `TryGetInt64`. Strings must be well-formed text (`isWellFormed`), and trace values that are not are dropped. The encoder writes the canonical field order. |
| `src/results.ts` | The decided types (`CallError` with `contract` and `command`, `ErrorHandlers` with no parameter for payload-less cases, `Handlers`, `FailureResults`), the opaque `Result`, `Call` as a `Promise` subclass with `Symbol.species` = `Promise` and `toStringTag`s, `SparError`, and `configureSpar`. |
| `src/connection.ts` | One document's connection over a host `BridgeChannel`: hello with the fingerprint and understood types, calls queued until open, increasing ids, field-by-field decoders from generated code, unknown error codes, aborts (cancel sent; the backend's confirmation or the real result wins; outcome unknown after 5 s), a lost channel (`transport` / `lost` for sent calls, `unavailable` for unsent ones), events, `ready`, and close work answered `closing-done`. |
| `src/sdk.ts` | The public surface and `invoke` for generated modules; `connectSpar` for the host's start-up script. |
| `test/` | Vitest: the shared vectors, 18 connection tests against a scripted host, and 11 result-routing tests. |

## Results (observed)

| Check | Result |
|---|---|
| `npm test` | 103 of 103: 76 vector checks (24 canonical encodes, 24 canonical decodes, 10 other forms, 18 malformed), 18 connection, 11 results (`Call` and `Result` included) |
| Shared vectors, both sides | TypeScript 76 of 76 and C# 95 of 95 after two cases were added (`7.0` as an id, `4e0` as credits); the TypeScript encoder writes the same canonical bytes as the C# one for all 24 types |
| `npm run check` | sources and tests clean on TypeScript 6.0.3 and 7.0.2 (`strict`, `exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`) |
| A parsed `__proto__` key in a declared error | ignored by the field-by-field decoder; `Object.prototype` untouched |

## Not done or not verified

- **Not built:** the per-host transports (WebView2, WebKitGTK, loopback), the generated TypeScript modules and the client step, packaging as `@spar/app/sdk`, streams and subscriptions, the Vite plugin, and the type tests of `match` (code that must fail to compile).
- **Rerun since (2026-11-15):** the React, Vue and Angular checks were rebuilt against this `Call` and `Result` in `test/frameworks/` (decision "The frontend-framework checks in the SDK's tests").
- **Not rerun (at the time):** the React, Angular and Vue checks of `spikes/frontend-frameworks` against this `Call` and `Result`, which the conventions require when they change (they still run against the spike's own copy).
- **Not measured:** the 5 s confirmation timeout. It is a choice made here, not a decided number.
- **Not run:** a real browser, so the page side runs in Node only. `String.prototype.isWellFormed` needs Chromium 111+ or Safari 16.4+; the WebKitGTK minimum (2.48) is assumed to have it, which is untested.

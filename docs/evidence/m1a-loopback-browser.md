# M1a: the loopback bridge and the Browser surface

Recorded 2026-11-15. What it shows: a page in Chromium calls an app's backend running in the test process, over the loopback bridge and the compiled TypeScript SDK, with the decided messages, checks and page headers. Decision: "Loopback transport: one ordered send path".

## Environment

Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24.21.0, `Microsoft.Playwright` 1.64.1 with `chromium_headless_shell-1248` (already installed under the user's Playwright cache by earlier spikes), `xunit.v3` and `xunit.v3.aot` 4.0.3. Warnings as errors with the trim and AOT analyzers on. Not run on Linux.

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Hosting/SparPageHeaders.cs` | The page headers of section 3b for every host (`SparPageHeaders`) and the exact content type per file type (`SparContentTypes`; unlisted types are not served). |
| `src/Spar.Loopback/` | `LoopbackHost`: Kestrel (`CreateEmptyBuilder`, `UseKestrelCore`) on 127.0.0.1 and a free port; `Host` check on every request; the page's files; `/_spar/channel`, `/_spar/send` and `/_spar/csp-report`; a `BridgeSession` per document, closed with its channel. `LoopbackAssets`: page folders mounted at URL prefixes, plain path segments only. |
| `src/Spar.Client/src/loopback.ts` | `loopbackChannel()` and `takeToken()`: the token moved from `#token=` to session storage and out of the address; the channel read line by line; sends one request at a time, batched; a refused or failed request closes the channel. |
| `src/Spar.Testing/SparTestApp.cs` | `SparTestSurface.Browser`, `o.Page(folder, prefix)`, `app.PageUrl(path)`, `app.Origin`, `app.Problems` (what the bridge refused or dropped, and CSP violations). |
| `tests/apps/WireShapes/page/` | A plain ES-module page importing the compiled SDK from `/spar/sdk.js`, with hand-written decoders until the client step exists. `EchoService.WaitAsync` was added to the app for the abort test. |
| `tests/Spar.Tests/LoopbackHostTests.cs` | 28 tests over real HTTP: headers, 404s, path rules, `Host`, token and `Origin` refusals, the channel, batched sends, a closed channel, `ready`, CSP reports, stopping the host. |
| `tests/WireShapes.EndToEndTests/` | 8 Chromium tests (a shared headless browser, a context per test): values, declared errors with and without payloads, text with U+2028, U+2029, an emoji, a TAB and a newline both ways, a `long` as a `bigint`, an abort confirmed as `cancelled` / `stopped`, 200 calls at once in order, a page built for other contracts refused `rejected` / `contract-mismatch` / `notStarted`, the token gone from the address and a reload opening a new document, and another origin kept out. Each test also asserts that the bridge refused or dropped nothing, that no CSP violation was reported, and that the console stayed clean. |

## Results (observed)

| Check | Result |
|---|---|
| `dotnet test --solution Spar.slnx` | 186 of 186: Spar.Tests 123, Spar.Generator.Tests 25, WireShapes.Tests 20, Notebook.Tests 10, WireShapes.EndToEndTests 8 |
| Browser tests, 8 runs | 8 of 8 each time, 3.2–3.4 s for the whole run including Chromium's start (one 5.0 s first run after a build) |
| The ordering rule, broken on purpose | with the compiled SDK changed to send in parallel, the 200-call test failed with a timeout after 10 s (calls dropped by the id rule, never answered); restored, it passes |
| Vitest | 112 of 112 (9 new loopback tests); `npm run check` clean on TypeScript 6.0.3 and 7.0.2 |
| NativeAOT lane | Spar.Tests 123 of 123 (publish 24 s, 20,328,448 B); WireShapes.Tests 20 of 20 (22 s, 20,162,560 B); Notebook.Tests 10 of 10 (21 s, 20,221,952 B); 0 warnings. Before this change: 14 s / 16.45 MB, 13 s / 16.41 MB, 10 s / 16.48 MB |
| Production boundary (scan, not yet a test) | `samples/Notebook` published Release (2,541,056 B): no `LoopbackHost`, `Spar.Loopback`, `Kestrel`, `_spar/channel`, `Microsoft.AspNetCore` or `SparTestSession` as UTF-8, and no `LoopbackHost`, `_spar/channel` or `Kestrel` as UTF-16 |
| Size check | 62 source files, 0 errors, 0 warnings |
| Leftover processes | none after the runs (no test executable, no headless shell) |

## Follow-up: the Browser surface in its own package

After the user chose a separate package (decision "Loopback transport accepted; Browser surface in its own package"), the surface moved to `src/Spar.Testing.Browser`, and `Spar.Testing` no longer references `Spar.Loopback`. Observed: 187 of 187 on CoreCLR (one new test: the Browser surface without its package fails naming it); NativeAOT lane Spar.Tests 123 of 123 (20,328,448 B, Kestrel kept for the loopback host's own tests), WireShapes.Tests 21 of 21 (16,441,856 B), Notebook.Tests 10 of 10 (16,480,768 B). Publish times were 18–25 s under uncontrolled conditions and are not compared.

## Not done or not verified

- **Not run:** Linux, and the CI workflow (no repository yet). The CI file now builds the SDK and installs Playwright's headless shell; both steps are untested.
- **Not built:** the Development-variant loopback (M1d), the CSP and permissions from the app's configuration (M1b), the packed page (the Browser surface serves folders the test names), streams and events over loopback, the bridge limits (message size, calls in flight, a bounded queue: the channel's queue is unbounded), and the production boundary as an automated test.
- **Not measured:** per-test browser cost (only whole runs), and the send latency added by one request at a time under load.
- **Chromium's profile** is Playwright's temporary folder, which Chromium deletes on close; it is not under `artifacts/`.

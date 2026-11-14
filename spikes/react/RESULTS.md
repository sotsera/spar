# React app spike: results

Recorded 2026-11-13 on the development machine: Windows 11, SDK 11.0.100, Node 24.21, npm 11.19, React 19.3.1, Vite 8.3.8, TypeScript 7.0.2, `@vitejs/plugin-react` 6.3.0, Playwright for .NET 1.64.1, xUnit v3 4.0.3 (plain `xunit.v3` package). Built on the D1 pipeline (`../d1-pipeline`) and the loopback host (`../loopback/Loop.Loopback`).

## What was built

| Part | Role |
|---|---|
| `React.App` | Notes backend: `save`, `search` (backend filtering, with an optional delay), `remove`, `count`. In Debug it references the loopback host. Release is NativeAOT win-x64. Launch profiles: `Desktop`; `Loopback` (`--loopback --dev-session obj/spar-dev-session.json`). |
| `React.App/frontend` | A React 19 + TypeScript 7 + Vite 8 app: form, backend search with "latest query wins" (`AbortController` plus a sequence check), remove, count, `ui-ready`, a contract-mismatch listener. Strict `tsconfig` (`noUnusedLocals`, `verbatimModuleSyntax`). |
| `React.targets` | `npm ci` only when `package-lock.json` changes (npm's `node_modules/.package-lock.json` is the output). A junction links `node_modules/@spar` to `obj/…/spar/client/packages/@spar`. `tsc --noEmit` + `vite build` go into `obj/…/spar/frontend-dist`, and only when sources or the generated packages change. The dist folder is packed and embedded. |
| Generator change | The client step now writes npm-shaped, TypeScript-only packages: `@spar/sdk` and `@spar/notes`, each with `package.json` (exports `./index.ts`) and `index.ts`, plus `CallOptions { signal }`. The SDK classifies failures as `NotExecutedError` (with `ContractMismatchError`) or `OutcomeUnknownError`. |
| Loopback host change | `--dev-session <file>`: reuses the session's port (when free) and token across restarts, and writes the current backend address atomically. |
| `vite.config.ts` | Dev server with a `spar-bridge` middleware that forwards `/_spar` to whatever backend the session file currently names, presenting the backend's origin. |
| `React.Tests` | Playwright journeys that launch the real `React.App.exe --loopback`; a caching test; an in-process factory (`InProcessApp`) that runs the app's real entry point inside the test process. |
| `measure-build.sh`, `dev-loop.mjs` | Repeatable build and development-session measurements. |
| `React.Desktop` | Desktop host. A Win32 window and WebView2 run on a dedicated STA thread, using CsWin32 and hand-declared `[GeneratedComInterface]` WebView2 COM interfaces. `WebResourceRequested` serves `https://app.spar.local/`: the embedded frontend, plus the bridge at `/_spar/call/…`, the loopback host's URL shape. A `postMessage` bridge exists only for comparison. `--selftest` (Debug, or Release with `-p:SparSelfTest=true`) drives the page through in-process CDP. `Desktop.targets` links the WebView2 loader statically. |

## Results (observed)

### Build pipeline

The first build ran `npm ci` (skipped, lockfile unchanged), the type-check and `vite build` (bundling 117 ms). Output: 223,488 bytes of JavaScript plus `index.html`, embedded as a 69,979-byte zip.

Incremental behaviour (`measure-build.sh`; times at normal verbosity, steps from a detailed log):

| Change | Time | Contract client | Frontend build | Pack | C# compile |
|---|---:|---|---|---|---|
| No-op | 1.1 s | no | no | no | no |
| C# method body | 2.7 s | yes | **no** | yes (zip unchanged) | yes |
| Frontend component | 3.2 s | **no** | yes | yes | yes |
| New contract | 4.1 s | yes | yes | yes | yes |

**Bug found and fixed:** the pack step wrote the zip only when its bytes changed, so that `CoreCompile` stays skipped, but used the zip as its MSBuild output. The zip could then stay older than its inputs, and packing re-ran on every build. A separate `frontend.pack.stamp` is now the output. This rule must carry into the framework.

### Headless journeys (`dotnet test`, 3 consecutive runs, 3 tests, all passed)

| Browser | App ready | React page ready | Journey: 3 saves, 2 searches, remove, validation | Whole test |
|---|---:|---:|---:|---:|
| `chromium-headless-shell` 156 | ~215 ms | 173–191 ms | 369–395 ms | **1.40–1.45 s** |
| Edge 158 headless | ~205 ms | 177–187 ms | 485–534 ms | **1.90–1.95 s** |

**"Latest search wins" really guards something.** With the hook's abort and sequence check removed (a mutation), the test failed: a slower, older answer overwrote the newer results ("expected count 2"). With the guard restored, it passed.

**The type-check gates the build.** A deliberately unused variable failed the MSBuild build (TS6133), so the old bundle stayed in place.

### Development session (`dev-loop.mjs`, two runs)

The session ran `dotnet watch -lp Loopback --non-interactive`, with Vite started as a sibling and one headless page open throughout.

| Step | Run 1 | Run 2 | Notes |
|---|---:|---:|---|
| `dotnet watch` start → backend ready | 5.5 s | 11.0 s | First build. Run 2 was during the Linux agent's Docker work. |
| React page through Vite → ready | 329 ms | 313 ms | |
| Component edit → visible (React Fast Refresh) | **96 ms** | **82 ms** | The typed search text was **kept**. |
| C# method-body edit → applied | **0.87 s** | **1.10 s** | Hot Reload in place, no restart. Visible in the page; backend state kept. |
| Contract edit → backend restarted and ready | **6.2 s** | **6.6 s** | Same port and token (`session=reused`). The fingerprint changed. |
| The open page after the contract edit | — | — | Vite **hot-updated** the modules importing the regenerated `@spar/notes`. The page's next save returned 200 with matching fingerprints, without a reload. Backend state was reset (in-memory). |
| Reload at the same URL | 126 ms | 137 ms | Works, same token. |

### Production boundary

`dotnet publish -c Release` produced a 1,560,576-byte `React.App.exe` with the React bundle embedded.
- It contains no `LoopbackHost`, `Kestrel`, `AspNetCore` or `_spar/call` strings.
- Run with `--loopback`, it ignores the flag.

### TypeScript-only client

One `index.ts` per package replaced the `.js` + `.d.ts` pair.
- It type-checks under the app's strict settings, and Vite bundles it unchanged.
- Packages still import each other relatively. `../sdk/index.js` maps to `index.ts` under TypeScript's bundler resolution.
- The tests (3/3) and the development session behaved as before.

### What goes into the assembly

The `Spar.Frontend` resource holds only the built `dist` output: `index.html` and the hashed bundle. Vite compiles the generated `@spar` packages into that bundle; they are not embedded as files. The earlier D1 and loopback spikes embedded the raw client files. That was wrong for a bundled frontend.

### No caching of served assets

Every asset response carries `Cache-Control: no-store`. `CachingTests` reloads the page and counts CDP `Network.requestServedFromCache` events: **0**. A rebuilt backend always serves its latest bundle.

### Clone-safe paths

- The tests store the app path **relative to the test output folder**. It is written as assembly metadata at build time and resolved from `AppContext.BaseDirectory`.
- `--dev-session obj/spar-dev-session.json` resolves against the project directory. `dotnet run` from the repository root and from the project folder therefore share one session.
- The `node_modules/@spar` junction has a `.spar-link` marker naming its target. When a clone moves, the junction goes stale. The build sees that the marker doesn't match and recreates the junction.

### In-process test factory (`InProcessApp`)

The app's real entry point runs inside the test process with `--loopback`. The app reads a test hook once at startup. The hook is an `AsyncLocal`, so it reaches only the app started from that call. It lets the test:
- replace services before the container is built (here, a fixed `TimeProvider`);
- receive the address, token and the app's own `IServiceProvider`, instead of parsing a ready line;
- stop the app through a cancellation token.

The hook exists only in the Debug loopback build (`SPAR_LOOPBACK`), next to the loopback host.

Timings over 3 consecutive `dotnet test` runs:

| Start | App ready | Notes |
|---|---:|---|
| Launch `React.App.exe` | ~210 ms | A new process each time. |
| In-process, first in the test process | 124–134 ms | JIT and ASP.NET Core warm-up. |
| In-process, later starts | **20–27 ms** | Two apps started concurrently. |

- **A fake clock reaches the UI.** With `FixedTime(2030-01-02T03:04:05Z)`, the saved note showed `2030-01-02`. The test then read `CreatedAt` directly from the app's `NotesService`.
- **Isolation held.** Two apps started concurrently in one process got different ports and separate containers. A note saved in one was absent from the other.
- **The browser launch dominates.** A whole in-process UI test still took 1.23–1.37 s, against 1.40–1.45 s for the exe journey. Launching Chromium costs about 1 s.
- **Process-wide state leaks.** The app's `SPAR_CALL` console lines appeared in the test run's output. The console, environment variables, the current directory and all static state are shared. Logging must go to a per-app sink, not to `Console`.
- **It is not the shipped binary.** In-process runs the JIT-compiled Debug assembly. Only launching the exe covers AOT behaviour, trimming and the real process lifetime.
- **Referencing an executable from the test project** worked: a plain `ProjectReference` with no extra properties.

### Desktop host (WebView2)

All measured on the AOT exe (`dotnet publish -c Release -p:SparSelfTest=true`) with an offscreen, shown window and the profile under `artifacts/webview2-profiles/`, unless marked JIT. Three background agents were building and testing at the same time, so absolute timings carry some noise.

**One bridge for both hosts.** The SDK's `invoke` now always POSTs `/_spar/call/<contract>/<command>` to its own origin.
- The loopback host checks its bearer token.
- The desktop host answers in-process from `WebResourceRequested` and checks that `Origin` is `https://app.spar.local` instead. The SDK sends `Authorization` only when it has a token.
- The same React bundle ran unchanged in both hosts. The loopback tests passed again (6/6) after the SDK change.

**Startup** (3 runs, warm profile, milliseconds from host start):

| Step | AOT | JIT (Debug) |
|---|---:|---:|
| Process start → host | 45–54 | — |
| Window shown | 10–11 | 26 |
| Environment created | 19–23 | 42 |
| Controller created | 247–306 | 291 |
| Navigation completed | 321–377 | 325–365 |
| UI ready (`/_spar/ready` from React) | **339–396** | 325–388 |

The first run of the production exe on a **fresh profile** needed 1.58 s to complete navigation (one observation).

**Journey.** Through in-process CDP, the test focused the note field, inserted "Buy milk" (`Input.insertText`), clicked Save and waited for the list item. It took 9–19 ms, and the saved note showed its date.

**Caching** (handler hit counts across a `Page.reload`, plus Resource Timing):

| Response header | Reload re-requests `index.html` | Reload re-requests the JS bundle | Resource Timing of the bundle |
|---|---|---|---|
| `Cache-Control: no-store` (5 runs) | yes | **yes** | 223,861 bytes, `deliveryType` "" |
| `max-age=3600` (control, 3 runs) | yes | **no** | 0 bytes, `deliveryType` "cache" |

- The check detects caching, and `no-store` prevents it.
- After an app **restart**, the first load requested every asset again, even with `max-age` (2 runs). Within one session, `no-store` is what matters.
- `favicon.ico` (a 404) was requested once and not again after reload.

**Compressed assets do not work.** A response with `Content-Encoding: br` or `gzip` from `WebResourceRequested` was **not decoded**: the page body showed the compressed bytes and the app never started. The host must serve decompressed bytes.

**Bridge transports** (in-page `performance.now()` at 0.1 ms resolution, calls interleaved, after 10 warm-up calls each):

| Call | fetch, AOT (median / p95 ms) | postMessage, AOT | fetch, JIT | postMessage, JIT |
|---|---:|---:|---:|---:|
| `notes.count`, no body (n=300) | 1.0 / 1.3 | **0.1 / 0.2** | 1.0 / 1.6 | 0.2 / 0.3 |
| `echo`, 1 KB (n=300) | 0.8 / 1.2 | **0.1 / 0.2** | 0.8 / 1.2 | 0.1 / 0.3 |
| `echo`, 64 KB (n=100) | 3.0 / 3.8 | **1.2 / 1.7** | 2.1 / 2.6 | 1.0 / 1.4 |
| `echo`, 1 MB (n=20) | 21.5 / 29.1 | **15.8 / 19.9** | 20.1 / 22.0 | 15.6 / 16.4 |

`postMessage` is 5–10× faster for small calls and 25–60% faster for large ones. Both are far below a frame for typical commands.

**Memory** (AOT, after the journey, 3 runs):
- The app process uses about 28 MB working set and 10.5 MB private memory.
- Including the six `msedgewebview2` processes, the total is 335–341 MB working set (shared pages counted repeatedly) and 117–125 MB private memory.

**Shutdown.**
- The message loop ended 16–22 ms after `WM_CLOSE`.
- The browser process exited 236–284 ms after the close in most of 14 runs; **2 runs took about 6.46 s**.
- After the host process was killed, no WebView2 process remained.

**Sizes** (Release, NativeAOT, win-x64, bytes). The probes are size-only builds selected with `-p:SpikeSizeProbe=…`; the ones marked "not functional" do not run.

| Build | `React.App.exe` | Difference |
|---|---:|---|
| Desktop app (production) | **3,676,160** | |
| … with the self-test compiled in | 3,820,032 | +143,872 |
| … without the zip reader (not functional) | 3,183,104 | the zip reader costs **493,056** |
| … with a Brotli decoder instead of zip (not functional) | 3,356,160 | Brotli costs **173,056** |
| … without the `Process` API | 3,638,784 | `Process` costs 37,376 |
| … without SHA-256 (the fingerprint could be a generated constant) | 3,674,624 | 1,536 |
| … without Microsoft DI (one hand-made instance) | 3,376,128 | DI costs **300,032** with the host |
| No host, dispatch only | 2,611,712 | |
| No host, no DI | 1,736,704 | DI costs **875,008** without the host |

The host, without DI, costs 1,639,424 bytes, of which 493,056 is the zip reader.

**Production boundary.**
- The Release exe built without `SparSelfTest` contains none of `SELFTEST`, `Runtime.evaluate`, `Input.insertText` or `msedgewebview2`; the self-test build contains all of them.
- Note for the boundary test: `LoopbackHost` *does* appear in both. It is a `System.Uri` flag name, so string scans need markers unique to Spar.
- The production exe also started offscreen and served the app.
- Publication had no ILC or trimming warnings.

### Development session with an app-owned dev server (`dev-loop-owned.mjs`)

The only command is `dotnet watch -lp Loopback --property:SparDevServerBuild=true`. The `Loopback` profile adds `--dev-server`, so the backend starts Vite, or reuses a running one, and prints `SPAR_DEV_SERVER url=…`.
- Vite is launched through `cmd start`, so it is not the backend's child and survives the restarts that `dotnet watch` performs.
- Vite records itself in `obj/spar-dev-server.json`. It exits once the backend named in the dev session has been gone for 60 s (15 s in the script).
- The proxy now adds the backend token itself, so the page URL has no token, and it accepts only requests from its own origin.

| Step | Dev build, run 1 | Dev build, run 2 | Full build (`--full`) |
|---|---:|---:|---:|
| Backend starts Vite → `SPAR_DEV_SERVER` | 515 ms | 521 ms | 520 ms |
| Page through Vite → ready | 358 ms | 266 ms | 266 ms |
| Component edit → visible (Fast Refresh, search text kept) | 83 ms | 81 ms | 83 ms |
| C# method body → applied (Hot Reload) | 3.78 s | 3.74 s | 3.93 s |
| Contract edit → backend ready | **5.37 s** | **5.43 s** | 6.44 s |
| Vite after the restart | reused, same pid, 81 ms | reused, 67 ms | reused, 71 ms |
| Save from the already open page after the contract edit | HTTP 200, listed | HTTP 200, listed | HTTP 200, listed |
| Vite gone after `dotnet watch` was stopped | 1.4 s | 1.4 s | 1.4 s |

- **`dotnet watch` does not mark its builds.**
  - In SDK 11.0.100 a watch build sees no `DotNetWatchBuild`, `DOTNET_WATCH` or similar property; a probe target confirmed it, and the strings are absent from the dotnet-watch assemblies. So the development build has to be chosen explicitly.
  - `dotnet watch -p` means `--project`; the property needs `--property:`.
- **Skipping the production bundle saves about 1 s** of a contract restart: 5.4 s against 6.4 s. The rest is the C# build, the generator, the type-check (about 1 s by itself) and the restart.
- **Hot Reload took 3.7–3.9 s here**, against 0.9–1.1 s in the earlier sibling-Vite runs. The cause was not found.
- **Vite stopped with the session.** It exited 1.4 s after `dotnet watch` was killed, well before its own 15 s idle timeout.
  - The cause is in Vite itself: it closes when its **standard input ends** (`process.stdin.on("end")` in `vite/dist/node/chunks/node.js`, unless `CI=true`).
  - Launched through `cmd start /b`, Vite inherits the launcher's standard input. Under `dotnet watch` that stream belongs to the watch session, so Vite survives backend restarts and ends with the session.
  - When the app is run from a pipe (a script or an agent tool), Vite ends with the app.
  - In an interactive terminal it stays until the terminal closes, or until the 60 s idle watchdog fires.
- **Stopping `dotnet watch` through a shell does not reach it.** The earlier script spawned `dotnet watch` with `shell: true` and stopped it with `taskkill /T` on that pid; `dotnet watch`, its `dotnet run` child and the app all kept running and locked the build output. Spawning `dotnet` directly fixed it: `backendAliveAfterWatchKilled` was false in all 3 runs. Agent tooling must own the real process tree.

### npm, the generated packages and a clean clone

**npm deletes the junction's target.** The build linked `node_modules/@spar` to the generated packages with a junction that npm does not know about.
- `npm i` removed it as extraneous and deleted the generated files **through** it.
- The next build failed (TS2307 `Cannot find module '@spar/notes'`): the client step's stamp was still up to date, so nothing regenerated them.
- `npm ci` removes it too.

**Fix tested: the generated packages are ordinary `file:` dependencies.**
- `package.json` lists `"@spar/sdk": "file:../obj/spar/client/packages/@spar/sdk"` and the same for `@spar/notes`. npm creates and records the links itself (`"link": true` in the lockfile).
- The generated files survived `npm i` (2.4 s) and `npm ci` (3.6 s).
- The build's own junction step is off (`SparLinkPackages=false`).

**The generated client must not depend on the configuration.**
- With the links pointing at `obj/Debug/net11.0/win-x64/…`, a clean clone that only ran `dotnet publish -c Release` failed with TS2307: the Debug folder never existed.
- The client now goes to `obj/spar/client/` (`_SparClientDir`, from `BaseIntermediateOutputPath`).

**Clean clone** (sources copied without `bin`, `obj` or `node_modules`; NuGet and npm caches warm):

| Command in the clean copy | Result |
|---|---|
| `dotnet build` (Debug), with the per-configuration client path | succeeded, 25.9 s |
| `dotnet publish -c Release`, with the per-configuration path | **failed**, TS2307 |
| `dotnet publish -c Release`, with `obj/spar/client/` | succeeded, 18.1 s; exe 3,676,160 bytes, the same size as in the working tree |
| then `dotnet build` (Debug) | succeeded, 6.0 s |

**`npm i` against `npm ci`.**
- With the lockfile in sync, `npm i` took 2.3–2.9 s and did not modify `package-lock.json` (2 runs).
- `npm ci` took 3.6–3.8 s, because it always deletes and reinstalls `node_modules`.
- The build keeps `npm ci`, run only when the lockfile is newer than npm's own `node_modules/.package-lock.json`. Developers use `npm i` when they change dependencies.

### Contract snapshot (`<Project>.contracts.snapshot`)

The client step also writes a reviewable snapshot: the fingerprint, the SDK source and each `@spar/<contract>/index.ts`. A build target compares it with the committed file next to the project.
- **`Update`** is the default: it copies the snapshot when it differs and prints `SPAR: contract snapshot updated…`.
- **`Verify`** is the default when `ContinuousIntegrationBuild=true`: it fails with `SPAR100` at the snapshot file.
- **`Off`** disables it.

Results:

| Case | Result |
|---|---|
| No-op build | no message, nothing written |
| `Verify`, unchanged | build succeeded |
| `Verify` after adding `string? Color` to `NewNote` | `error SPAR100` at `React.App.contracts.snapshot` |
| `Update` after the same change | snapshot rewritten; the diff is one line, `readonly color: string \| null;` |
| Change reverted | snapshot rewritten, byte-identical to the original |

The repository's `.gitattributes` forces LF, so the hash is stable across clones.

**Defect found: the fingerprint ignores DTO shapes.** Adding a field to a request DTO left `# fingerprint 3d1faabdfca06185` unchanged, because the manifest lists only type names. A page built against the old shape would not get `409 contract-mismatch`. The fingerprint must cover the full wire shape: properties, types and nullability.

### Debug builds always served by Vite (desktop and loopback)

- **Debug builds embed no production bundle.** `SparDevServerBuild` defaults to true in Debug, and the build embeds a 152-byte placeholder. No property and no special command are needed.
- **The app starts or reuses Vite at runtime:**
  - `--loopback` prints `SPAR_DEV_SERVER url=…` beside `SPAR_READY`.
  - The **desktop host navigates WebView2 to the Vite URL** and filters `WebResourceRequested` only on `<vite origin>/_spar/*`, so bridge calls are still answered in-process (the Origin check uses the Vite origin) and never reach Vite. The `@spar` SDK is unchanged.
- **Release and the tests embed the bundle.** The test project references the app with `AdditionalProperties="SparDevServerBuild=false"`. The tests passed (6/6).

| Desktop, Debug, offscreen self-test (3 runs) | Result |
|---|---:|
| Vite started by the app | 382–459 ms after host start, in parallel with WebView2 creation |
| Navigation completed | 494–628 ms |
| UI ready | **516–661 ms** (AOT Release with the embedded bundle: 339–396 ms) |
| Journey: insert text, click, list item | 16–21 ms |

| `dotnet watch -lp Desktop` (`dev-loop-desktop.mjs`, 1 run) | Result |
|---|---:|
| Start → UI ready | 6.4 s (first build included) |
| Component edit → visible **in WebView2** (Fast Refresh) | **39 ms** |
| Contract edit → app relaunched, UI ready | 6.1 s |
| Vite after the relaunch | reused, 67 ms |

**The variants collide.** The tests' bundle build and the Debug build share `obj/` and `bin/`.
- Every switch recompiles the app: 3.1 s and 3.3 s, against 1.2 s for a no-op.
- `bin/Debug` always holds whichever variant was built last. A test run with `--no-build` after a plain Debug build would launch an exe without the bundle.
- Variants need separate output folders (plan D2 and D3).

### Optional OpenTelemetry layer

**Two parts:**
- **Always on, BCL only (`SparDiagnostics.cs`):** one `Activity` and one `spar.call.duration` sample per bridge command. Without a listener, `StartActivity` returns null.
- **Opt-in at build (`-p:SparTelemetry=true`), `React.Telemetry`:** the OpenTelemetry SDK plus the OTLP exporter, for traces, metrics and logs. It starts only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

Both publish as NativeAOT with **no warnings**.

**Size** (Release, AOT, win-x64, bytes):

| Build | Without outbound HTTPS | With outbound HTTPS (`links.preview` uses `HttpClient`) |
|---|---:|---:|
| App, no instrumentation (earlier baseline) | 3,676,160 | — |
| App with the BCL instrumentation | 3,781,632 (+105,472) | 6,001,152 (HTTPS and TLS: +2.2 MB) |
| + layer, traces only | 8,504,832 | 8,525,824 (**+2.52 MB**) |
| + layer, traces and metrics | 9,029,632 | 9,050,624 (**+3.05 MB**) |
| + layer, traces, metrics and logs | 10,317,824 | 10,337,280 (**+4.34 MB**) |

An app that already calls HTTPS already pays for `HttpClient` and TLS: about 2.2 MB of the first measurement's 6.5 MB. The rest is the OpenTelemetry SDK and exporter (about 2.5 MB), metrics (0.5 MB), and logs through `Microsoft.Extensions.Logging` (1.3 MB).

**Runtime** (AOT, offscreen self-test, 2 rounds):

| Build | UI ready | `fetch` call median | App working set after the run |
|---|---:|---:|---:|
| No layer | 345–367 ms | 0.8–0.9 ms | 64–65 MB |
| Layer present, not configured | 263–283 ms | 0.8 ms | 67–83 MB |
| Layer exporting to an unreachable endpoint | 268–297 ms | 0.8 ms | 83–85 MB |

**Export verified from the AOT exe.** A local OTLP/HTTP receiver got one `POST /v1/traces` (2,201 bytes), one `/v1/metrics` (1,850 bytes) and one `/v1/logs` (2,597 bytes), all `application/x-protobuf`, flushed when the app closed. The run also made a real outbound HTTPS call from the AOT exe, `links.preview` to `https://example.com/`: 188–244 ms the first time, including TLS, and 42–43 ms after that.

### The same app with SQL Server: NativeAOT with warnings accepted, against CoreCLR

**Setup.** `-p:SparSql=true` adds SqlClient 7.1.2 to a Release build and sets `InvariantGlobalization=false`. Each variant ran the offscreen self-test 3 times against a local SQL Server 2022 container (local image, never pulled). Each run did the UI journey, which saves a note to SQL Server, plus an outbound HTTPS call. All 12 runs saved their row: `SELECT COUNT(*)` returned 12.

**Strict AOT fails, as expected.** IL2104 and IL3053 errors come from Microsoft.Data.SqlClient and its logging assembly.

**WebView2 loader under CoreCLR.** `Desktop.targets` now links the loader statically only when `PublishAot=true`. Otherwise it publishes `WebView2Loader.dll`. In the single-file variants the DLL was bundled into the exe and still loaded.

**Gotcha:** passing `-p:PublishAot=true` on the command line fails with NETSDK1207, because the global property reaches the netstandard2.0 generator project. Leave it in the project file.

| Variant | Publish | Files (no pdb) | Size | Process start → host | UI ready | First journey (with SQL) | Working set / private |
|---|---:|---:|---:|---:|---:|---:|---:|
| NativeAOT, warnings accepted | 13.5 s | 2 | **18.6 MB** (18.0 MB exe + 0.56 MB SNI DLL) | **36–64 ms** | 261–370 ms | **25–52 ms** | **37 MB / 14 MB** |
| CoreCLR, framework-dependent (needs the .NET 11 runtime) | 1.7 s | 41 | 7.7 MB | 72–77 ms | 282–318 ms | 133–143 ms | 67 MB / 21 MB |
| CoreCLR, self-contained single file | 4.4 s | 2 | 86.9 MB | 70–74 ms | 280–304 ms | 136–139 ms | 69 MB / 21 MB |
| CoreCLR, self-contained single file + ReadyToRun | 31.9 s | 2 | 106.3 MB | 65–70 ms | 298–357 ms | 57–68 ms | 70 MB / 23 MB |

- **UI ready is about the same everywhere.** WebView2 dominates it.
- **The JIT shows in the first backend work.** The first journey, with its first SQL calls, takes about 135 ms on CoreCLR against 25–52 ms on AOT; ReadyToRun recovers about half of that.
- **CoreCLR uses about twice the memory.**
- **Self-contained CoreCLR is large: 87–106 MB untrimmed.** Trimming it would bring back the same SqlClient trim warnings as AOT; not tested.
- **Outbound HTTPS** took 198–351 ms for the first call in every variant.

## Findings for the plan

1. **The loopback approach works with a real React app.** A full UI journey against the real backend runs in about 1.4 s per test, including app start and browser launch.
2. **Generated client as npm-shaped, TypeScript-only packages.** The app's own `tsc --noEmit` and Vite compile them along with the app; nothing generated is compiled separately.
   - The packages must import each other **relatively**. The bundler resolves the junction to the real `obj/` path, where a bare `@spar/sdk` can't be found. Relative imports avoid `preserveSymlinks` in every app.
3. **A dev session keeps one address.** A session file holds the token and port and survives restarts. Vite in front means the browser URL never changes. Contract changes reach an open page through Vite's hot update; the 409 fingerprint check stays as the safety net when the page isn't served by Vite.
4. **Restart cost.** A contract-changing restart costs about 6 s, mostly the full build, which also produces the frontend bundle that Vite doesn't need in a dev session. A development variant should skip the production frontend build when a dev server serves the frontend.
5. **Keep tool globs out of `node_modules`.** The SDK's default item globs and `dotnet watch` must exclude `frontend/node_modules` and `dist` (`DefaultItemExcludes`, `Watch Remove`). Otherwise every evaluation walks `node_modules`.
6. **The type-check must stay strict and part of the build.** It caught a real problem during the mutation test.
7. **An in-process factory is cheap and useful, but it complements the exe.** It gives service overrides and direct backend inspection, with 20–27 ms starts. It cannot show AOT or process behaviour, and it shares the console and static state with the test process. Launching the exe stays the default for journeys. The factory suits backend-heavy tests and fakes such as the clock.
8. **The real desktop host works with the same bundle and SDK.** In AOT the UI is ready in about 0.4 s, and a journey through in-process CDP takes about 15 ms, so a desktop test level and agent tooling on the real exe are practical.
9. **Bridge transport is a real choice.** One URL-shaped transport (`fetch`) keeps the SDK identical across hosts. `postMessage` is 5–10× faster for small calls but needs its own correlation, error mapping and host-side origin check. Decide in the plan revision.
10. **Embedded frontend format.** WebView2 will not decode compressed responses. The options:
    - zip: +493 KB of code;
    - a Brotli blob decoded at startup: +173 KB of code, with a better ratio;
    - uncompressed resources: no code, but the full asset size.

    Uncompressed is smaller only for small frontends: below roughly 230 KB of raw assets against Brotli.
11. **Microsoft DI is not free in AOT:** 300–875 KB depending on what else is linked. This is relevant to the plan's "MS DI scopes per command".
12. **Shutdown has a long tail.** Browser exit is usually about 0.25 s but sometimes about 6.5 s. Tests and tools must wait for the browser to exit, not for the window, before reusing or deleting a profile.
13. **One command for the development loop is reachable:** `dotnet watch` plus a profile and a property gives backend, dev server, Hot Reload, Fast Refresh and restarts that open pages survive. The property is easy to forget, so a `spar dev` command or the template should supply it, matching the plan's explicit `SparVariant`.
14. **Generated packages are npm `file:` dependencies at a configuration-independent path** (`obj/spar/client/`). A private junction is deleted by npm along with its target, and a per-configuration path breaks clean Release builds.
15. **A committed contract snapshot works as a reviewable lockfile:** Update locally, Verify in CI. It also exposed that the contract fingerprint must hash full DTO shapes.
16. **Debug can always use the dev server.** The decision moves from build time to runtime: Debug embeds no bundle and the app (desktop included) serves the page from Vite with the bridge in-process; Release and test variants embed the bundle. This removes the need to detect `dotnet watch`, gives Fast Refresh inside the real WebView2 (39 ms), and requires per-variant outputs (D2/D3), because test and dev builds otherwise overwrite each other.

## Not tested

- Desktop host:
  - a normal on-screen window (only offscreen was measured; the window code is the same), DPI changes and multiple windows;
  - foreign frames or origins trying to call the bridge;
  - abort propagation to the backend (neither transport tells the host): now covered by an explicit cancel message, see "Cancellation";
  - streaming large uploads, ARM64, and a WebView2 runtime update while running.
- An in-process test factory for the desktop host. It would need an STA thread and a message loop inside the test process.
- Linux: see `../linux-loopback/RESULTS.md`.
- `dotnet run -lp Loopback` without watch: the Vite idle exit was not observed there.
- TypeScript checking of `vite.config.ts`: it was dropped to avoid installing `@types/node`.
- The development build on a non-Windows host (the `cmd start` launch is Windows-only).
- Fresh-clone `npm ci` with a cold npm cache: every clean-clone run used a warm cache.

## Streaming

Recorded 2026-11-13. Desktop: the NativeAOT exe (`dotnet publish -c Release -p:SparSelfTest=true`), offscreen, `React.App.exe --selftest --offscreen --bridge-bench all`, WebView2 Runtime as installed (157 in earlier entries). Loopback: the JIT Debug app (`--loopback`) driven by Playwright with `chromium-headless-shell` 156.0.8078.4, `dotnet test --project spikes/react/React.Tests --filter-class React.Tests.BridgeTests --explicit only`. Raw results: `artifacts/react/bridge-bench.jsonl`; tables made with `node spikes/react/bridge-summary.mjs`. **The two hosts differ in runtime (AOT against JIT Debug) and browser**, so compare transports within a host, not hosts with each other. No other agent's build was running during the final desktop run; the loopback suite ran alone.

### What was built

| Part | Change |
|---|---|
| Generator (`../d1-pipeline/Spike.Generator`) | A command may return `IAsyncEnumerable<T>` (a **stream**). `[SparSubscription(capacity)]` makes it a **subscription**. The binding gets `OpenStream`; the catalog gets `SparCatalog.OpenStream`. The manifest carries `"kind"`, so the fingerprint changes when a command becomes a stream. The TypeScript package exposes `AsyncIterable<T>` for streams and `AsyncIterable<SubscriptionEvent<T>>` for subscriptions, importing only what it uses (`noUnusedLocals`). **The generator was extended; nothing streaming is hand-written.** |
| Generator, outcome safety | Request parse errors are wrapped as `SparInvalidRequestException`, unknown commands throw `SparUnknownCommandException`. Before, any `JsonException` or `InvalidOperationException` thrown *by the handler itself* was reported as "not executed", which is wrong after side effects. |
| Runtime (`../d1-pipeline/Spike.Runtime`) | `SparStream`: a pull stream (the host reads batches as JSON arrays; a batch is flushed early when the source would wait) and a ring-buffer subscription (drop-oldest, counts drops). `SparCall`/`SparCallRegistry`: calls keyed by document and call id, a cancel token, credits, and a short memory of cancels that overtook their call. `SparMessage`: one text protocol for every transport. `SparDispatch`: runs a unary call or pumps a stream and turns every ending into exactly one terminal message. |
| SDK (`../d1-pipeline/Spike.Tool/SparSdk.ts`, embedded) | Moved out of the C# string into a real `.ts` file. Call ids, `CancelledError`, `OutcomeUnknownError.reason` (`lost`, `failed`, `unconfirmed`), `stream`, `subscribe`, `keepSubscribed`, and `configure({ unary, streams, cancelConfirmMs })`. |
| Desktop host | `postMessage` channel (`q` unary, `s` stream, `k` credits, `x` cancel, `h` hello) and a `fetch` stream at `/_spar/stream/…` served by a custom `IStream` (`Interop/PipeStream.cs`). `NavigationStarting` starts a new epoch: every call of the old document is cancelled and its late replies are dropped. |
| Loopback host | `/_spar/call` with call ids and status 499 for "cancelled"; `/_spar/stream/…` (one NDJSON response per call); `/_spar/channel` (one long-lived NDJSON response per document) plus `/_spar/send` (page-to-host messages). A closed connection cancels that document's calls. |
| Measurement | `LabService` (contract `lab`: rows, work, ticks, watch, stats, memory sampler), `frontend/bench.html` + `src/bench.ts` (bundled **only** in Debug and self-test builds), `SelfTest.cs --bridge-bench`, `React.Tests/BridgeTests.cs` (12 functional checks by default, the matrix as explicit tests). |

**Protocol.** One message per `postMessage`, or one per line of a streamed response: a kind letter, the call id, kind-specific fields, then compact JSON (which never contains a raw newline). Host to page: `r id json` (unary result), `i id dropped json-array` (a batch), `e id` (completed), `c id` (cancelled and stopped), `n id code json` (not executed), `u id code json` (outcome unknown), `o doc` (channel open). Page to host: `h doc`, `q id contract command fingerprint json`, `s id contract command fingerprint batch credits json`, `k id credits`, `x id`.

**Back-pressure.** Over a message channel the page grants credits: `window` batches in flight (default 8), refilled in halves as it consumes. Over a per-call `fetch` response the SDK reads the body only when the consumer asks for the next batch, so TCP back-pressure reaches the producer.

### WebView2 cannot stream a `WebResourceRequested` response

The desktop `fetch` stream answers with a custom `IStream` whose `Read` blocks until bytes arrive.

| Observation (AOT) | Result |
|---|---|
| Which thread calls `IStream::Read` | **The UI (STA) thread, 8,664 of 8,664 calls**; 503 of them had to wait for data (3,978 of 4,054 in a delayed-stream probe). A blocking `Read` freezes the message pump. |
| Other calls WebView2 makes | `Seek(0, STREAM_SEEK_SET)`, answered `S_OK` at position 0. No `Stat`. |
| 2,000 rows with a 20 ms pause every 100 rows (≥ 380 ms) | `fetch`: first row at **598.3 ms**, total 599.2 ms: the page sees nothing until the stream ends. `postMessage` channel: first row at **0.4 ms**, total 585.4 ms. |
| `Read` returns `E_PENDING` when no data is buffered (`--istream-pending`) | WebView2 delivered the first batch and then ended the response: the page saw `lost its connection`. |
| A `ticks` stream over `fetch`, aborted after 20 items | The page got its first items only after the 60 s command finished, so the "cancel" arrived after the end (`SPAR_CANCEL … not-running`; the page saw `completed`). |
| Back-pressure | None: WebView2 reads to the end. A 100k-row `fetch` stream raised the backend's working set by 114–177 MB, against 0.1–13 MB over the channel. |

**Consequence: on the desktop host, streams, subscriptions and cancellation must use `postMessage`.** The `fetch` stream route is kept in the spike only as evidence.

### Streams of 100,000 rows (10 columns, about 200 bytes of JSON each)

Medians of 3 runs, range in brackets. "Page task" is CDP `Performance.getMetrics` `TaskDuration`, the main thread's busy time during the run.

| Host | Transport | Batch | First row ms | Total ms | Rows/s | Page task ms | Backend peak working set Δ MB | Backend CPU ms |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| desktop (AOT) | fetch (fully buffered) | 1 | 225 (213–231) | 338 (334–374) | 296k | 148 | 168 (146–177) | 234 |
| desktop (AOT) | fetch (fully buffered) | 100 | 194 (184–245) | 221 (210–275) | 452k | 45 | 140 (120–159) | 156 |
| desktop (AOT) | fetch (fully buffered) | 1000 | 237 (230–256) | 276 (255–279) | 362k | 63 | 146 (114–161) | 219 |
| desktop (AOT) | channel | 1 | 0.3 (0.3–0.5) | 3,572 (1,908–7,380) | 28k | 1,369 (832–2,253) | 5.4 | 3,781 |
| desktop (AOT) | channel | 100 | **0.5** (0.4–0.6) | **163** (156–163) | **614k** | 85 | 4.1 | 234 |
| desktop (AOT) | channel | 1000 | 2.8 (2.5–2.8) | **160** (157–174) | **626k** | 72 | 6.0 | 219 |
| loopback (JIT) | fetch | 1 | 15 (14.8–15.5) | 733 (707–738) | 136k | 533 | 19.4 | 1,500 |
| loopback (JIT) | fetch | 100 | 17.6 (15.3–17.8) | 290 (281–298) | 345k | 180 | 17.3 | 391 |
| loopback (JIT) | fetch | 1000 | 26.1 (18.8–37.6) | 327 (319–330) | 306k | 112 | 18.2 | 344 |
| loopback (JIT) | channel | 1 | 19.4 (18.3–19.9) | 15,525 (11,586–15,634) | 6k | 11,100 | 24.1 | 6,484 |
| loopback (JIT) | channel | 100 | 12.1 (11.6–16.2) | 173 (159–208) | 578k | 134 | 17.4 | 234 |
| loopback (JIT) | channel | 1000 | 15.3 (12.3–15.7) | **118** (110–123) | **850k** | 74 | 17.8 | 125 |

- **Main thread:** no long task (over 50 ms) in any run, on either host. The longest gap between animation frames was 16.7–17.6 ms everywhere except batch 1 over a channel (23.5 ms desktop, 50 ms loopback).
- **Batch 1 is a trap.** It costs about 36 µs per message on the desktop and 155 µs on loopback, where every 4th batch also costs a credit POST. Batches of 100–1,000 rows are 20–130× faster. **The host should size batches by bytes or time** (for example 64 KB or 5 ms), not by rows.
- **Page memory.** The loop that counts rows and drops them ended with a CDP heap of 2–10 MB. A grid that **keeps all 100,000 rows** (desktop, channel, batch 1000) took 243 ms, raised the page heap by **38.6 MB** at peak and left **36.1 MB** in use. `performance.memory` stays at 0 in `chromium-headless-shell`, so loopback heap figures come only from CDP.
- **Backend memory** on loopback is dominated by the JIT Debug app's baseline (17–24 MB working set increase in every run); compare runs on the same host only.

### Back-pressure: the consumer reads 5,000 rows of a 1,000,000-row stream, then stops for 2 s

| Host | Transport | Batch | Produced by the backend | Ahead of the consumer | Producer stalled (1 s → 2 s) | Cancel → confirmed (page) |
|---|---|---:|---:|---:|---|---:|
| desktop | channel | 1 | 5,008 | 8 | yes | 2.7 ms |
| desktop | channel | 100 | 5,600 | 600 | yes | 0.4 ms |
| desktop | channel | 1000 | 12,000 | 7,000 | yes | 0.6 ms |
| loopback | fetch | 1 | 17,090 | 12,090 | yes | 8.8 ms |
| loopback | fetch | 100 | 16,600 | 11,600 | yes | 3.1 ms |
| loopback | fetch | 1000 | 19,000 | 14,000 | yes | 3.2 ms |
| loopback | channel | 1 | 5,008 | 8 | yes | 0.9 ms |
| loopback | channel | 100 | 5,600 | 600 | yes | 0.8 ms |
| loopback | channel | 1000 | 12,000 | 7,000 | yes | 0.7 ms |

Credits bound what is in flight exactly (window × batch, minus what has been granted back). TCP bounds it at about 2.5–3 MB of rows (12,000–14,000), independent of the batch size.

### Failures found and fixed

1. **Cancelling a Kestrel flush aborts the connection.** The loopback pump passed the call's token to `PipeWriter.FlushAsync`. When the page cancelled a stream while the host was blocked by back-pressure, Kestrel aborted the connection, so the confirmation could never arrive and the page saw `lost` (3 of 3 runs at batch 1; the host logged `SPAR_CANCEL` and then `SPAR_CONNECTION_LOST` 6 ms later). **Rule: transport flushes never take the command's token.** A page that cancels keeps reading, so the flush completes; a page that is gone aborts the request anyway. Disabling `MinResponseDataRate` did not help and is kept only as a loopback setting.
2. **Generated outcome misclassification** (above): handler exceptions of type `JsonException` or `InvalidOperationException` were reported as "not executed".
3. **Test harness:** Playwright's `WaitForFunctionAsync` with an `async` predicate treats the returned promise as truthy, so two waits passed vacuously in the first loopback run (the restart measurement showed 0 events). `BridgeTests.WaitForAsync` now polls an awaited boolean; both affected checks were run again.

### Sizes

- The production exe (`dotnet publish -c Release`, strict NativeAOT) is **6,233,600 bytes** with no ILC or trim warnings, against 6,001,152 for the last comparable build (BCL instrumentation and `HttpClient`): **+232,448 bytes**, which includes the runtime above and the spike's `lab` contract.
- The production bundle has `index.html` and one 226,909-byte script (it was 223,488): the SDK grew by about 3.4 KB. **It contains no `bench.html` and no `sparBench`**; the exe has no `SELFTEST` or `Runtime.evaluate`. The `lab` contract itself *is* in the production exe (a spike shortcut, like `echo`).

### Transport recommendation

**One message protocol, carried by the host's cheapest channel, with unary calls on `fetch` only on loopback:**

| | Desktop host | Loopback host |
|---|---|---|
| Unary calls | `postMessage` (0.2 ms median, p95 0.5 ms, against `fetch` 1.3 / 6.3 ms; 64 KB: 1.4 against 3.2 ms) | `fetch` POST (0.7 ms median, p95 1.0 ms) |
| Streams, subscriptions | `postMessage` with credits (the only working option: `WebResourceRequested` buffers to the end and reads on the UI thread) | **The HTTP channel**: one streamed response per document down, POSTs up, credits |
| Cancel | `x id` over `postMessage` | `x id` POSTed to `/_spar/send` |

Why the channel on loopback, although per-call `fetch` streams are simpler and similar in speed (batch 100: 290 against 173 ms):
- **Browsers allow 6 HTTP/1.1 connections per host, and a loopback page cannot use HTTP/2** (no TLS). With **six open `fetch` subscriptions a unary call was blocked for over 3 s**, and all six cancels went **unconfirmed**, because the cancel POST could not get a connection either. With the channel the same unary call took 5.7 ms and all six subscriptions were confirmed cancelled.
- A per-call response hides subscription overflow in OS and browser buffers: a slow consumer saw **no gaps but latency up to 6 s** (p50 2.97 s), so the bounded buffer never acted.
- The channel gives both hosts the same semantics: credits, gap markers, one document scope.

Keep `fetch` for loopback unary calls (they are request-shaped, and the browser's devtools show them), but send their cancellation over the channel uplink. Size batches by bytes. Make the credit window configurable per stream (8 × 1,000 rows kept 7,000 rows in flight).

### Observed, skipped, not tested

- **Observed:** everything in the tables above; 18 of 18 default `React.Tests` passed (6 explicit skipped), the loopback spike's own 4 tests passed, `../d1-pipeline/Spike.App` still builds, and the strict production publish has no warnings.
- **Skipped:** desktop `fetch` streams in the back-pressure, cancellation and push matrices (they cannot work, see above); `--istream-pending` was run once.
- **Not tested:** the development session through Vite's proxy (`/_spar/stream` and `/_spar/channel` pass through its `pipe`, but nothing was measured); a desktop JIT build for an AOT-to-AOT comparison with loopback; several pages on one loopback backend; ARM64; WebView2's `CoreWebView2SharedBuffer` or `PostWebMessageAsJson` as alternatives to strings; uploads (page-to-host streams); `IAsyncEnumerable` element types other than records and `long`.

## Cancellation

### Semantics (implemented and verified)

- Every call has an id. Aborting the signal sends `x id` and **keeps waiting for the backend's answer**; the fetch or the stream itself is never aborted.
- The page learns:
  - **`CancelledError`** only when the backend confirms the command **ended by observing its cancelled token**. For a stream, the confirmation is sent only after the iterator has been disposed (its `finally` blocks ran).
  - **The result** if the command completed anyway. The SDK does not turn a real success into an error.
  - **`NotExecutedError("cancelled")`** when the signal was aborted before the call was sent. **`cancelled-before-start`** when the cancel overtook its call on another connection: the host remembers the cancel for 30 s, and the call never runs.
  - **`OutcomeUnknownError` (`unconfirmed`)** when no answer arrives within `cancelConfirmMs` (default 5 s), and **`lost`** when the connection drops. The late answer is dropped. **Nothing is ever retried.**
- An `OperationCanceledException` from another token (an internal timeout, for instance) is **not** a confirmation: it is reported as `failed`, which is outcome unknown.
- Leaving a `for await` loop (`break`) cancels too, and waits for confirmation. If none comes, the `break` itself throws `OutcomeUnknownError`.
- A navigation (desktop `NavigationStarting`), a closed channel or request (loopback), or host shutdown cancels the document's calls, and their replies are dropped.

### Verified with work that would otherwise keep going

`lab.work` and `lab.ticks` increment a shared counter every tick. After the outcome, the page reads the counter, waits 300 ms and reads it again; `workRunning` is the backend's started minus stopped count.

| Host | Command | Transport | How | Honors the token | n | Outcome on the page | Page: abort → settled (median, range) | Counter moved after the outcome | Work still running |
|---|---|---|---|---|---:|---|---:|---:|---:|
| desktop | unary | fetch | abort | yes | 5 | cancelled | 0.8 ms (0.7–1.9) | 0 | 0 |
| desktop | unary | postMessage | abort | yes | 5 | cancelled | **0.3 ms** (0.2–0.7) | 0 | 0 |
| desktop | stream | channel | abort | yes | 5 | cancelled | 0.3 ms (0.2–0.5) | 0 | 0 |
| desktop | stream | channel | `break` | yes | 5 | stopped | 0.2 ms (0.2–0.5) | 0 | 0 |
| loopback | unary | fetch | abort | yes | 5 | cancelled | 1.1 ms (1.0–4.6) | 0 | 0 |
| loopback | stream | fetch | abort | yes | 5 | cancelled | 1.0 ms (0.9–2.2) | 0 | 0 |
| loopback | stream | channel | abort | yes | 5 | cancelled | 0.8 ms (0.7–1.2) | 0 | 0 |
| loopback | stream | channel | `break` | yes | 5 | stopped | 0.8 ms (0.7–0.9) | 0 | 0 |
| both | unary | message / fetch | abort | **no** (2.5 s of work) | 1 each | **unknown: unconfirmed** after 1,005 / 1,013 ms (`cancelConfirmMs` 1000) | | **20** (it kept running) | 1 |
| both | stream, 3 s between items | channel | abort | **no** | 1 each | **unknown: unconfirmed** after 1,001 / 1,010 ms | | 0 within 300 ms (next tick due later) | 1 |
| both | stream, 1 ms between items | channel | abort | **no** | 1 each | **cancelled** after 16.9 / 16 ms | | 0 | 0 at the next read |

- **Backend side**, cancel received → terminal message sent: **0.03–0.07 ms** on the desktop (`SPAR_STREAM … cancel->Cancelled`). On loopback it was 3.8–4.1 ms in the back-pressure runs, where blocked writes drain first; the other loopback runs did not record it.
- **A pull stream is stoppable at every `yield`, even when its code ignores the token:** the host stops pulling and disposes the iterator at its next yield (16–17 ms here, one timer tick). Only a long stretch without a yield or an await on the token stays unconfirmed.
- **The page is never told "stopped" while work runs:** in every unconfirmed case the counter was still moving, or work was still registered as running.

### Not tested (cancellation)

- Commands that start background work they do not await: the host cannot see it, so "cancelled and stopped" covers only the command's own code. That limit belongs in the contract documentation.
- A cancel racing a completion at the same instant (both outcomes are handled; the race was not forced).
- `cancelled-before-start` was implemented but not provoked by a test.
- Cancelling during a navigation on the desktop (`NavigationStarting` cancels; only the subscription reload case below was measured).

## Push

### Design

- **A subscription is a streaming command marked `[SparSubscription(capacity)]`** that returns `IAsyncEnumerable<T>`. The client is generated as `AsyncIterable<SubscriptionEvent<T>>`, with events `{ type: "event", value }`, `{ type: "gap", dropped }` and `{ type: "reset" }`.
- **It never blocks its producer.** A pump reads the source into a ring buffer of `capacity` items. When the page falls behind, the oldest item is dropped and counted. The next batch carries the count, and the page sees a `gap` before the newer events. Delivery uses the same credits as streams, so a stalled page does not flood the WebView2 IPC queue or the channel.
- **Document scope:** a reload ends the old document's subscriptions (desktop `NavigationStarting`, loopback channel closed). The app subscribes again on mount.
- **Re-subscription after a lost connection is opt-in per subscription:** `keepSubscribed(() => lab.watch(…), { signal })`. It retries every 250 ms (100 ms in the test) on `lost` or `unavailable` only, and yields `reset` before the first event of the new subscription, telling the app that events may be missing and to refetch state. The SDK never re-subscribes by itself, so "never replay a command automatically" holds. Whether an explicit opt-in is acceptable for read-only subscriptions is **a decision for the user**.

### Rate and latency (5 s per run, 1 run per row)

Latency is the page's `performance.timeOrigin + performance.now()` minus the backend's wall-clock stamp at production (same machine, both from the system clock). The producer emits every event that is due on each timer tick, so events leave in small bursts.

| Host | Transport | Rate/s | Delivered | Gaps | p50 ms | p95 ms | p99 ms | max ms | Page main-thread busy | Backend CPU | Backend working set Δ |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| desktop (AOT) | channel | 1,000 | 4,993 | 0 | 0.19 | 0.36 | 0.52 | 0.60 | 57 ms / 5 s (1.1%) | ≈0 ms* | 0 MB |
| desktop (AOT) | channel | 5,000 | 24,962 | 0 | 0.28 | 0.44 | 0.53 | 0.78 | 71 ms (1.4%) | 31 ms | 4.8 MB |
| desktop (AOT) | channel | 20,000 | 99,784 | 0 | 0.38 | 0.64 | **0.84** | 1.13 | 139 ms (**2.8%**) | 391 ms (7.8% of one core) | 8.8 MB |
| loopback (JIT) | channel | 1,000 | 4,984 | 0 | 0.42 | 0.65 | 0.86 | 1.76 | 107 ms (2.1%) | 797 ms | 7.4 MB |
| loopback (JIT) | channel | 5,000 | 24,952 | 0 | 0.48 | 0.72 | 1.01 | 1.82 | 131 ms (2.6%) | 859 ms | 9.0 MB |
| loopback (JIT) | channel | 20,000 | 99,923 | 0 | 0.56 | 0.84 | 1.00 | 1.82 | 266 ms (5.3%) | 1,344 ms | 13.9 MB |
| loopback (JIT) | fetch | 20,000 | 99,930 | 0 | 0.39 | 0.62 | 0.73 | 2.58 | 144 ms (2.8%) | 891 ms | 11 MB |

\* `Process.TotalProcessorTime` advances in about 15.6 ms steps; below that it reads 0.

- **1,000 events/s is sustained with large headroom on both hosts.** 20,000/s was delivered completely with p99 under 1 ms and the page main thread at most 5% busy. No long tasks, and the longest frame gap was 16.8 ms. A higher rate was not tried.
- Loopback's backend CPU includes the JIT Debug app and Kestrel; the desktop figures are the AOT exe.
- The event count is 0.2–0.4% below rate × 5 s because the run ends at a fixed time.

### Overflow (2,000 events/s for 3 s; the page sleeps 200 ms every 100 events, so it consumes about 500/s)

| Host | Transport | Delivered | Gaps (dropped) | Every event accounted for | Out of order | p50 / max latency |
|---|---|---:|---:|---|---:|---:|
| desktop | channel | 3,122 | 4 (2,854) | yes (delivered + dropped = produced) | 0 | 1.74 s / 3.37 s |
| loopback | channel | 3,119 | 4 (2,857) | yes | 0 | 1.69 s / 3.39 s |
| loopback | fetch (per-call response; functional run, 2 s) | 3,979 | **0** | yes | 0 | 2.97 s / **6.09 s** |

- With the channel, the bounded buffer acts and **the page sees each gap**. The remaining latency comes from the buffer's size: 1,000 items plus up to 8 × 100 in flight, at 500/s. **A live-monitoring subscription wants a small capacity and window** (fresh data and more gaps); a log tail wants a large one. Capacity is a per-command attribute today; a per-call option would serve both.
- Over a per-call `fetch` response the OS and browser buffers hold about 6 s of events, so the ring never fills and no gap appears: **stale data without a signal**. This is the second reason for the channel on loopback.

### Reload and backend restart

| Case | Result |
|---|---|
| Desktop: a subscription, then `Page.reload` | The old subscription had ended when the new page was ready (`activeWatches` 1 → **0** → 1). Reload → page ready 114 ms; reload → first events of the new subscription 231 ms. |
| Loopback: the same (default test `A_reload_ends_the_old_documents_subscription`) | The old subscription ends when the channel connection closes; `activeWatches` back to 1. Passed. |
| Loopback development session: kill the app, start it again with the same `--dev-session` (same port and token), with `keepSubscribed` (3 runs) | The backend was ready **161–168 ms** after the kill. The page resumed with exactly **one `reset`** and no error; the first 10 events of the new subscription arrived **389–394 ms** after the kill (100 ms retry plus 100 events/s). The event sequence restarted at 0: backend state is not kept, which is what `reset` tells the app. |
| Desktop backend restart | Not applicable: the page and the backend are one process, and a relaunch is a new page that subscribes on mount. |

### Not tested (push)

- Rates above 20,000/s, many concurrent subscriptions on one channel, and fairness between a fast stream and a subscription sharing it (head-of-line blocking on the loopback channel's single connection).
- Restart through the Vite proxy (`dotnet watch` session): the proxy returns 503 while the backend is down, which the SDK maps to `unavailable`. That mapping is untested.
- Battery or CPU over long runs; the page in a background tab (timers throttled).
- Event payloads larger than about 100 bytes.

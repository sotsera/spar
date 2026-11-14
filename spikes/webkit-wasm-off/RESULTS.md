# WebAssembly off by default on the Linux host: spike results

Recorded 2026-11-14 on the development machine: Windows 11 (build 26320), WSL 2 Ubuntu 24.04.5 (kernel 6.18.58.1) with WebKitGTK **2.52.6** (`libwebkitgtk-6.0-4 2.52.6-0ubuntu0.24.04.1`), GTK 4.14.5, bubblewrap 0.9.0, display through WSLg (the web view is never presented, so no window appears). Comparison runs: Docker image `spar-webkit-wasm-off:fedora43` (Fedora 43, WebKitGTK **2.54.2**, GTK 4.20.4, bubblewrap 0.12.0, Xvfb). .NET check: the existing image `spar-linux-desktop:webkit2.52` with SDK 11.0.100.

**Question.** Spar's base CSP has no `'wasm-unsafe-eval'`, which blocks WebAssembly on WebView2. `spikes/csp-navigation` saw `WebAssembly.compile` succeed on WebKitGTK under the same policy. The decision is: WebAssembly blocked by default, with a C# opt-in, and the Linux host should match Windows. What does WebKitGTK actually block, and what can the Linux host do to switch WebAssembly off?

**Short answer.**

- WebKitGTK does enforce `'wasm-unsafe-eval'`, but in 2.52 only when a module is **instantiated**. `validate`, `compile`, `new WebAssembly.Module` and `compileStreaming` succeed under the base CSP; every instantiate path fails with a `CompileError`. That is why `csp-navigation` saw `compile` "run". WebKit fixed this upstream on 2026-05-28 (commit `4003087f20`); 2.54.2 also blocks compile, `Module` and the streaming calls (observed). `validate` still runs on both.
- On both versions, **a worker script served without a CSP header runs WebAssembly (and `eval`) freely**, even when the page has the base CSP. A worker takes its policy from its own response.
- **`JSC_useWasm=false`** in the UI process environment reaches the web process (seen in `/proc/<pid>/environ`) and removes the `WebAssembly` global from the page, every frame and every worker, with or without a CSP. `typeof WebAssembly` is `"undefined"`; using it throws `ReferenceError`. No effect was measured on ordinary JS speed or page start. Observed on 2.52.6 and 2.54.2. The suggested name `JSC_useWebAssembly` is **wrong** for these versions: it was renamed to `useWasm` between 2.44 and 2.48, and the old name is only logged as `ERROR: invalid option` and otherwise ignored.
- .NET's `Environment.SetEnvironmentVariable` does **not** reach native `getenv` on Linux (observed). The host must call libc `setenv` before WebKit starts its processes.
- A web process extension that calls `jsc_options_set_boolean("useWasm", FALSE)` works too, but only if the extensions directory is set **before** the first load. Set inside the `initialize-web-process-extensions` signal, as the API docs suggest, the extension is silently never loaded under the sandbox.
- A document-start user script that deletes or locks `WebAssembly` does not reach workers, nor the `about:blank` document left behind when CSP blocks a frame. WebKitGTK has no `WebKitSettings` property or feature flag for WebAssembly.

**Recommendation:** by default, set `JSC_useWasm=false` with libc `setenv` and keep the CSP without `'wasm-unsafe-eval'`. Send the CSP on worker scripts as well. The C# opt-in sets `JSC_useWasm=true` and adds `'wasm-unsafe-eval'`. Details under "Recommendation".

## Layout

| Path | Contents |
|---|---|
| `pages/` | `probe.html`, `suite.js` (every compile and instantiate path, run against the `WebAssembly` of any realm), `probe.js` (page, workers, a Module posted to workers, frames, recovery tricks, a JS workload), `worker.js`, `frame.html`. No timers: waits end on an event or when the host calls `window.__hostTick()`. |
| `linux/probe_host.py` | Minimal WebKitGTK 6.0 host (Python GI): `spar://app` custom scheme (secure, CORS-enabled) or a loopback HTTP server. CSP profiles, `--setenv`, `--jsc-ui`, `--user-script delete|lock`, `--ext-dir/--ext-mode/--ext-early`, `--dump-features`. Records the `JSC_*` entries of every WebKit child process's `/proc/<pid>/environ`. |
| `linux/ext/wasm_off_ext.c` | Web process extension: `jsc_options_set_boolean("useWasm", FALSE)` when given `"off"`; exposes what it saw as `window.__sparExt`. |
| `linux/run.sh` | WSL runner (`features`, `matrix`, `timing [n]`, `one <label> <args>`). Private `HOME`/`XDG_*`/`XDG_RUNTIME_DIR` under `~/spar-spikes/webkit-wasm-off`; ephemeral network session; inherited `JSC_*` cleared. `linux/ext-run.sh` and `linux/ext-debug.sh`: helpers used while diagnosing the extension. `linux/env-check.sh`: version listing. |
| `docker/` | `Dockerfile` (Fedora 43 + WebKitGTK 2.54.2), `in-container.sh` (the subset run there). Run with `tools/fedora.sh`. |
| `dotnet-env/EnvCheck.cs`, `tools/dotnet-env.sh` | Does `Environment.SetEnvironmentVariable` reach native `getenv`? |
| `tools/summarize.mjs`, `tools/timing.mjs`, `tools/source-excerpts.sh` | Tables from the raw runs; the cited WebKit source lines. |
| `results/` | `runs/*.json` and `logs/*.log` (2.52.6), `fedora-2.54/` (2.54.2), `summary.md` per folder (generated tables), `features.json`, `timing.json`, `dotnet-env.txt`, `source-excerpts.txt`, `wsl-versions.txt` (`fedora-2.54/versions.txt` for the container). |

Commands (repository root, Windows):

```
wsl -d Ubuntu-24.04 --exec bash spikes/webkit-wasm-off/linux/run.sh features
wsl -d Ubuntu-24.04 --exec bash spikes/webkit-wasm-off/linux/run.sh matrix
wsl -d Ubuntu-24.04 --exec bash spikes/webkit-wasm-off/linux/run.sh timing 7
cd spikes/webkit-wasm-off; docker build -t spar-webkit-wasm-off:fedora43 docker/; bash tools/fedora.sh
bash tools/dotnet-env.sh > results/dotnet-env.txt; bash tools/source-excerpts.sh
node tools/summarize.mjs; node tools/summarize.mjs results/fedora-2.54; node tools/timing.mjs
```

## Method

- **Policy.** `base` is the plan 3b policy (`default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; worker-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'; report-uri /_spar/csp-report`). `connect-src 'self'` replaces the Windows origin `https://app.localhost`, as in `csp-navigation`. Variants: `none`, `wasm-unsafe-eval` and `unsafe-eval` (added to `script-src`). The policy is sent as a header on every response, with `Content-Type` repeated in the headers (the `csp-navigation` `nosniff` finding). The exception is `worker.js?csp=0`, which is sent without CSP.
- **Module.** A 41-byte module exporting `add(i32, i32)`. Each check records the value (`add(2,3) = 5`) or the exact error.
- **Realms.** The top page; a dedicated worker whose script has the CSP header and one whose script has none; a Module compiled in the page and posted to each worker; dynamically created `about:blank` and `srcdoc` iframes; an iframe `src="frame.html"` (blocked by `frame-src` under `base`); a `blob:` worker; `window.open`; dynamic `import("./add.wasm")`.
- **Environment.** `--setenv` sets the variable in the UI process (`os.environ`, which calls `putenv`) before GI loads WebKit. The host reads `/proc/<pid>/environ` of the `WebKitWebProcess` and `WebKitNetworkProcess` descendants (through bubblewrap) once the page starts.
- **Source.** WebKit at tag `webkitgtk-2.52.6` (and `2.54.0` and `2.44.0` for comparison), downloaded file by file to `artifacts/webkit-wasm-off/src/` (1.6 MB, disposable). Cited lines: `results/source-excerpts.txt`.

## Q1. What the CSP blocks on WebKitGTK

2.52.6, `spar://app` (the HTTP loopback origin gave identical rows, `csp-base-http`):

| Check | `none` | `base` (page, frames, worker with CSP) | `base`, worker script without CSP | `wasm-unsafe-eval` | `unsafe-eval` |
|---|---|---|---|---|---|
| `typeof WebAssembly` | object | object | object | object | object |
| `WebAssembly.validate` | true | **true** | true | true | true |
| `WebAssembly.compile` | Module | **Module** | Module | Module | Module |
| `new WebAssembly.Module` (sync) | Module | **Module** | Module | Module | Module |
| `compileStreaming(fetch)` | Module | **Module** | Module | Module | Module |
| `new WebAssembly.Instance` (sync) | 5 | CompileError | **5** | 5 | 5 |
| `instantiate(bytes)` | 5 | CompileError | **5** | 5 | 5 |
| `instantiate(module)` | 5 | CompileError | **5** | 5 | 5 |
| `instantiateStreaming(fetch)` | 5 | CompileError | **5** | 5 | 5 |
| `new WebAssembly.Memory` | ok | ok | ok | ok | ok |
| `eval`, `new Function` | ran | EvalError | **ran** | EvalError | ran |

- Error text (page and workers): `CompileError: Refused to create a WebAssembly object because 'unsafe-eval' or 'wasm-unsafe-eval' is not an allowed source of script in the following Content Security Policy directive: "script-src 'self'".`
- **WebKitGTK honours `'wasm-unsafe-eval'`.** Adding it allows instantiation and keeps `eval` blocked. `'unsafe-eval'` allows both, as the CSP spec says. Without either, only instantiation is refused in 2.52.6.
- **Source (2.52.6).** WebCore turns WebAssembly off together with eval when `script-src` lacks both keywords (`ContentSecurityPolicy.cpp:180,325`, message from `ContentSecurityPolicyDirectiveList.cpp:168`). JSC checks `webAssemblyEnabled()` only in `JSWebAssemblyInstance.cpp:318` (instance creation); `JSWebAssembly.cpp` has no check. Upstream commit `4003087f20` ("CSP wasm-unsafe-eval directive is not enforced during WebAssembly byte compilation", 2026-05-28, bug 315489) added checks to compile, `Module`, `compileStreaming` and `instantiateStreaming`; they are present at tags `webkitgtk-2.54.0` to `2.54.2` and absent at 2.52.6. There is no 2.52.7 tag.
- **2.54.2 (Fedora container, `results/fedora-2.54/`):** under `base`, `compile`, `new Module`, `compileStreaming` and every instantiate path fail with the same `CompileError`, in the page, in the frames and in the worker with the CSP header. The page therefore had no Module to post to a worker. `validate` still returns `true`. The worker without CSP still runs everything. `none`, `wasm-unsafe-eval` and `unsafe-eval` behaved as in 2.52.6.
- **Workers take their policy from their own response.** `worker.js?csp=0`, served without a header, ran every WebAssembly path and `eval` while the page had `base`. A Module compiled in the page (2.52.6) and posted to that worker was instantiated there (`5`); the worker with the header refused it. This is CSP's normal rule for network-loaded workers, so it should hold on every engine; only WebKitGTK was run here. `blob:` workers were blocked by `worker-src 'self'`.
- **Frames inherit the page policy.** `about:blank`, `srcdoc` and the about:blank left by the blocked `frame.html` all refused instantiation (and, on 2.54.2, compile).
- **No violation event or report for WebAssembly.** In every `base` run the host received exactly 4 reports (eval, `new Function`, `blob:` worker, `frame-src`). The WebAssembly refusals produced only the exception. (One run lists 3 page events against 4 reports: the `frame-src` event arrived after the page had posted its result. That is a probe timing artifact.)
- **WebAssembly ES module integration is off.** `import("./add.wasm")` failed: `TypeError: 'application/wasm' is not a valid JavaScript MIME type.` The `WebAssemblyESMIntegration` feature is `testable`, default `false`.

## Q2. `JSC_*` options in the UI process environment

**Option names.** In 2.52.6 `OptionsList.h:546` declares `useWasm` (default `canUseWasm()`, availability `Normal`, "Expose the Wasm global object."). Related options: `useWasmIPInt` (632), `useBBQJIT` (571), `useOMGJIT` (572), `failToCompileWasmCode` (548), `useJIT` (87). Up to 2.44.0 the option was `useWebAssembly` (`OptionsList.h:496` at that tag); 2.48.0 already has `useWasm`. `Options.cpp:1049–1068` reads every `JSC_<name>` variable from `environ` on Linux in release builds. An unknown name logs `ERROR: invalid option` and crashes only when `validateOptions` is set (off by default). `Normal` options are not gated by the restricted-options switch. `BubblewrapLauncher.cpp` never clears the environment.

| Run (2.52.6 unless noted) | Reached web process? | Result |
|---|---|---|
| `JSC_useWasm=false`, profile `none` | yes (`WebKitWebProcess` and `WebKitNetworkProcess` both list it) | `WebAssembly` **undefined** in page, both workers, all three frames. `WebAssembly.compile(...)` → `ReferenceError: Can't find variable: WebAssembly`. `'WebAssembly' in globalThis` is false. |
| `JSC_useWasm=false`, `base` | yes | same; CSP otherwise unchanged (eval still blocked, same 4 reports) |
| `JSC_useWasm=false`, `wasm-unsafe-eval` | yes | same: the environment wins over the CSP keyword |
| `JSC_useWasm=false`, `base`, **2.54.2** | yes | same as 2.52.6 |
| `JSC_useWebAssembly=false` | yes | **no effect**; `ERROR: invalid option: JSC_useWebAssembly=false` logged 3 times (UI, network and web process) |
| `JSC_failToCompileWasmCode=true` | yes | no effect on this module: everything still ran (the module is probably run by the IPInt interpreter, which this option does not stop) |
| `JSC_useWasmIPInt=false` + `JSC_useBBQJIT=false` | — | host aborted (exit 134): `INCOHERENT OPTIONS: at least one of useWasmIPInt, or useBBQJIT must be true`. The UI process reads `JSC_*` too, so a bad combination kills the app itself. |
| `JSC_useJIT=false` (control) | yes | WebAssembly still works; the JS workload went from 10 ms to 346–348 ms. This proves the variable is honoured, and shows what a stray `JSC_*` in a user's environment can do. |

**Side effects of `JSC_useWasm=false`** (`results/timing.json`, 7 alternating runs each, profile `base`): host start to first page script, median 149.7 ms default vs 146.0 ms with the variable (ranges 140–472 and 138.5–164; the 472 was the first, cold run). The JS workload (30 M iterations, best of 3) took 10 ms in both. `eval` and CSP reporting were unchanged. The script message handler and `evaluate_javascript` worked in every run. The variable is also present in the network process (harmless; it runs no page JS) and in the UI process's own JSC.

**.NET.** `results/dotnet-env.txt` (SDK 11.0.100, JIT `dotnet run` in the Linux image):

```
managed Get after SetEnvironmentVariable: false
native getenv after SetEnvironmentVariable: (null)
child via libc system(): not set
libc setenv returned 0
native getenv after setenv: false
child via libc system(): set
```

So on Linux `Environment.SetEnvironmentVariable` changes only .NET's own copy. WebKit spawns its processes with GLib, which uses the native `environ`. A NativeAOT build was not run, but the managed environment is the same runtime code.

## Q3. Other ways

| Approach | Observed | Robustness |
|---|---|---|
| **`WebKitSettings` / features** | 58 settings properties, 489 features (`results/features.json`). The only WebAssembly feature is `WebAssemblyESMIntegration` (off). `enable-javascript-markup=false` would stop every page script, the app's included. No `NetworkSession` or `WebContext` setting exists for it. | Not available. |
| **`jsc_options_set_boolean` in the UI process** | The UI process's `useWasm` read back `false`; the page still had WebAssembly. | None: options are per process. |
| **Web process extension** calling `jsc_options_set_boolean("useWasm", FALSE)` from `webkit_web_process_extension_initialize_with_user_data` | With the extensions directory set right after creating the `WebKitWebContext`: `WebAssembly` undefined in page, workers and all frames (2.52.6 and 2.54.2). Before the call, `useWasm` read 1; afterwards 0. The JS workload and start time were unchanged (median 151 ms). With the directory set **inside** `initialize-web-process-extensions`, the extension never loaded and gave no error. The setter adds the directory to the bubblewrap sandbox (`WebKitWebContext.cpp:1711–1712`), but the signal is emitted after the sandboxed process has started (`:2043`). | High on the versions tested, but it means shipping a native `.so` per architecture. It also depends on the extension running before JSC freezes its options. `jsc_options_set_boolean` writes only that one option (`JSCOptions.cpp:190`); it skips `disableAllWasmOptions()` (`Options.cpp:671,814`), which the environment route runs. |
| **User script at document start, all frames** (`delete globalThis.WebAssembly`, or redefine it as a non-configurable `undefined`) | Removed from the page and from dynamically created `about:blank` and `srcdoc` frames. **Not** removed from workers (user scripts do not run there) or from the about:blank document left in an iframe whose `frame.html` load CSP blocked. `Reflect.get`, the prototype chain and `window.open` (null) gave nothing back. After `delete` the page can define its own `WebAssembly` (harmless); the locked form refuses that with `TypeError: Attempting to change configurable attribute of unconfigurable property.` | Low. Workers and the blocked-frame document keep the real global. Under `base` the CSP still blocks instantiation in both (and compile too on 2.54), so it adds little beyond the CSP. Without a CSP header on the worker, nothing stops it. |
| **`worker-src`** | `'self'` already blocks `blob:` workers. It cannot remove WebAssembly from a same-origin worker. | Not a WebAssembly control. A CSP header on every worker script is needed instead (Q1). |

## Recommendation

1. **Default (WebAssembly off):** the Linux host calls libc `setenv("JSC_useWasm", "false", 1)` (or `g_setenv`) at startup. This happens before GTK and WebKit initialise and before any other thread runs, because `setenv` is not thread-safe. Overwriting replaces any inherited value. Keep the CSP without `'wasm-unsafe-eval'`, as on Windows. The result: no `WebAssembly` global in any realm, so the policy cannot be bypassed through a worker or frame, at no measured cost.
2. **Opt-in:** the C# opt-in that adds `'wasm-unsafe-eval'` to `script-src` also makes the Linux host call `setenv("JSC_useWasm", "true", 1)`. The variable is then set explicitly in both cases, and a `JSC_useWasm=false` inherited from the user's shell cannot silently break an app that opted in. One declaration drives both, so the policy and the engine setting cannot diverge.
3. **Guard the option name.** It changed once (`useWebAssembly` up to 2.44, `useWasm` from 2.48), and a wrong name is only logged. Add a test that runs the real Linux host and asserts `typeof WebAssembly === "undefined"` in the page and in a worker by default, and `"object"` with the opt-in. Spar's minimum supported WebKitGTK version has to be at least 2.48 for this name.
4. **Send the CSP on worker scripts** (simplest: on every host-served response, as this probe did). Otherwise a worker escapes `script-src` on every engine, eval included. This also affects the Windows host and the Vite plugin, and needs a WebView2 check.
5. Keep the web process extension as the fallback if the option is ever removed from the environment route. Do not use a user script for this.

Behaviour difference that remains: on WebView2 the global exists and compiling throws `CompileError`; on Linux the global is absent (`ReferenceError`, `typeof` is `"undefined"`). Feature-detecting code takes its fallback on Linux and fails at compile on Windows. Chromium's `--js-flags=--noexpose_wasm` might give the same shape on WebView2. Not tested.

## Proposed pitfalls (not added to `docs/pitfalls.md`)

- `Environment.SetEnvironmentVariable` on Linux does not change the native environment, so WebKit's child processes never see it. Use libc `setenv` before WebKit starts.
- `webkit_web_context_set_web_process_extensions_directory` called inside `initialize-web-process-extensions` is too late under the bubblewrap sandbox: the extension is never loaded, and nothing reports it. Call it before the first load.
- WebKitGTK 2.52 checks `'wasm-unsafe-eval'` only at instantiation: `compile`, `new WebAssembly.Module` and `compileStreaming` succeed under a CSP that blocks WebAssembly. Probe instantiation, not `compile`.
- A worker script served without a CSP header runs `eval` and WebAssembly under a page with a strict CSP.
- `JSC_*` variables are read by the UI process too. An incoherent set (for example `JSC_useWasmIPInt=false JSC_useBBQJIT=false`) aborts the app, and `JSC_useJIT=false` from a user's shell made JS about 35 times slower. An invalid name is only logged.
- `dotnet run` of a file-based app in a container hung twice: once after the program had printed (the Roslyn compiler server it started was still alive), and once before running, with `dotnet run` as PID 1. With `docker run --init` and `UseSharedCompilation=false` it ran and exited normally (`tools/dotnet-env.sh`).

## Not tested

- Windows/WebView2: workers without a CSP header, `--noexpose_wasm`, violation reports for blocked WebAssembly. Nothing on Windows was run.
- The Spar C# NativeAOT host (only the Python GI host and a JIT .NET check), and GTK 3 / WebKitGTK 4.1.
- A web process restarted after a crash, a second web view, or process swaps. The variable is inherited by each spawn and the extension loads per process, but this was not observed.
- Shared and service workers, worklets, and `WebAssembly.Memory`-only abuse (Memory creation is allowed under every policy).
- Memory use with `useWasm=false`, and the WebAssembly JIT tiers on a large module.
- ARM64, a Flatpak sandbox, and distributions other than Ubuntu 24.04 (2.52.6) and Fedora 43 (2.54.2).

## Leftovers

- Docker image `spar-webkit-wasm-off:fedora43` (1.9 GB). Remove with `docker rmi spar-webkit-wasm-off:fedora43`.
- WSL: `~/spar-spikes/webkit-wasm-off/` (copied pages and host, the compiled extension, private HOME, outputs).
- `artifacts/webkit-wasm-off/src/` (WebKit source files, 1.6 MB).

# M1c: WebAssembly off by default on the Linux host (`JSC_useWasm`, workers, web-process restarts)

Recorded 2026-11-20. What it shows: the Linux host's WebAssembly setting of plan M1c, build step 5 (`docs/evidence/webkit-wasm-off`), on the bridge probe (`tests/apps/BridgeProbe`) and on its own build with `csp.AllowWebAssembly()`, under an Xvfb of the run's own, as Development builds and as Production NativeAOT executables: `typeof WebAssembly` and a module's result in the page and in a dedicated, a shared and a service worker; an inherited `JSC_useWasm` of the other kind replaced; the setting in the web process that replaces a lost one and in the one "Reload page" starts; the Notebook. Decision "Linux WebAssembly off by default (implementation record; choices for review; 2026-11-20)".

## Environment

- **Linux:** WSL2 `Ubuntu-24.04` (kernel 6.18.58.1-microsoft-standard-WSL2), the normal user, no root; `libwebkitgtk-6.0-4` 2.52.6, `libgtk-4-1` 4.14.5, Mesa llvmpipe, SDK 11.0.100. The Xvfb of `docs/evidence/m1c-host/wsl.sh tools`, on its abstract socket only, with no window manager; a private D-Bus session per test run. Never WSLg's display.
- **Isolation:** each app run has its own `HOME` and `XDG_*` folders under the working copy's `artifacts/linux-homes/host-tests/<run>/`; `XDG_RUNTIME_DIR` is `/dev/shm/spar-m1c-wasm`; every inherited `JSC_*` variable is cleared by the runner, and the tests set their own.
- **Input:** none. "Reload page" was answered by the Development build's `SPAR_DEV_PROMPT_ANSWERS`, a lost web process was a SIGKILL from the test.
- **Network:** nothing left the machine; the page's module is 41 bytes in its own script.
- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, the same tree.
- **Working copy:** the Windows tree as it was, uncommitted changes included, copied to `/var/tmp/spar-m1c-wasm/repo` (`wsl.sh sync`); the opt-in build from a copy of the probe's folder in it, `tests/apps/BridgeProbeWasm` (`wsl.sh probewasm`), which exists only there.

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Linux/EngineEnvironment.cs` | `JSC_useWasm` written with libc `setenv` and overwrite 1, `false` or `true` from the page security, checked with `getenv`; the start-up line with the inherited value it replaced; a warning for every other inherited `JSC_*` |
| `src/Spar.Linux/LinuxApp.cs`, `AppWindow.cs` | The page security resolved (`PageSecuritySetup.Resolve`: the build's copy from the catalog) after the prerequisite checks and before the GPU check, instead of in the window; the window takes it; exit 70 if the variable cannot be set |
| `tests/apps/BridgeProbe` | `-p:BridgeProbeWebAssembly=true` defines `BRIDGE_PROBE_WEBASSEMBLY`, which declares `csp.AllowWebAssembly()`; the step `wasm` (`frontend/src/wasm.js`, `frontend/src/wasm-worker.js`); `again-<step>` runs a step after a reload only |
| `tests/Spar.Tests/LinuxPageSecurityTests.cs` | Two theories, default and opt-in: the page, the workers, the inherited value, the policy and the web process's environment, then a lost web process; then "Reload page" (Development) |
| `tests/Spar.Tests/LinuxHostTests.cs`, `LinuxAppRun.cs`, `LinuxHostUnitTests.cs` | The Notebook's web process, before and after a loss, without WebAssembly; `SPAR_LINUX_WASM_APP`; the setting's value and line |
| [`m1c-wasm/wsl.sh`](m1c-wasm/wsl.sh), [`m1c-wasm/`](m1c-wasm/) | The runner, the recorded results (`linux-wasm-development.txt`, `linux-wasm-production.txt`, `notebook.txt`) and the apps' logs of the last run (`dev/`, `aot/`, named by build and case) |

## Commands

From Git Bash at the repository's root, after `docs/evidence/m1c-host/wsl.sh tools` once:

```
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-wasm/wsl.sh sync client appmodel build test probe probewasm wasm regress probeaot wasmaot notebook
```

`build` rebuilds the probe as a Debug build that embeds no page, so `probe` comes after it; `probewasm` copies the probe's folder after `probe`. On Windows: `dotnet build Spar.slnx -c Debug` (and `--no-incremental`), the solution's tests without the `Window` and `BlackBox` surfaces, the NativeAOT `Spar.Tests` lane (`dotnet publish tests/Spar.Tests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.Tests`, then the executable), `dotnet run eng/check-size.cs`.

## Results (observed)

From the last full run (2026-11-20, 10:28–10:36 local).

| Check | Result |
|---|---|
| Windows build | `dotnet build Spar.slnx -c Debug --no-incremental`: 0 warnings, 0 errors |
| Windows solution tests | 1,048: 976 passed, 72 skipped, 0 failed |
| Windows NativeAOT `Spar.Tests` | 713: 657 passed, 56 skipped (every Linux test skips before any process starts), 0 failed |
| Size check | 502 files, 0 errors, 0 warnings (`tests/Spar.Tests` still at 40 files; `LinuxPageSecurityTests.cs` 651 lines) |
| SDK tests in WSL (`wsl.sh client`) | 266 of 266 |
| Linux solution tests (`wsl.sh test`) | 1,048: 958 passed, 90 skipped, 0 failed |
| WebAssembly tests, Development builds (`wsl.sh wasm`) | 9 of 9 (the two theories' four rows and the unit theory's five) |
| The same, Production NativeAOT executables (`wsl.sh wasmaot`) | 9: 7 passed, 2 skipped ("Reload page" needs the Development build's scripted answer) |
| Steps 1–4's Linux tests on this host, Development probe (`wsl.sh regress`) | 181: 176 passed, 5 skipped (no opt-in build, Notebook or flood sizes named), 0 failed |

### WebAssembly where the page runs code

Each cell is the same in both builds (`linux-wasm-development.txt`, `linux-wasm-production.txt`); the inherited `JSC_useWasm` was the opposite of the declaration in every run of the first theory, and replaced.

| Where | Default (no opt-in) | `AllowWebAssembly()` |
|---|---|---|
| The page | `typeof WebAssembly` `"undefined"` | `"object"`, the module instantiated, `add(2, 3) = 5` |
| A dedicated module worker | `"undefined"` | `"object"`, `5` |
| A shared module worker | `"undefined"` | `"object"`, `5` |
| A service worker | never registered: `register()` rejects with `TypeError: serviceWorker.register() must be called with a script URL whose protocol is either HTTP or HTTPS` | the same |
| After the web process was killed (the silent reload) | the same three answers | the same three answers |
| After "Reload page" (`webkit_web_view_terminate_web_process`, then the reload; Development) | the same three answers | the same three answers |
| The web process's environment (`/proc/<pid>/environ`) | `JSC_useWasm=false` | `JSC_useWasm=true` |
| The policy's `script-src` | no `'wasm-unsafe-eval'` | `'wasm-unsafe-eval'` |

- **The start-up lines,** in every run: `WebAssembly off: JSC_useWasm=false, replacing the inherited JSC_useWasm=true.` or `WebAssembly on (AllowWebAssembly()): JSC_useWasm=true, replacing the inherited JSC_useWasm=false.` (without the replacement when nothing was inherited, as in the "Reload page" runs), after `Page security overrides in this app: …` with `AllowWebAssembly()` for the opt-in; and `WARN … JSC_useWebAssembly=… from the environment reaches JavaScriptCore in this app and its web processes; Spar lets it through.` for the old option name the test passed too.
- **Its cost:** the page security, the setting and the GPU check together took 0.2 ms of the start (`prerequisites checked at 17.6 ms`, `gpu checked at 17.8 ms`, Production NativeAOT probe).
- **The probes:** both Production NativeAOT executables are 4,416,736 B.

### The Notebook (Production NativeAOT, `notebook.txt`)

| Measure | Result |
|---|---|
| Executable | 5,094,768 B (+20,992 B over step 4) |
| Launch to `ui-ready`, warm | median 224.4 ms (215.9–239.3, n = 11) |
| Close request to exit | median 38.9 ms (18.2–55.9) |
| Total PSS, app and WebKit processes | 231.2 MB |
| WebAssembly | `WebAssembly off: JSC_useWasm=false.` in its log; `JSC_useWasm=false` in its web process and in the one after a loss |
| Violations | 0 lines |

## Not verified

- A real desktop, Wayland, a real GPU; WebKitGTK 2.54 and other distributions (the spike's Fedora 2.54.2 run covered the variable, not Spar's host).
- Frames (the spike saw the variable reach them; the probe's policy refuses same-origin frames, and the lock refuses `about:blank` and `srcdoc` frames on Linux).
- Worklets and nested workers; a second window or a web-process swap (one window per app today).
- Windows: nothing changed there, and the window suite was not rerun.

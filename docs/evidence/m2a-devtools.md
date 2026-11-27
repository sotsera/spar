# M2a: the devtools core, the page tools and the CLI

Recorded 2026-11-26. What it shows: plan M2a, build steps 1 to 5 (section 6, `docs/design/agents.md`): a Development build of an app listens on an owner-only local socket, records it in `obj/spar-devtools.json`, and answers newline-delimited JSON-RPC 2.0 with ten tools; the `spar` tool's flat commands use them. Decisions "M2's decide-first questions", "M2a's decide-first questions" and "M2a: the devtools (implementation record; choices for review; 2026-11-26)". The user-facing description is `docs/guide/devtools.md`.

## Environment

- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 158.0.4370.33. The Notebook's Debug apphost ran on a desktop of the run's own (`CreateDesktop`, through `docs/evidence/m1d-hot-reload/WatchDesktop.cs`): no window reached the monitor, took the focus or got input. Data folders under `artifacts/webview2-profiles/m2a-devtools/<run>/`.
- **Linux:** WSL Ubuntu 24.04 (kernel 6.18.58.1), GTK 4.14.5, WebKitGTK 2.52.6, the repository's copy in `/var/tmp/spar-m1c-blackbox/repo` (`docs/evidence/m1c-blackbox/wsl.sh`, steps `sync build devtoolssmoke`): the Notebook's Debug build on a private Xvfb (`:171`, abstract socket only) with a private D-Bus session, its home and XDG folders under the copy's `artifacts/linux-homes/m2a-devtools/`, its runtime folder in `/dev/shm`, software rendering. Nothing opened on WSLg's display.

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Devtools/` | The switch (`SparDevtools`), the discovery file (`DevtoolsFile`), the socket (`DevtoolsListener`: a named pipe with a protected access list for the current user, or a 0600 Unix socket in `XDG_RUNTIME_DIR` with an `SO_PEERCRED` check), JSON-RPC framing (`JsonRpc`), the server (`DevtoolsServer`: `initialize` and its token first, duplicate ids, the `run` check), the tools (`DevtoolsTools`, `PageTools`), the call record (`CallRecorder`, 200 calls), the merged log (`DevtoolsLog`, a logger provider, 1,000 entries), the page interface (`IDevtoolsPage`) and the hosts' part (`DevtoolsHost`) |
| `src/Spar/Hosting/Folders/OwnerOnlyFile.cs` | The owner-only file the loopback session's record used, now shared with the discovery file |
| `src/Spar/Contracts/ISparCall.cs`, `src/Spar/Runtime/SparDispatcher.cs` | `ISparCall.Caller` (`SparCaller.Page`, `Test`, `Devtools`); the dispatcher records each call when the devtools are in the build |
| `src/Spar.Windows/Devtools/DevtoolsPage.cs`, `BrowserView.cs`, `AppWindow.cs`, `WindowsApp.cs` | The window tests' `TestPage` installed before the first navigation, CDP `Input.*` for clicks and `Input.insertText` for typing; dialogs left to the user |
| `src/Spar.Linux/Devtools/DevtoolsPage.cs`, `BrowserView.cs`, `AppWindow.cs`, `LinuxApp.cs` | The same before the first load, with the engine's synthetic click and `insertText` |
| `src/Spar.Loopback/LoopbackApp.cs` | The backend tools; the page tools answer `unavailable` |
| `src/Spar.Sdk/Sdk/Devtools/PageEngine.js` | The page engine, moved from `Spar.Testing` (which embeds it from here): `snapshot` with references, the `ref` locator step, `syntheticClick`, and `prepareFill`'s element |
| `src/Spar.Sdk/Sdk/Sdk.targets` | `SparDevtools` (default true), the switch on in Development only (`Trim="true"` off elsewhere), `Spar.Devtools.File`, the engine and the contract snapshot embedded in Development builds |
| `src/Spar.Tool/Devtools/` | `spar tree`, `screenshot`, `click`, `fill`, `wait`, `contracts`, `call`, `calls`, `logs`, `version`: discovery, the pipe or socket client, text and `--json` output |
| `tests/Spar.Tests/DevtoolsServerTests.cs`, `DevtoolsPageToolsTests.cs`; `tests/Spar.Tool.Tests/DevtoolsCommandTests.cs` | 20 and 15 tests |
| `tests/Spar.BuildTests/ProductionBoundary.cs`, `NotebookBuildTests.cs` | 7 type markers and 4 literals; controls in Spar.dll |
| `docs/evidence/m2a-devtools/smoke.sh`, `smoke-windows.sh`; `wsl.sh` step `devtoolssmoke` | The evidence runs below |

## Results (observed)

### Tests

- **Windows**, `dotnet test --solution Spar.slnx -c Debug` (window tests included): 1,475 (1,362 passed, 113 skipped, 0 failed); before M2a 1,440.
- **WSL**, `wsl.sh sync client build test` (no window or black-box tests): 1,277 (1,236 passed, 41 skipped, 0 failed); the devtools tests ran on the Unix socket. `wsl.sh window`: 220 (170 passed, 50 skipped, 0 failed), no process left; the Linux view's and test page's changes kept the window tests passing.
- **NativeAOT and the boundary (Windows):** the AOT lane of `Spar.Tests` 803 (7 skipped), its 20 devtools tests included; `Spar.BuildTests` 7 of 7, the Notebook's Production NativeAOT executable without any of the new markers.
- **The devtools' own:** `DevtoolsServerTests` (10): the discovery file is owner-only (the access list on Windows, 0600 for the file and the socket on Linux) and goes at the end, a later run's file is left; a first request that is not `initialize`, or a wrong token, is `unauthorized` and ends the connection; a reused id is `duplicate_id` and the command ran once; another run is `stale_target`; a bad line is a parse error; `call` runs as `SparCaller.Devtools`, gives a value, a declared error and a failure, and `calls` lists them in order; a call past its wait is `timeout` and listed as `running`; `logs` merges and filters by source, level and time; `version` and `contracts`; the line reader's CRLF and its 1 MiB limit. `DevtoolsPageToolsTests` (10) against a scripted page: `no_page` and `unavailable`; a reference before any tree and after the document changed is `stale_target`; the trusted click presses at the engine's point, presses again when the press did not land, runs the frames step once, and lists the calls made during it; the synthetic click; `ambiguous` with its matches, `timeout` with the last reason, a test id's quoting; fill's insertion, its empty text and the engine's own setting; wait's states and ambiguity; the screenshot's size. `DevtoolsCommandTests` (15): no run, an ended run, eight wrong command lines (64), the requests (`initialize` with the token, `run`, `timeout`, the target object), the tree's and an action's text, `call` with its request and `--json`, a refusal's reason and matches (exit 2), and the screenshot's file.

### The smoke runs

The same sequence (`docs/evidence/m2a-devtools/smoke.sh`) on both systems: version, tree, a click on "New page", a wait for the dialog, a fill of its title, a click on "Create page", a wait for the new page's heading, tree, calls, `call pages.list`, `call pages.get` with a missing id, contracts, logs, the page's logs, a screenshot, then four refusals.

- **Windows** ([`logs/windows-smoke.txt`](m2a-devtools/logs/windows-smoke.txt), [`windows-notebook.png`](m2a-devtools/windows-notebook.png)): every step as expected; `version` says `trustedInput: true` and the clicks carry no synthetic note. "Create page" listed `pages.create` and `pages.list` (5.6 and 5.2 ms).
- **Linux** ([`logs/linux-smoke.txt`](m2a-devtools/logs/linux-smoke.txt), [`linux-notebook.png`](m2a-devtools/linux-notebook.png)): the same, with `trustedInput: false` and each click marked as the page's own events; the page's React handlers took the synthetic events (the dialog opened, the page was created). SIGTERM closed the app (exit 0), which removed its discovery file; no process was left.
- **The refusals, on both:** two headings without `--nth` is `ambiguous`, listing the `<h2>` and the `<h1>`; a button that never appears is `timeout` after 500 ms with "no element matches"; `e9999` is `stale_target` at once ("not a reference of the last tree (e1 to e22)"); `fill e1` without text is exit 64.
- **"Calls while it ran":** on both systems the first click listed two `pages.list` calls, which were the page's own first load (React's development mode lists twice) still running when the smoke clicked; an action lists the calls made from its start until the app is quiet, not only those it caused, and the output and the guide say so.
- **Loopback on Windows** ([`logs/windows-loopback.txt`](m2a-devtools/logs/windows-loopback.txt), `SPAR_HOST=Loopback`, `SPAR_LOOPBACK_BROWSER=none`): `call pages.list` and `calls` worked (`caller` devtools), `tree` answered `unavailable` with its reason, `logs` showed the host's lines. The first `version` found the previous run's file, left because the harness ends the app by killing its tree, and said that run had ended; the new run's file came a moment later.

### Found and fixed during the runs

- The run's id and the element were both `target` in a request; the run's is now `run`.
- `wait` on a locator matching two elements kept retrying and said "the page did not answer"; it is now `ambiguous` at once.
- `fill` printed no element; the engine's `prepareFill` now names it.
- The tool printed numbers in the machine's culture (4,8 ms on an Italian Windows); and its redirected output was in the console's code page (an em dash became "-"). Both now invariant and UTF-8.
- A multi-line field's value broke the tree's lines; it is shown with `\n`.
- On Linux, a run's runtime folder under `/var/tmp` made WebKit's sandbox fail and the app abort; it goes in `/dev/shm` (pitfall).

## Not verified

- The devtools under an IDE's F5 (the same build, so expected to work), and with `dotnet watch` restarts (each run writes a new file; the old run's connections end).
- A real Linux desktop: Wayland, and a page whose code checks `event.isTrusted`.
- Windows Sandbox and CI.

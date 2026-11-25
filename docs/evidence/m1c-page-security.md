# M1c: page security on the Linux host (the navigation lock, the headers, the reports, the permission gate, the folders)

Recorded 2026-11-20. What it shows: the Linux host's page security of plan M1c, build step 4, on the bridge probe (`tests/apps/BridgeProbe`) under an Xvfb of the run's own, as a Development build and as a Production NativeAOT executable: the navigation lock in `decide-policy` against every way a script can try to leave, frames and an address declared with `FrameSrc`, new windows and a user's click on an external link, the policy and headers on every response kind, both violation channels with their flood limits, the `permission-request` gate, and the network session's folders. Decision "Linux page security (implementation record; choices for review)".

## Environment

- **Linux:** WSL2 `Ubuntu-24.04` (kernel 6.18.58.1-microsoft-standard-WSL2), the normal user, no root; `libwebkitgtk-6.0-4` 2.52.6, `libgtk-4-1` 4.14.5, Mesa llvmpipe, SDK 11.0.100. The Xvfb of `docs/evidence/m1c-host/wsl.sh tools`, on its abstract socket only, with no window manager; a private D-Bus session per test run. Never WSLg's display.
- **Isolation:** each app run has its own `HOME` and `XDG_*` folders under the working copy's `artifacts/linux-homes/host-tests/<run>/`; `XDG_RUNTIME_DIR` is `/dev/shm/spar-m1c-page-security`.
- **Input:** two tests click the middle of the app's window with XTEST on the run's own Xvfb (a link over the whole page, a button over the whole page); no other input. The external address goes to a stub handler the test writes into the run's own `XDG_DATA_HOME/applications` and `XDG_CONFIG_HOME/mimeapps.list`, after `gio mime x-scheme-handler/https` with the run's folders named the stub; the stub appends the address to a file. No real browser could open (WSL has no `xdg-open`, and without the stub no handler for `https`).
- **Network:** nothing left the machine: blocked addresses are `*.invalid`, and the one site the page may reach is the test's own on `127.0.0.1`.
- **Camera and microphone:** never asked for (an unattended run on a machine with a person); `enumerateDevices` only.
- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime as in M1b, the same tree.
- **Working copy:** the Windows tree as it was, uncommitted changes included, copied to `/var/tmp/spar-m1c-page-security/repo` (`wsl.sh sync`).

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Linux/PageGuard.cs` | The lock on navigation, new-window and response decisions; the permission gate by the request's GType name; `query-permission-state`; external addresses through `g_app_info_launch_default_for_uri` on a thread of its own; refusals logged 100 times a run |
| `src/Spar.Linux/BrowserView.cs` | `decide-policy`, `permission-request`, `query-permission-state`, failing closed; a capture-phase `GtkEventControllerLegacy` that notes button, key and touch events; the bridge's epoch ended at `LOAD_STARTED` only after a decision for the app's origin, a declared frame address in the window stopped as it starts, a foreign commit sent back to the app's page; the session's folders read back |
| `src/Spar.Linux/SchemeServer.cs` | `report-uri` answered 204 at once, its body read with `g_input_stream_read_all_async` within `ReportLimiter` and at most 16 KiB, then handed to `CspViolationLog` |
| `src/Spar.Linux/BridgeRelay.js`, `ScriptBridge.cs` | The violation listener in the relay's world with Windows' limits written in; its batches read on the reader without a session; each document opened in the log |
| `src/Spar.Linux/AppWindow.cs`, `LinuxApp.cs`, `Interop/` | The log per window, `BrowserProfile` registered, an absolute `Profile(…)` required, web processes with no window left ended after 3 s; the WebKit, GTK, GIO and GObject imports, checked against the 2.52.6 GIR in WSL |
| `src/Spar/Security/NavigationRules.cs` | The external-address rule (moved from `Spar.Windows/ExternalLinks.cs`) and the `FrameSrc` match, for both hosts |
| `src/Spar/Hosting/SparFolders.cs`, `CspViolationLog.cs`, `CspReportReader.cs` | `ProfileFolderName` (`WebView2` on Windows, `WebKit` on Linux); the listener's and the endpoint's limits as constants; a violation without a source file grouped without its position |
| `tests/apps/BridgeProbe` | `Permission.Geolocation` declared, `BRIDGE_PROBE_PROFILE`, the steps of `frontend/src/security.js` (`nav-script`, `nav-about:<which>`, `nav-declared`, `frames`, `headers`, `worker`, `violation`, `distinct:<n>`, `permissions`, `permissions-click`, `external-link`) and `frontend/src/worker.js` |
| `tests/Spar.Tests/LinuxPageSecurityTests.cs` | 14 tests in a real window, the XTEST click, the loopback site, the stub handler |
| `tests/Spar.Tests/LinuxHostUnitTests.cs`, `CspViolationLogTests.cs`, `SecurityDefaultsTests.cs`, `src/Spar.Client/test/webkit.test.ts` | The lock's addresses, frame sources, external addresses on Linux, every permission kind, queries, the folder names; one violation from both channels without a source file; the relay's listener (grouping, the interval, the bounds, the cut texts, off) |
| [`m1c-page-security/wsl.sh`](m1c-page-security/wsl.sh), [`m1c-page-security/`](m1c-page-security/) | The runner, the recorded numbers and the apps' logs of the last runs (`dev/` Development, `aot/` Production NativeAOT, named by the probe's step) |

## Commands

From Git Bash at the repository's root, after `docs/evidence/m1c-host/wsl.sh tools` once:

```
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-page-security/wsl.sh sync client appmodel build test probe security regress probeaot flood notebook
```

`build` rebuilds the probe as a Debug build that embeds no page, so `probe` must come after it and before `security`, `regress` and `flood`; `flood` uses the NativeAOT probe that `probeaot` publishes. On Windows: `dotnet build Spar.slnx -c Debug`, the solution's tests without the `Window` and `BlackBox` surfaces, `dotnet test --project tests/Spar.WindowTests -c Debug --no-build`, the NativeAOT lanes of `tests/Spar.Tests` and `tests/Spar.WindowTests` (`dotnet publish … -c Release -p:SparTestLane=Aot -o artifacts/aot/<name>`, then the executable), `dotnet run eng/check-size.cs`.

## Results (observed)

From the last full run (2026-11-20, 09:45–09:54 local), [`linux-page-security-development.txt`](m1c-page-security/linux-page-security-development.txt), [`linux-page-security-production.txt`](m1c-page-security/linux-page-security-production.txt), [`linux-csp-flood.txt`](m1c-page-security/linux-csp-flood.txt), [`notebook.txt`](m1c-page-security/notebook.txt) and the apps' logs.

| Check | Result |
|---|---|
| Windows build | `dotnet build Spar.slnx -c Debug`: 0 warnings, 0 errors |
| Windows solution tests | 1,039: 971 passed, 68 skipped, 0 failed (`tests/AppModel.Tests` and the static-state list among them) |
| Windows window suite | 118: 106 passed, 12 skipped (Sandbox-tagged), 0 failed |
| Windows NativeAOT lanes | `Spar.Tests` 704: 652 passed, 52 skipped (every Linux test skips before any process, site or listener starts); `Spar.WindowTests` 118: 106 passed, 12 not run (Sandbox); 0 failed |
| Size check | 0 errors, 0 warnings (501 files; `tests/Spar.Tests` at 40 files, the limit) |
| SDK tests in WSL (`wsl.sh client`) | 266 of 266 (`webkit.test.ts` 14, 4 of them the relay's listener) |
| Linux solution tests (`wsl.sh test`) | 1,039: 953 passed, 86 skipped, 0 failed |
| Linux page-security tests, Development probe | 14: 13 passed, 1 skipped (the opt-in flood numbers) |
| The same, Production NativeAOT probe | 14: 13 passed, 1 skipped |
| Steps 1–3's Linux tests on this host (`wsl.sh regress`) | 158: 156 passed, 2 skipped, 0 failed |

### The navigation lock

| Case | Result |
|---|---|
| `location` to `https://example.invalid/top`, `spar://other/`, `data:text/html,…`, a `blob:` address; a script's click on a link to another origin; a meta refresh | each ignored by the lock with its own log line; the page still on `spar://app/`, on its first document (both builds) |
| `location` to `file:///etc/hostname` | refused by WebKit before the lock was asked (no decision); the page stayed |
| A form posted to another origin | refused by `form-action 'none'` (a violation line); the page stayed |
| `window.open(…)`, a script's click on a `target="_blank"` link | refused by WebKit's popup blocker before any decision (`window.open` answered `null`) |
| `location` to `about:blank` or `about:srcdoc` | ignored by the lock; the page stayed on its document. Before the lock refused them (an earlier build), both committed in the window and replaced the page |
| Frames: `src="about:blank"`, `srcdoc` | the navigation ignored; the blank frame kept its first document (`about:blank`, same origin), the `srcdoc` frame stayed empty |
| A frame of the declared site (`FrameSrc("http://127.0.0.1:*")`) that redirects to `http://example.invalid` | the frame's request reached the site; the redirect was refused by `frame-src` (one line, `blocked http://example.invalid`) before the lock was asked |
| The window sent to the declared site's page | stopped as the load started ("the window started to load … the load was stopped"), the page on its document with its bridge; the site still received the request; a second web process was left with no window and ended at the exit, exit 0 (it made the app abort, 134, about 10 s after its exit before that change) |
| A user's click (XTEST) on a `target="_blank"` link to `https://example.invalid/from-click`, after the script's own click on it | the script's click refused; the user's handed to the desktop's handler: the stub got exactly that address, about 1.0 s after the host's line (998–1,004 ms); one window throughout. Before the real-input check (an earlier build), the script's click right after the load opened the address too |

### Headers

The page read them with `fetch` from the document, a module, the worker script and a missing file: each had `Content-Type` exact (`text/html; charset=utf-8`, `text/javascript; charset=utf-8`), the policy with `default-src 'none'`, `connect-src spar://app`, `require-trusted-types-for 'script'` and `report-uri /_spar/csp-report`, `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, COOP and CORP `same-origin`, and `Permissions-Policy` with `geolocation=(self)` and `camera=()`; the missing file was 404 with the same set. A module worker the host served: `eval` and `new Function` blocked (`EvalError`), a `fetch` to another origin refused; **no violation reached either channel from the worker** (WebKitGTK raises no event and sends no report there, as `docs/evidence/linux-gpu-csp` found).

### Violations

| Case | Result |
|---|---|
| One violation (`img-src`, both builds) | 1 log line; at exit "1 listener batches, 1 report-uri reports read, 0 refused unread, 0 dropped"; no summary |
| A violation with no source file (`style-src-attr` inline) | the listener said line 1, column 1, the report no position: two lines until the key left the position out; one since (unit test) |
| 2,000 distinct (`img-src`, one address each) | raised in 57 ms (Development), 78 ms (Production); 100 lines, 1 listener batch (it stopped at 1,000 and said so), 20 reports read, 1,980 refused unread (the rate limit), 0 dropped |
| 1,000 / 10,000 / 100,000 distinct, Development (`SPAR_AUTOMATED=1`) | page time 31 / 305 / 2,998 ms; 100 / 101 / 101 lines (the 101st says the group limit was passed), 1 summary, 1 batch, 20 / 21 / 34 reports read, 980 / 9,979 / 99,966 refused, 0 dropped, no hang alarm |
| 1,000 / 10,000 distinct, Production | 33 / 316 ms; the same lines, batches and reports |

WebKitGTK sent a report for every distinct violation (WebView2 157 sent 89 per document).

### Permissions

| Case | Result |
|---|---|
| `geolocation`, declared | "Permission request for geolocation from spar://app/: granted (declared)"; the position then failed with code 2 (no location service), not 1 |
| `Notification.requestPermission()`, `clipboard.readText()`, `enumerateDevices` without a click | "denied", `NotAllowedError`, 0 devices with hidden labels, decided by WebKit itself: no request reached the host |
| The same notifications and clipboard calls and `requestPointerLock` in a real click's handler (XTEST) | "Permission request for notifications …: denied" and "… for pointer lock …: denied"; the page saw `denied`, `refused`; the clipboard refused by WebKit without a request; one window throughout, no prompt |
| `permissions.query` | the host answered every query; the page saw `granted` for geolocation (granted in the document), `prompt` for notifications, camera and microphone (WebKit shows the host's "denied" as "prompt"); `clipboard-read` is not a name WebKit knows (`TypeError`) |
| Kinds no run could raise | the camera, the microphone and screen capture (never asked for), media keys (EME off), storage access, XR: covered by the unit test of every kind |

### Folders

| Case | Result |
|---|---|
| Defaults | "Browser data folder `<data root>/WebKit`, cache `<data root>/cache/WebKit`", read back from the session's data manager; WebKit wrote `WebKit/storage`, `WebKit/mediakeys` and `cache/WebKit` |
| Everything the run wrote under its `HOME` and `XDG_*` folders | 15 files: the app's folder (`WebKit/…`, `cache/WebKit`, `instance.lock`, `logs/app.log`) and, outside it, only Mesa's `cache/mesa_shader_cache` |
| `spar.Folders(f => f.Profile("<run>/chosen-profile"))` | the session used it (read back), the default `WebKit` folder was not created |
| `Profile("relative/profile")` | exit 70, "is not an absolute path on a local drive" |

### The Notebook (Production NativeAOT)

5,073,776 B (+113,456 B over step 3), 0 violation lines in its log, its browser data in `WebKit` and `cache/WebKit`; launch to `ui-ready` median 217.2 ms (211.3–242.2) and 224.5 ms (220.0–235.3) in two runs of 11, close to exit 37.4 and 40.4 ms, total PSS 235.1 and 234.4 MB. Its image holds no `CspAdvice`, `PageSecurityMismatch` or `DevelopmentTriggers` (UTF-8) and no "Allow it with csp." or `SPAR_DEV_` (UTF-16).

## Not verified

- The camera, microphone and screen-capture requests; a clipboard request (WebKit refused before asking); media keys, storage access and XR.
- The desktop's real handler, the OpenURI portal in a sandbox, and whether the opened browser gets the focus under Wayland (no launch context is passed).
- Autoplay: not gated on Linux in this step (WebKitGTK ignores `autoplay=()`).
- Service and shared workers; the page's error reports on Linux (not built); a real desktop, a window manager, a compositor.
- Windows' behaviour for frames declared with `FrameSrc` (its lock cancels them; unchanged here).

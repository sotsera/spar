# WebView profiles, shutdown and data clearing spike: results

Recorded 2026-11-14. Questions: what happens to the browser processes and the page's data when the host exits right after closing its WebViews; what a quick relaunch or a second instance on the same user-data folder (UDF) does; what profiles isolate and cost; how an app should clear its browsing data; which fresh profile tests should use. The same questions on Linux for WebKitGTK 6.0 and 4.1.

## Summary and recommendation

The Windows findings come first; the Linux ones (question 6) follow them.

**Windows (WebView2 Runtime 157.0.4341.46):**

1. **Exiting at once after `Close` is safe and loses nothing.** In 330 measured runs every `msedgewebview2` process exited, and every write made just before the close (`localStorage`, IndexedDB, a persistent cookie, Cache Storage, a stream of IndexedDB transactions) was there at the next start. Killing the host without `Close` lost nothing either. **The ~6.5 s exits are SmartScreen:** when the controller is closed less than about 0.6 s after a navigation, the navigation's reputation request to `nav-edge.smartscreen.microsoft.com` is still connecting, and the browser waits about 6.4 s for it (30 of 30 runs: 6.40–6.49 s). `ICoreWebView2Settings8.IsReputationCheckingRequired = false` removes the request and the wait (30 of 30: 52–67 ms). Closing later than about 0.75 s after the navigation also avoids it (139 ms median).
2. **A quick relaunch just works, but it inherits the old browser's exit.** Environment creation succeeds at once (6–11 ms) even while the previous browser is still running; controller creation then waits for that browser to exit and starts a new one (it never attaches to the exiting one), so after a young close the relaunched window took 6.6 s instead of 0.3 s. Different browser options during that window did not fail. **A second instance while the first runs** shares its browser when the options are identical, and fails at controller creation with `0x8007139F` (`ERROR_INVALID_STATE`) when the additional browser arguments, the language or `ExclusiveUserDataFolderAccess` differ, or with `0x8000FFFF` when the first instance holds the folder exclusively. Environment creation itself succeeds in every case.
3. **Profiles isolate everything the page stores**; InPrivate data never reaches the disk. A second window on the same profile costs about 26 MB of private memory, a second profile about 30 MB, an InPrivate window 27 MB, and a second UDF (a second browser) 149 MB, against 147 MB for one window.
4. **Clear data with `ClearBrowsingDataAll`:** 69–74 ms, no restart, and afterwards the page's token was in no file of the profile. `ALL_SITE` alone keeps the browsing history, which held the page's URL. **Do not use `Profile8.Delete` for a setting:** the deleted name can never be used again (`ERROR_DELETE_PENDING` on every later start, also after the folder is gone). Deleting the whole UDF after `BrowserProcessExited` takes 11–13 ms but needs every window closed.
5. **Tests:** InPrivate on a shared environment is the fastest fresh profile (76–79 ms per test after the first, nothing on disk). A fresh UDF per test costs about 0.5 s in-process and 0.7 s per launched process, including exit and deletion.

**Linux (WebKitGTK 2.52.6, 6.0 and 4.1 behave the same unless noted):**

6. **Exiting without waiting is safe and fast.** With the host gone, the web and network processes (and on 6.0 the four `bwrap` and the `xdg-dbus-proxy` processes) exited 3–24 ms after the close (medians 6–15 ms) in 320 runs, also when the host was killed; no write was lost (`localStorage`, IndexedDB, the last committed transaction of a stream). Destroying the window runs `pagehide`, `visibilitychange` and `unload`, not `beforeunload`. On the custom scheme cookies are never stored and Cache Storage refuses requests, on both APIs.
7. **Relaunch:** nothing to handle; the previous children were always gone before the next start (0 of 60). **A second instance on the same folders is not refused and corrupts data:** each instance runs its own network process on the same files, and after both had exited the `localStorage` written by both was gone (2 of 2 runs on each API). Single-instance enforcement before the first web view is a data-integrity requirement on Linux, not only a UX one.
8. **Data sets:** separate folders (6.0 `WebKitNetworkSession`, 4.1 `WebKitWebsiteDataManager` with its own `WebKitWebContext`) isolate `localStorage` and IndexedDB; ephemeral sets write nothing to disk. Memory (PSS, UI process included): one view 212 MB (6.0) and 209 MB (4.1); a second view +23–24 MB; a second data set +24 MB on 6.0, where all sessions share one network process, and +32 MB on 4.1, where each context has its own.
9. **Clearing:** `webkit_website_data_manager_clear(ALL, 0)` took 30–38 ms and left 104 kB of empty databases (from 7.85 MB), with the token in no file. Deleting the folders after the processes have gone took about 1 ms.
10. **Tests:** an ephemeral data set per test, 60–69 ms after the first; a fresh process with fresh folders, 213–300 ms from launch to every process gone.

**Recommendations:**

| Topic | Recommendation |
|---|---|
| Shutdown wait | Close every controller, then exit without waiting. Wait for `BrowserProcessExited` only when the same process will delete or reuse the folder (data reset, tests), with a bounded timeout: it fires about 160 ms after the browser has really exited, and never when no controller was created. |
| SmartScreen | Set `IsReputationCheckingRequired = false` by default. The app serves only its own origin and the navigation lock keeps it there, so the check protects nothing, and it costs 6.4 s at every early exit plus an external request at every navigation. This touches a browser security setting (plan 3c level 2), so it needs the user's decision and a C# opt-in back. |
| Relaunch | No retry logic needed. Show the window at once; the controller completes when the old browser has gone. Measure controller creation in the startup milestones, so a slow previous exit is visible. |
| Second instance | Enforce single instance before creating the environment (handoff to the running instance). If one slips through, map `0x8007139F` and `0x8000FFFF` from controller creation to a clear "another instance with different settings is running" error. |
| Profiles per window and instance | One UDF and one environment per app; all windows on the default profile. A named profile only where the app needs separate data (an account, a sandboxed tool window). Never a second UDF in one app. |
| Clear-data setting | `ClearBrowsingDataAll` on the live profile, then reload the windows. A full "reset" that also removes preferences and caches is "delete the UDF at the next start, before the environment is created". |
| Test profiles | In-process tests: InPrivate views (on distinct `ProfileName`s when tests run in parallel, because InPrivate views of one profile share data). End-to-end tests that launch the app: a fresh UDF per run under `artifacts/`, deleted after the harness has confirmed every browser process of it is gone. |
| Linux shutdown | Destroy the windows, release the views and data sets, and exit; do not wait for the children when the process is exiting. Keeping the main loop running until the children exit (plan section 3) should matter only while the app process lives on, for example after closing one window (inferred: here the children always exited within 24 ms of the process exit; a window closed in a process that keeps running was not measured). |
| Linux instances | Take the single-instance lock (plan: lock file plus Unix socket) before creating any data set; never let two processes open one data folder. |
| Linux data sets and clearing | One data set per app (data and cache folders under the app's XDG folders). Prefer 6.0, where an extra network session is cheap. The clear-data setting calls `webkit_website_data_manager_clear` with `WEBKIT_WEBSITE_DATA_ALL`. Tests use an ephemeral data set per test, or fresh folders per launched process. |

## Method

### Environment

| Item | Value |
|---|---|
| OS | Windows 11 Pro 10.0.26320, 32 logical CPUs |
| WebView2 Runtime | 157.0.4341.46 (Evergreen, system install) |
| WebView2 SDK headers | Microsoft.Web.WebView2 1.0.4314.35; interfaces hand-declared with `[GeneratedComInterface]`, slots counted from the header's C vtables |
| .NET | SDK 11.0.100, NativeAOT win-x64 and linux-x64, repository `Directory.Build.props` (warnings as errors, code style enforced): **0 warnings, 0 errors, no suppressions** for all three builds |
| Linux | WSL 2 Ubuntu 24.04, kernel 6.18.58; WebKitGTK 2.52.6 for both APIs (4.1 with GTK 3, 6.0 with GTK 4); GDK's **Broadway** backend on a private display (`broadwayd` / `gtk4-broadwayd`), so no window reached the Windows desktop; no GL there, so WebKit ran without hardware acceleration; web-process sandbox on for both (always on in 6.0, enabled in 4.1); a private D-Bus session per run. Executables 3,096,032 bytes each. |
| Network | Internet reachable (SmartScreen and Edge configuration requests completed when not cut short) |

### What was built

| Part | Role |
|---|---|
| `Profiles.Win/` | One NativeAOT executable, two verbs. `host` is the app: an offscreen (`-32000,-32000`), never-activated window per view (`WS_EX_NOACTIVATE`, `SW_SHOWNOACTIVATE`), one environment per UDF, one controller per window with its own `ProfileName` or InPrivate, assets served through `WebResourceRequested` (source-kinds filter) on `https://app.localhost`. Scenarios: `shutdown`, `memory`, `multi`, `private`, `clear` (`all`, a hex kind mask, or `delete`), `udfdelete`, `tests`, `names`, `scan`. `harness` runs host processes and measures them from outside. The window, UI context and loader targets are copied from `spikes/desktop-origin`. |
| `Profiles.Win/Interop/` | Flat declarations: `ICoreWebView2Environment10` (slots 3–21, with `BrowserProcessExited` 12/13, `UserDataFolder` 15, `GetProcessInfos` 18, controller options 20/21), `ICoreWebView2Profile8` (slots 3–26: `ProfilePath` 5, `ClearBrowsingData` 10, `ClearBrowsingDataAll` 12, `SetPermissionState` 15, `GetNonDefaultPermissionSettings` 16, `Delete` 24, `Deleted` 25/26), `ICoreWebView2Settings8` (`IsReputationCheckingRequired` 35/36), `ICoreWebView2ControllerOptions`, `ICoreWebView2_13.get_Profile` (105), and a host-implemented `ICoreWebView2EnvironmentOptions` + `Options2` (`ExclusiveUserDataFolderAccess`). |
| `Profiles.Win/ProcessTools.cs` | Finds the browser processes of one UDF by command line (`NtQueryInformationProcess`, class 60: every WebView2 process carries `--user-data-dir=<UDF>\EBWebView`, the crashpad handler `--database=` under it), so only this spike's processes are ever watched. Exit times come from `GetProcessTimes` on handles opened before the close. |
| `Profiles.Linux/` | The Linux counterpart (GTK3 + WebKitGTK 4.1 by default, `-p:SparGtk=4` for GTK4 + 6.0). Native imports and the GLib synchronization context are copied from `spikes/linux-memory` and `spikes/linux-desktop`. A "data set" is a `WebKitNetworkSession` on 6.0 (all views share one `WebKitWebContext`) and a `WebKitWebsiteDataManager` with its own `WebKitWebContext` on 4.1 (sandbox on). The harness watches the host's descendants through `/proc`. |
| `www/` | The probe page, shared by both hosts: reads and writes `localStorage`, IndexedDB, a persistent cookie, Cache Storage and the HTTP cache, lists service workers and the geolocation permission state; optional bulk data, `beforeunload`/`pagehide`/`visibilitychange`/`unload` writers, a service worker, and a continuous IndexedDB stream. |
| `scripts/` | `run-shutdown.sh` (question 1), `run-relaunch.sh` (2), `run-profiles.sh` (3–5) for Git Bash on Windows. |
| `tools/` | `vt.mjs` prints an interface's C vtable from `WebView2.h` with 0-based slots (used for every declaration above); `netlog.mjs` summarises a Chromium net log relative to the close. |
| `wsl/`, `win/wsl.sh` | `env.sh` (private `HOME` and XDG folders), `sync.sh`, `publish.sh`, `session.sh` (a private D-Bus session on a private GTK Broadway display), `run-all.sh` (question 6). |

```sh
# Git Bash, in spikes/webview-profiles
dotnet publish Profiles.Win -c Release -o Profiles.Win/bin/publish
bash scripts/run-shutdown.sh; bash scripts/run-relaunch.sh; bash scripts/run-profiles.sh
win/wsl.sh sync; win/wsl.sh 'bash wsl/publish.sh gtk4; bash wsl/publish.sh gtk3; bash wsl/run-all.sh gtk4; bash wsl/run-all.sh gtk3'
```

### How shutdown was measured (Windows)

Each run is a new host process on one UDF per variant. The page writes a new token everywhere and reports; the host logs `READY`; the harness then opens a handle to every process of the UDF and tells the host to close (stdin). The host removes its handlers, calls `Close` on each controller, destroys its windows and returns from `Main`, or, in the `wait` variants, first waits for `BrowserProcessExited`. The kill variants terminate the host instead (`TerminateProcess`, no `Close`). Times are kernel exit times relative to the host's `CLOSE-START` (both UTC FILETIME). The next run's page reads the previous token first, which is the data-loss check. Unless a variant says otherwise, the close came 0–5 ms after the page's report, which is the "young WebView" case.

## Results: Windows

### 1. Exiting without waiting

30 runs per variant, all processes of the UDF (browser, renderer, GPU, network service, storage service, crashpad handler; 6 per run):

| Variant | All exited after `Close` (min / p50 / p90 / max) | Data at next start |
|---|---|---|
| Default (reputation checking on) | 6,403 / 6,423 / 6,456 / 6,491 ms | 29/29 complete |
| `IsReputationCheckingRequired = false` | 52 / 59 / 62 / 67 ms | 29/29 |
| Default, closed 2 s after the page loaded | 77 / 139 / 151 / 1,004 ms | 29/29 |
| Default, `--host-resolver-rules="MAP * ~NOTFOUND"` | 113 / 129 / 137 / 139 ms | 29/29 |
| Reputation off, page with `beforeunload`, `pagehide`, `visibilitychange`, `unload` writers | 52 / 62 / 71 / 90 ms | 29/29; handlers ran, see below |
| Reputation off, page with an active service worker | 53 / 58 / 65 / 73 ms | 29/29 |
| Reputation off, page writing IndexedDB transactions back to back at the close | 66 / 76 / 86 / 143 ms | 29/29; every transaction committed by the report was present |
| Reputation off, host killed (no `Close`) | 49 / 59 / 70 / 82 ms | 29/29 |
| Default, host killed | 6,398 / 6,423 / 6,442 / 6,479 ms | 29/29 |
| Reputation off, host waits for `BrowserProcessExited` | 154 / 170 / 181 / 194 ms; host exit 180 / 326 / 345 / 389 ms | 29/29 |
| Default, host waits | about 6.5 s; host exit about 6.66 s | 29/29 |

- No process outlived its browser: 0 leftovers in 330 runs. The renderer exits 3–6 ms after `Close`; the network, storage and GPU processes go 25–50 ms later, the browser process last but one and the crashpad handler last (median 11 ms after the browser).
- The host process itself exits 17–29 ms after `CLOSE-START` (2–4 ms when killed). `Close` on one controller takes about 11 ms; on four, 53 ms.
- **`Close` runs the page's unload handlers.** The `localStorage` writes made in `beforeunload`, `visibilitychange` and `unload` were present at the next start in 29 of 29 runs, and the `pagehide` one in 26 of 29. No dialog appears (default script dialogs are off). The plan's "Close skips `beforeunload`" does not hold on this Runtime; required cleanup still must not depend on the page, because a kill or a crash runs nothing.
- **Waiting costs time without buying anything** for a normal exit: with the host waiting, the browser took 158 ms instead of 48 ms to exit, and `BrowserProcessExited` arrived a median 159 ms (15–197 ms) after the kernel exit time, so the host exited after 326 ms instead of 20 ms.

**What causes the ~6.5 s exits** (`results/probes-cause.txt`). Probes of 3 runs each (default settings): closing 100, 250 or 500 ms after the page's report gave 6.41–6.50 s; 750, 1,000, 3,000, 5,000 and 6,000 ms gave 120–167 ms. With no navigation at all, `BrowserProcessExited` came 175–180 ms after `Close`; a host that waited 1.5 s between creating the controller and navigating, then closed at once, still took 6.40–6.47 s. So the delay belongs to the navigation, not to the browser's age. The net log of one slow close (`results/netlog-young-close.txt`) shows two external requests started by the navigation and still in their TLS connect at the close: `config.edge.skype.com/config/v1/Edge/…` (Edge configuration) and `nav-edge.smartscreen.microsoft.com/api/browser/edge/navigate/3` (SmartScreen); both end at 6,477 ms, with the browser's exit. With reputation checking off (`results/netlog-young-close-reputation-off.txt`) the SmartScreen request is never made, the configuration request is cancelled at 115 ms, and the browser exits. `--disable-features=msSmartScreenProtection` gave the same result (3 runs, 58–72 ms); `--no-proxy-server` and `--disable-background-networking` did not help (3 runs each, 6.42–6.47 s), and neither did `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` (6.41–6.42 s). Blocking all name resolution (`MAP * ~NOTFOUND`) avoids it (120–133 ms), because the request then fails at once. **This matches the earlier note about SmartScreen shutdown work in young WebViews; it is now confirmed, and `IsReputationCheckingRequired = false` is the fix.**

### 2. Quick relaunch and a second instance

**Relaunch at once** (each host started the moment the previous one exited, 30 runs, `results/relaunch.txt`):

| Variant | Previous browser still running at start | Environment creation | Controller creation | Launch to page ready | Data |
|---|---|---|---|---|---|
| Young close, reputation on | 29/30 | 6 / 8 / 11 ms | p50 6,464 ms (203 ms for the first run) | p50 6,611 ms | 29/29 |
| Reputation off | 29/30 | 5 / 6 / 7 ms | 174 / 191 / 212 ms | p50 310 ms | 29/29 |
| Every other run with different `AdditionalBrowserArguments`, reputation on | 9/10 | 6–11 ms | p50 6,444 ms | p50 6,572 ms | 9/9 |
| The same, reputation off | 9/10 | 6 ms | 177–202 ms | p50 314 ms | 9/9 |

The relaunched instance never attached to the exiting browser (a new browser PID every time) and never failed, also with different options. Creating the environment does not wait; creating the controller does, until the previous browser has exited.

**A second instance while the first is running** (3 runs each, same UDF, `results/relaunch.txt`):

| Second instance | Result |
|---|---|
| Same options | Shares the first browser process (same PID); controller in 56–70 ms |
| Same options, another `ProfileName` | Shares the browser; controller in 77–80 ms |
| Different `AdditionalBrowserArguments` | Environment created (6 ms); controller fails with `0x8007139F` (`ERROR_INVALID_STATE`) after 35–39 ms |
| Different `Language` (`de-DE`) | Same: `0x8007139F` after 34–37 ms |
| `ExclusiveUserDataFolderAccess = true` | Same: `0x8007139F` after 35–38 ms |
| First instance exclusive, second not | `0x8000FFFF` (`E_UNEXPECTED`) after 35–38 ms |

The first instance kept running in every case. A host whose controller creation failed never received `BrowserProcessExited` within 20 s while it kept its environment.

### 3. Several windows and profiles

One environment, four windows: default, default, `second`, InPrivate (`results/profiles-isolation.txt`). Window 0 wrote; window 0's profile also got `SetPermissionState(geolocation, https://app.localhost, allow)`.

| Data | Same profile | Other `ProfileName` | InPrivate |
|---|---|---|---|
| `localStorage`, IndexedDB, cookie, Cache Storage | shared | isolated | isolated |
| Stored permission (`GetNonDefaultPermissionSettings`) | `3:https://app.localhost/:1` | none | `3:https://app.localhost/:0` (listed, state default) |
| HTTP cache | not testable: responses from `WebResourceRequested` were never served from the HTTP cache, even with `Cache-Control: public, max-age=86400` (every fetch reached the host) | | |

- `navigator.permissions.query({name:'geolocation'})` stayed `prompt` in every window, also in the profile with the stored allow: the page's view of a permission does not show the host's stored state.
- **Profile folders:** `ProfilePath` is `<UDF>\EBWebView\Default` for the default profile and `WV2Profile_<name in lower case>` for a named one.
- **InPrivate:** `ProfilePath` and `ProfileName` report the parent profile (the default profile's folder, or the named one with `~private` on a `ProfileName`). After the InPrivate window wrote a token and 512 KB of IndexedDB data, no readable file in the UDF contained the token while it ran, no IndexedDB or Local Storage file had been written since, and after the exit the token was in no file at all. InPrivate windows on the same profile share their data while they live (the second saw the first's writes). When the last InPrivate window of that profile closed, the data was gone: a new InPrivate window a second later saw nothing, while the browser kept running for a regular window.
- **Profile names:** 64 characters accepted, 65 rejected with `0x80070057`; `/` and `é` rejected; space, `.`, `#`, `_`, `-` accepted. Names are case-insensitive (`Alpha` and `alpha` share `WV2Profile_alpha`).

**Memory** (`results/profiles-memory.txt`): private bytes summed over the browser processes, 3 s after the windows loaded, median of 5 rounds (range in brackets); the host process itself is 8–9 MB.

| Windows | Private (MB) | Working set (MB) | Processes |
|---|---|---|---|
| 1 window | 147.1 (146.5–155.8) | 324 | 6 |
| 2 windows, same profile | 173.1 (+26) | 384 | 7 (2 renderers) |
| 2 windows, 2 profiles | 177.0 (+30) | 388 | 7 |
| 2 windows, default + InPrivate | 174.5 (+27) | 386 | 7 |
| 3 windows, 3 profiles | 207.2 (+30 each) | 450 | 8 |
| 2 windows, 2 UDFs | 296.4 (+149) | 659 | 12 (2 of each) |

A second profile costs its renderer plus about 4 MB in the browser process; a second UDF duplicates the browser, GPU, network, storage and crashpad processes.

### 4. Clearing data for a user setting

Profile `app` with 9.4–9.7 MB of data (4 MB IndexedDB, 4 Cache Storage entries, 1 MB `localStorage`, a cookie), `results/profiles-clear.txt`. Permission clearing was not checked: this scenario stored none.

| Method | Time | Profile folder after | Page data after | Token found in files after exit |
|---|---|---|---|---|
| `ClearBrowsingDataAll` (3 runs) | 69–74 ms | 3.46 MB, 156 files | none | none |
| `ClearBrowsingData(ALL_SITE)` | 50 ms | 2.66 MB | none | `History` (the page's URL with its query) |
| `ClearBrowsingData(ALL_SITE \| DISK_CACHE \| BROWSING_HISTORY \| SERVICE_WORKERS)` | 76 ms | 3.47 MB | none | none |
| `Profile8.Delete` (3 runs + 2 timing runs) | `Deleted` after 15.5–17.3 ms; the WebView is closed | folder gone within 0.3 s (present at 35 ms, gone at 300 ms), while the browser kept running for another profile | — | — |
| Delete the whole UDF after `BrowserProcessExited` (5 runs) | every process already gone 3.5–4.3 ms after the event; `Directory.Delete` 11–13 ms | — | — | — |

- The 3.5 MB left after `ClearBrowsingDataAll` are the empty profile's databases, GPU and code caches; the size did not change in the next 2 s.
- **`Profile8.Delete` poisons the name.** Creating a controller with the same `ProfileName` failed with `0x8007012F` (`ERROR_DELETE_PENDING`) at every retry for 5.4 s, after the browser exited, and at every later start (8 later starts across 4 folders, the folder long gone). `Local State` keeps `"profile_basenames_deleted": ["WV2Profile_app"]`. The documentation describes the pending state as lasting until the folder is removed; on this Runtime it does not end.

### 5. Test profiles

10 tests per mode, each writing to and reading from a profile that must start empty (`results/profiles-tests.txt`); all 30 in-process tests and all 10 per-process tests started empty.

| Mode | Per test | Breakdown | Left behind |
|---|---|---|---|
| In-process, fresh UDF per test (new environment, wait for `BrowserProcessExited`, delete) | 469–536 ms | controller (browser start) 177–246, page 93–126, close to `BrowserProcessExited` 159–193, delete 11–16 | nothing |
| In-process, fresh `ProfileName` per test, then `Profile8.Delete` | first 389 ms, then 193–369 ms | controller 50–90, page 108–299, delete 12–15 | no folders; every name is added to `profile_basenames_deleted` for good |
| In-process, InPrivate per test | first 262 ms, then 76–79 ms | controller 26–28, page 38–40, close 10–12 | nothing on disk |
| One process per test, fresh UDF (host waits, harness deletes) | launch to ready 338–361 ms; launch to all processes gone 539–1,074 ms (p50 701) | folder delete 14–16 ms | nothing |

## Results: Linux (WebKitGTK)

Same probe page on `spar://app/`, results reported through the script message handler. Each harness run starts the host, records the host's descendants from `/proc` at `READY` (pid and start time), tells the host to close, and polls every millisecond until each is gone or a zombie. The host destroys its windows, releases its views, sessions and context, quits the main loop and returns from `Main`; the `waitexit` variant first iterates the main context until no child is left. Files: `results/linux-gtk4-*.txt`, `results/linux-gtk3-*.txt`, `results/linux-unload-stream.txt`.

### 6.1 Exiting without waiting

30 runs per variant (10 for `linger2s`); all children = the network and web process, plus four `bwrap` and one `xdg-dbus-proxy` (6.0 and 4.1 both, sandbox on):

| Variant | 6.0: all children gone (min / p50 / max) | 4.1 | Data at next start |
|---|---|---|---|
| Default | 6 / 10 / 13 ms | 6 / 8 / 9 ms | 29/29 each |
| Page with unload writers | 6 / 8 / 15 ms | 6 / 8 / 12 ms | 29/29 |
| IndexedDB stream at the close | 8 / 8 / 13 ms | 8 / 9 / 14 ms | 29/29 |
| Host killed (SIGKILL) | 8 / 11 / 13 ms | 9 / 11 / 24 ms | 29/29 |
| Host waits for its children | 14 / 15 / 20 ms; host exit 20 / 20 / 26 ms | 11 / 15 / 16 ms; host exit 16 / 21 / 22 ms | 29/29 |
| Closed 2 s after the page loaded | 3 / 6 / 10 ms | 5 / 6 / 7 ms | 9/9 |

- The host process exits 4–6 ms after `CLOSE-START`; destroying one window and releasing its objects takes under 1 ms once the page is idle (35–41 ms when the close came within a millisecond of the page's report, in the hand-run smoke tests).
- Data checked: `localStorage` and IndexedDB. **Cookies are never stored for `spar://`** (`document.cookie` stayed empty, also for another view of the same data set) and **Cache Storage rejects the requests** (`TypeError: Request url is not HTTP/HTTPS`), on both APIs.
- **Unload handlers** (3 runs per API, `results/linux-unload-stream.txt`): `pagehide`, `visibilitychange` and `unload` ran when the window was destroyed and their writes were present at the next start; `beforeunload` did not run. The IndexedDB stream's last committed record was present every time (committed 70–83, stored up to committed + 1).

### 6.2 Quick relaunch and a second instance

- **Relaunch** at once, 30 runs per API: all ready (launch to page ready p50 114 ms on 6.0, 119 ms on 4.1), the previous instance's children were gone at every start, data present 29/29.
- **Second instance on the same folders**, 2 runs per API, identical results. A wrote `A1`; B started while A ran, read, and wrote `B1`; A read again after B exited; C started after both had exited.

  | Reader | `localStorage` | IndexedDB |
  |---|---|---|
  | B, while A runs | empty | `A1` |
  | A, after B exited | `A1` | `B1` |
  | C, after both | **empty** | `B1` |

  B started without any error or warning. `localStorage` is held by each instance's network process and written back on its own, so both instances' values were lost; IndexedDB ended with the last writer's value.

### 6.3 Several windows and data sets

`results/linux-gtk4-isolation.txt` and `-gtk3-`: views 0 and 1 on data set `a`, view 2 on `b`, view 3 ephemeral.

- View 0's writes were visible to view 1 and invisible to views 2 and 3; the ephemeral view's writes (a token and 512 KB of IndexedDB) were invisible to the others, and a view on a new ephemeral set saw nothing. Two views on one ephemeral set shared its data.
- The ephemeral token was in no file under the private `HOME` or the profile folders, while running or after the exit.
- 6.0: one `WebKitWebContext` for all views; every network session, persistent or ephemeral, was served by **one** network process. 4.1: every data manager needs its own `WebKitWebContext`, and each context started its own network process. Each view had its own web process on both.

**Memory** (PSS of the UI process and all its descendants, 3 s after load, median of 5 rounds; each range within 2 MB):

| Views | 6.0 total (UI process) | 4.1 total (UI process) | Children (6.0 / 4.1) |
|---|---|---|---|
| 1 view | 212.4 (99.7) | 208.5 (96.9) | network 1, web 1 / same |
| 2 views, one data set | 235.1 (+23) | 232.2 (+24) | network 1, web 2 / same |
| 2 views, two data sets | 236.3 (+24) | 240.5 (+32) | network 1, web 2 / **network 2**, web 2 |
| 1 view, ephemeral | 209.3 | 206.3 | as 1 view |
| 2 views, persistent + ephemeral | 235.6 | 233.1 | network 1, web 2 / network 1, web 2 |
| 3 views, three data sets | 260.2 (+24 each) | 271.2 (+31 each) | network 1, web 3 / network 3, web 3 |

On 4.1 the ephemeral data manager's context added no network process (1 in the mixed case, against 2 for two persistent data sets); why was not investigated.

### 6.4 Clearing data

Data set with 7.85 MB (4 MB IndexedDB, 1 MB `localStorage`), 3 runs per API plus one with chosen types:

| Method | 6.0 | 4.1 | Folders after | Page data and token after |
|---|---|---|---|---|
| `webkit_website_data_manager_clear(ALL, 0)` | 30–38 ms | 34–38 ms | 104 kB, 9 files (empty IndexedDB and resource-monitor databases) | none; token in no file |
| `LOCAL_STORAGE \| INDEXEDDB_DATABASES` (`0x30` on 6.0, `0x50` on 4.1) | 8.6 ms | 9.2 ms | 12 kB | none; token in no file |
| Delete the folders after the exit | children gone 6–13 ms after the host; `Directory.Delete` about 1 ms | same | — | — |

The type values differ between the APIs (4.1 still has `WEBSQL_DATABASES` and `PLUGIN_DATA`; `WEBKIT_WEBSITE_DATA_ALL` is `0xFFF` on 6.0 and `0x3FFF` on 4.1), so the constants must come from the build's API.

### 6.5 Test data sets

`results/linux-*-tests.txt`, 10 tests per mode; every test started empty.

| Mode | 6.0 per test | 4.1 per test |
|---|---|---|
| In-process, ephemeral data set per test | first 186 ms, then 60–65 ms | first 145 ms, then 63–69 ms |
| In-process, fresh folders per test (release the view and session, wait until no process holds a file in them, delete) | first 293 ms, then 160–167 ms; the processes let go of the folders after 12–15 ms, delete 0.3 ms | first 247 ms, then 154–167 ms |
| In-process, one data set cleared between tests | first 287 ms, then 152–161 ms (clear 11–16 ms) | first 262 ms, then 155–164 ms (clear 10–13 ms) |
| One process per test, fresh folders | launch to all gone 213–300 ms (p50 214) | 220–265 ms (p50 222) |

## Problems met

- **Option parsing bug in the spike harness:** values starting with `--` were first parsed as flags, so the first `--host "--reputation off"` and `--browser-args` probes silently ran with defaults. Those probe results were discarded and rerun after the fix; none of the tables above use them.
- `crypto.getRandomValues` accepts at most 65,536 bytes per call; the page's bulk writer chunks it.
- On WebKitGTK the page's Cache Storage writes fail (`Request url is not HTTP/HTTPS`) and `document.cookie` is never stored on the custom scheme; the probe records this instead of failing.
- C# 15 rejects `await` inside an `unsafe` class ("updated memory safety rules"); the Linux host marks only its pointer methods `unsafe`.
- In the first smoke run of the fresh-profile test mode, the first controller with a new `ProfileName` took 5,222 ms once (not seen again: the 10 fresh-profile tests of the series took 50–233 ms). It may be the rare 5 s stall already listed in the pitfalls.

## Not tested

- Shutdown or `WM_ENDSESSION` at sign-out, a browser crash (`ProcessFailed`) during the exit, a Runtime update pending at exit (`NewBrowserVersionAvailable`).
- A page whose `beforeunload` asks to stay, and long work in unload handlers.
- The HTTP disk cache of a real network origin (the host-served origin is never cached); `ClearBrowsingData` kinds other than those in the table.
- InPrivate on a named profile that has no regular window (whether it creates the parent profile's folder).
- The camera, microphone and notification permissions (not touched by rule).
- `IsReputationCheckingRequired` set after the first navigation, and whether it also stops SmartScreen's download checks (downloads are denied by default anyway).
- Windows 10, older Runtimes, a machine without network access (SmartScreen's request then fails fast; not measured), an elevated host.
- **Linux:** a real desktop and GPU (only Broadway, software rendering), WSLg and X11 window managers, the GTK4 window-manager-drag shutdown problem from `spikes/linux-memory` together with this exit path, cookies and the HTTP cache (impossible on the custom scheme), stored permissions (WebKitGTK has no API to store them), a crash of the network process, 4.1 with the sandbox off, `webkit_website_data_manager_remove` for single origins, and the folders' placement under the app's XDG folders.
- **Interference:** the `linux-memory` spike was running its WebKitGTK memory matrix in the same WSL instance shortly before and, most likely, during the Linux series (11:39–11:47 UTC, plus a short unload check afterwards). Its processes map the same libraries, so the PSS figures here (and possibly theirs) are a little low; the differences between configurations, taken minutes apart in interleaved rounds, are less affected. No process of that spike was touched.

## Paths created outside the repository

- WSL: `~/spar-spikes/webview-profiles/` only (`repo/` working copy with its own `artifacts/`, `home/` and `run/` as the private `HOME` and `XDG_RUNTIME_DIR`, `nuget/packages/` restored offline from the Windows NuGet cache). `~/.dotnet` was used read-only for the SDK.
- Windows: none. Every WebView2 user-data folder was under `artifacts/webview2-profiles/webview-profiles/`, passed explicitly, and deleted at the end after the harness had confirmed that no process of it was running. Bulky output (JSONL records, net logs, run logs) is in `artifacts/webview-profiles/`.

# Window tests: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/window-tests/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/window-tests) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-17. Host: Windows 11 Pro 10.0.26320, 32 logical CPUs, SDK 11.0.100, WebView2 Runtime 157.0.4341.46 (Evergreen), Microsoft.Web.WebView2 1.0.4314.35 (loader and header only). One NativeAOT probe, `WindowTests.Win` (3,773,440 bytes, **0 warnings**, no suppressions). Every window was shown offscreen at (-32000, -32000) with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and `SW_SHOWNOACTIVATE`, with `IsReputationCheckingRequired = false`; all input went through in-process CDP (`CallDevToolsProtocolMethod`), never through the OS. Every user-data folder was under `artifacts/webview2-profiles/window-tests/` and was deleted at the end.

Questions: testing.md issue I (profiles for window tests, cleanup after a crash), issue D (trusted input in the in-process window), and whether `Page.setInterceptFileChooserDialog` can block WebView2's native file chooser (WebView2 has no file-chooser API).

## Summary

| # | Question | Result | Recommendation |
|---|---|---|---|
| 1a | Cost per test (setup + teardown, 25 tests each, one test process) | **InPrivate on a shared environment:** p50 81 ms, p95 147 ms (setup 65 / 123, teardown 16 / 28). **New ProfileName, then `Profile8.Delete`:** 220 / 360. **New ProfileName, then `ClearBrowsingDataAll`:** 259 / 313. **New user-data folder per test:** 510 / 1,072 (setup 296 / 414, teardown 215 / 328, one 4.5 s browser exit) | InPrivate on one shared environment per test process |
| 1b | Isolation between consecutive tests | **All four modes isolate everything checked**: `localStorage`, IndexedDB, a cookie, a service-worker registration, a remembered permission (window management, saved in the profile), and the HTTP cache of a real loopback origin. 0 leftovers in 309 of 310 test bodies; the one exception was a counter race in the probe, not a leak (below) | Any mode is safe; choose by cost |
| 1c | The first test of a fresh folder | **The first controller with a named profile in a fresh folder took 5.21 s in 5 of 13 runs** (always 5,209–5,218 ms). InPrivate on the default profile: no controller stall in 16 processes; a new default-profile folder: 0 of 13. Once, the first navigation of an InPrivate test stalled 5.03 s instead (1 of 16 InPrivate processes) | InPrivate on the default profile. Budget a 5 s outlier for the first window test of a run |
| 1d | Growth over 25 tests, memory per window | Browser private bytes after 25 tests: InPrivate +8 MB, named profiles +23 MB, new folder flat. `ClearBrowsingDataAll` leaves each profile's folder: 29 folders, 100 MB at the end. **Each extra window open at once: about 30 MB** (InPrivate 174 → 207 → 233 → 265 MB for 1–4 windows) | — |
| 1e | Two test processes at once, each with its own root | No interference: separate browsers, no leftovers, every test empty. InPrivate p50 100 and 123 ms (solo 81); second pair 84 and 103 ms. New folder p50 529 and 538 ms (solo 510) | Parallel test processes are fine with separate roots |
| 1f | Test process killed mid-test | Killed alone: the browser processes exited 76–88 ms later and the folder was deleted at the first attempt 96–127 ms after the kill (5 of 5). With reputation checking on, the orphaned browser lived 6.33–6.39 s (3 of 3). Killed together with its browser processes: deletable after 44–102 ms (one retry once) | Wait for the folder to be free, then delete; no stale state is left |
| 1g | Knowing that no browser still uses a leftover folder | **`EBWebView\lockfile` is the signal.** Chromium holds it open (share-read only) and deletes it on close: "held" in every poll while the browser lived, "absent" the moment the browser process exited (also when it was killed), never left behind stale. Checking it costs 0.09 ms against 10 ms for a command-line scan of `msedgewebview2` processes. Every delete attempted while the lock was held failed with `0x80070020` (about 1,130 attempts) | Next run: lock file absent → delete the folder; held → leave it (another run or an orphan) and use a new root |
| 2a | Trusted input in a never-activated offscreen window | Clicks, `Input.insertText`, key events and Tab all arrive with `isTrusted = true`, move focus, and give user activation (`isActive` and `hasBeenActive` true). **`document.hasFocus()` is false before any input and true after the first CDP input**, `:focus` matches, and `:focus-visible` behaves as with a real user (false after a mouse click on a button, true in text fields and after Tab). The window never became the foreground window | Use CDP `Input.*` for window tests; no focus emulation needed |
| 2b | Activation-gated APIs | `window.open` reaches `NewWindowRequested` with `IsUserInitiated = 1` after a trusted click and `0` without one (the popup is not blocked either way; the host decides). `requestFullscreen` changes from "Permissions check failed" (no activation) to "not granted" (activation, but the host does not grant fullscreen). `Runtime.evaluate` with `userGesture: true` gives the same activation. The clipboard was skipped (it would touch the user's clipboard) | Check activation through `IsUserInitiated` or `navigator.userActivation`, not through fullscreen |
| 2c | Latency per action (100 each) | Click (3 mouse events): p50 14.8 ms, p95 15.2. Click with a box lookup: 16.4 / 16.9. `insertText`: 0.3 / 0.5. One key: 0.8 / 1.1. Tab: 0.7 / 1.2 | — |
| 2d | The "first input after navigation stalls 5 s" pitfall | **Not reproduced in 200 first clicks**: right after `NavigationCompleted` or after two frames, on the same controller, a new InPrivate controller, and a new folder (new browser) each time; all clicks were seen. Without a wait the first click took 3–102 ms; after two frames 16–81 ms | Keep the two-frame wait (cheap: about 18 ms); the stall is rare, not reproduced here |
| 3a | Blocking `<input type="file">` | **Works, but only with `Page.enable` first.** Without it, `Page.setInterceptFileChooserDialog` returns `{}` and changes nothing: the dialog opened at every click. With `Page.enable` + interception: no dialog in 46 of 46 file-input clicks (and 15 of 15 File System Access picker calls), `Page.fileChooserOpened` fired every time (`backendNodeId`, `frameId`, `mode`), the page saw only its `click` (no `change`, no `cancel`, 0 files), and repeated clicks kept being intercepted | `Page.enable`, then `setInterceptFileChooserDialog`, right after controller creation, before the first navigation |
| 3b | After navigation, with DevTools off, at startup | Intercepted after a same-origin navigation, a reload, a cross-origin navigation and back, `input.showPicker()`, and with `AreDevToolsEnabled = false`. Calls issued (not awaited) before the first `Navigate` were in force by the first click (5 of 5). It is per WebView: a second WebView not enabled opened the dialog | Enable per WebView, at creation |
| 3c | Iframes | Same-origin iframe: intercepted. **Cross-site (out-of-process) iframe: not intercepted, the dialog opened.** With `Target.setAutoAttach {autoAttach, waitForDebuggerOnStart, flatten}` and, per child session (`CallDevToolsProtocolMethodForSession`), `Page.enable` + interception + `Runtime.runIfWaitingForDebugger`: intercepted (2 of 2), the event carries the child's session id | Spar's navigation lock should keep foreign frames out; if frames are ever allowed, auto-attach is required |
| 3d | File System Access pickers | `showOpenFilePicker`, `showDirectoryPicker` and `showSaveFilePicker` are **intercepted too** (with `Page.enable`): no dialog, `Page.fileChooserOpened` fires (no `backendNodeId`), and the page's promise rejects with `AbortError: Intercepted by Page.setInterceptFileChooserDialog()`. Without interception they open native dialogs. `Permissions-Policy: file-system-access=(), local-fs=()` and `CSP: sandbox allow-scripts allow-same-origin` did not block them. **`--disable-blink-features=FileSystemAccessLocal` removes all three** (`typeof` is `undefined`); `--disable-blink-features=FileSystemAccess` and `--disable-features=FileSystemAccessAPI` did not | Interception covers them; removing them as well needs the browser argument (an environment-wide option) |

## How it works

- **Profiles (`ProfileTests.cs`).** One test process runs 25 "tests". Each creates its window and controller (and in `udf` mode its environment), navigates to `probe.html`, reads every store (expecting nothing), asks for the window-management permission with `getScreenDetails()` (the host's `PermissionRequested` handler allows it with `SavesInProfile = 1`; a remembered decision would mean no event), loads a page from a loopback HTTP server twice (`c.js` is cacheable for a day; the server counts requests: 1 then 0 inside a test shows the cache works, a 0 at the next test's first load would be a leak), then writes a token to `localStorage`, IndexedDB and a cookie and registers a service worker. Teardown: InPrivate, `Close`; named profile, `Profile8.Delete` and wait for `Deleted`, or `ClearBrowsingDataAll` then `Close`; new folder, `Close`, wait for `BrowserProcessExited`, wait until no process of the folder is left, delete it. Setup is window + controller + first page; teardown ends when the next test could start.
- **Across processes (`Harness.cs`).** `parallel` starts two test processes on two roots at once. `crash` starts a test process that stops mid-test (after writing everything), kills it (`host`), kills it with reputation checking on (`host-reputation`, so the orphaned browser waits for SmartScreen), or kills it and every browser process of its folder (`tree`); then polls every 5 ms: browser processes of the folder by command line (`--user-data-dir=<folder>\EBWebView`), the state of `EBWebView\lockfile` (open with `FileShare.None`), and a delete once both say free (`--eager`: a delete at every poll).
- **Input (`InputTests.cs`).** `input.html` records, for every pointer, mouse, key, input and focus event: `isTrusted`, `document.hasFocus()`, the active element and whether it matches `:focus` and `:focus-visible`, and `navigator.userActivation`. Clicks are `mouseMoved`, `mousePressed`, `mouseReleased` at the element's centre; keys are `keyDown` (with `text`) or `rawKeyDown`, then `keyUp`. `NewWindowRequested` is always handled (no window opens).
- **File chooser (`FileChooserTests.cs`).** Every click is a trusted CDP click. A dialog is detected by enumerating visible top-level windows that belong to, or are owned by, this process or the folder's browser processes; any that appears is closed at once with `WM_CLOSE` and nothing is clicked. Dialogs that did appear (all `#32770`, in the browser process) showed 30–630 ms after the click and closed 63–220 ms after `WM_CLOSE`.

## Numbers

### 1. Profiles (`results/solo-*.log`, `results/first/`, `results/par*-*.log`)

25 tests per mode, one process each, in milliseconds (min / p50 / p95 / max):

| Mode | Setup | Teardown | Total | Notes |
|---|---|---|---|---|
| InPrivate, shared environment | 58 / 65 / 123 / 234 | 15 / 16 / 28 / 29 | 73 / 81 / 147 / 250 | controller 29–34 ms after the first |
| New ProfileName, `Profile8.Delete` | 171 / 200 / 340 / 483 | 16 / 20 / 24 / 26 | 191 / 220 / 360 / 501 | `Deleted` arrived every time |
| New ProfileName, `ClearBrowsingDataAll` | 152 / 159 / 205 / 5,306 | 88 / 100 / 116 / 133 | 244 / 259 / 313 / 5,415 | first controller 5,217 ms |
| New user-data folder | 255 / 296 / 414 / 859 | 195 / 215 / 328 / 4,516 | 463 / 510 / 1,072 / 4,850 | `BrowserProcessExited` 172–285 ms after `Close`, once 4,478 ms |

The shared environment itself costs 6–12 ms to create; closing the last window to all its processes gone took 197–454 ms (1.2 s with 29 profile folders).

**First test of a fresh process** (`results/first-summary.txt`, 10 processes per mode, two tests each): InPrivate first test 245–284 ms total, second 76–91 ms, no stall in 10. `ClearBrowsingDataAll` mode first controller **5,209–5,213 ms in 3 of 10**, otherwise 193–231 ms. New folder 461–523 ms, no stall. With the solo runs and a smoke run, a named profile's first controller in a fresh folder stalled 5 of 13 times (5,209–5,218 ms each), `Profile8.Delete` mode 0 of 1; never a later named profile in the same process (0 of 58 tests). `spikes/webview-profiles` saw the same 5,222 ms once.

**Isolation.** Every test from the second on found nothing from the one before: 310 test bodies over all runs. 309 reported no leftover. One (`par2-inprivate-A`, test 8) counted 0 requests at the first loading of the cacheable script and 1 at the second, the reverse of every other test, so the request arrived after the first navigation's `NavigationCompleted` had been taken; one network fetch per test in all 310, so no cache entry crossed tests. The permission request fired again in all 310 tests; `GetNonDefaultPermissionSettings` listed only the current test's grant.

**Memory** (private bytes of all the folder's browser processes, 2 s after each window loaded):

| Windows open at once | InPrivate | Named profiles (delete) | Named profiles (clear) |
|---|---|---|---|
| 1 | 174.2 MB | 189.1 | 186.9 |
| 2 | 206.5 (+32) | 216.8 (+28) | 212.3 (+25) |
| 3 | 232.8 (+26) | 246.4 (+30) | 239.3 (+27) |
| 4 | 265.1 (+32) | 274.2 (+28) | 272.3 (+33) |

During 25 sequential tests the browser grew from 164 to 172 MB (InPrivate), 168 to 191 MB (both named-profile modes), and stayed at 162–166 MB per new folder.

**Two processes at once** (25 tests each, total p50 / p95): InPrivate 100 / 154 and 123 / 145 ms; a second pair 84 / 128 and 103 / 115 ms (solo 81 / 147); one first test stalled 5.03 s in the navigation. New folder 529 / 596 and 538 / 567 ms (solo 510 / 1,072). Every test started empty.

### 1f–g. Crash cleanup (`results/crash-*.log`, `results/check-live.log`)

| Variant | Runs | Browser processes at kill | Lock file absent after | All processes gone after | Folder deleted after |
|---|---|---|---|---|---|
| Test process killed | 5 | 6 | 55–73 ms | 76–88 ms | 96–127 ms, first attempt |
| Killed, reputation checking on | 3 | 6 | 6,325–6,363 ms | 6,341–6,394 ms | 6,387–6,434 ms |
| Killed with its browser processes | 5 | 6 | 10–40 ms | 10–56 ms | 44–102 ms (one `0x80070020` retry) |
| Reputation on, delete at every poll | 3 | 6 | 6,392–6,921 ms | — | 6,398–6,933 ms; 365–396 failed attempts before, all `0x80070020` |

- The lock file was "held" at every poll while the browser process lived and "absent" from the poll after its exit; the crashpad handler outlived it by 15–31 ms and holds nothing that blocks deletion. It was never found existing and openable ("free"): delete-on-close removes it even when the process is killed.
- Checking a live folder: lock 0.09–0.10 ms, process scan 9.6–10.7 ms (6 processes); after the kill, lock 0.013 ms "absent", scan 0 processes.

### 2. Input (`results/input.log`; `results/input-first.log` is the first run, same results without the new-folder variant)

| Step | What the page saw |
|---|---|
| Before any input | `hasFocus` false, active element `BODY`, no activation, `visibilityState` visible |
| Click on a button | `pointerdown`, `mousedown`, `focus`, `mouseup`, `click`, all trusted; `hasFocus` true from the first event; activation true; `:focus` true, `:focus-visible` false |
| Click on a field, then `insertText("hello")` | `beforeinput` and `input` trusted; value `hello`; `:focus-visible` true |
| `keyDown` "x" + `keyUp` | `keydown`, `keypress`, `beforeinput`, `input`, `keyup`, trusted; value `hellox` |
| Tab | `keydown`, `blur` on the first field, `focus` on the second, `keyup` on the second; `:focus-visible` true |
| `Emulation.setFocusEmulationEnabled` | no difference (focus was already reported) |
| Foreground window | never the test window, at start or end |

First click right after a navigation (20 each; ms, min / p50 / p95 / max): same controller, no wait 4.1 / 8.0 / 8.8 / 8.9, two frames 17.6 / 17.7 / 18.8 / 19.0; new InPrivate controller 3.3 / 11.3 / 17.8 / 19.3 and 18.0 / 18.6 / 19.3 / 19.4; new folder 54 / 78 / 87 / 102 and 18 / 66 / 73 / 81. The first run added 80 more on the same controller and new controllers (max 23 ms). Every click was seen once.

### 3. File chooser (`results/filechooser-*.log`)

`filechooser-first.log` is the first complete run, in which interception was enabled without `Page.enable`: it records the control (dialog in 3 of 3 clicks and for `showPicker()`, the page then got a trusted `cancel`), the ineffective interception, and the File System Access dialogs ("Select a file this site can read", "Select a folder this site can view", "Warning: this site can see edits you make") closing as `AbortError: The user aborted a request.` The other files are the reruns with `Page.enable`.

| Case | Clicks | Native dialog | `Page.fileChooserOpened` | Page |
|---|---|---|---|---|
| No interception | 4 | 4 | — | `click`, then `cancel` after `WM_CLOSE` |
| Interception without `Page.enable` | 3 (+10 in the first run) | all | none | `click` |
| `Page.enable` + interception | 10 + one answered + `showPicker` + 4 after navigations | 0 | 16 of 16 | `click` only, 0 files |
| The same with `AreDevToolsEnabled = false` | 16 | 0 | 16 of 16 | `click` only |
| Answer with `DOM.setFileInputFiles {files: []}` | 2 | 0 | yes | `{}` returned; no `change`, no `cancel` |
| Enable not awaited before `Navigate` | 5 (new controller each) | 0 | 5 of 5 | `click` only |
| Second WebView, not enabled | 1 | 1 | — | `click` |
| Same-origin iframe | 1 | 0 | 1 of 1 | framed `click` |
| Cross-site iframe | 1 | 1 | none | framed `click`, `cancel` |
| Cross-site iframe, auto-attach, child `Page.enable` + interception | 2 | 0 | 2 of 2, with the child's session id | framed `click` |
| FSA pickers (3 each × 5 configurations) with interception | 15 | 0 | 15 of 15, no `backendNodeId` | `AbortError: Intercepted by Page.setInterceptFileChooserDialog()` |
| FSA with `--disable-blink-features=FileSystemAccessLocal` | 3 | 0 | none | APIs `undefined` |

## Problems met

- **`Page.setInterceptFileChooserDialog` silently needs `Page.enable`.** Without it the call succeeds with `{}` and nothing is intercepted. The first file-chooser run measured this by accident; the race, iframe and FSA sections were rerun with `Page.enable`.
- **A cross-site iframe has its own CDP target.** The page session's interception does not reach it; child sessions need their own `Page.enable` (the first auto-attach attempt enabled only the interception and still got the dialog).
- **`getScreenDetails()` needs user activation**: without it no permission request is raised (`NotAllowedError`). The probe calls it through `Runtime.evaluate` with `userGesture: true`.
- **The first file-chooser watch used 300 ms windows** after the third click: a dialog that appeared later was counted in the next click's window. Only the first run (no `Page.enable`) was affected; it is evidence of "every click opened a dialog", not of "some did not".
- The parallel harness first failed reading its children's logs (`File.ReadLines` against an open writer); the children's logs were complete and are the record, and a second parallel run used the fixed harness.
- Shell heredocs with JavaScript failed under the tool's Bash wrapper; the pages were written as files.

## Not tested

- Windows 10, older Runtimes, a machine without network access, an elevated test process.
- Real OS input and real focus (only CDP input); `Controller.MoveFocus`; IME and composition events; drag and drop (out of scope).
- The clipboard (it would touch the user's clipboard); fullscreen granted by a host (the host here handles no fullscreen); the HTTP cache of `WebResourceRequested` responses (never cached, `spikes/webview-profiles`).
- What causes the 5.2 s first-controller stall for a named profile, and whether pre-creating the profile folder or a warm-up controller avoids it.
- `ClearBrowsingDataAll` mode's disk growth over long runs (29 folders, 100 MB after 29 profiles), and `profile_basenames_deleted` growth in `Local State` with `Profile8.Delete`.
- A crash of the browser process itself (only the test process, or both); a leftover folder whose orphaned browser lives indefinitely (only the 6.4 s SmartScreen case).
- File chooser: workers and `<input type=file webkitdirectory>`, a popup window's own target, interception with auto-attach and DevTools off together, and answering the chooser with real files.

## Reproduce

From the repository root, in Git Bash:

```bash
dotnet publish spikes/window-tests/WindowTests.Win -c Release -o spikes/window-tests/WindowTests.Win/bin/publish
bash spikes/window-tests/scripts/run-profiles.sh      # question 1: solo modes, parallel, crash variants
RUNS=10 bash spikes/window-tests/scripts/run-first-test.sh > artifacts/window-tests/first-summary.txt
bash spikes/window-tests/scripts/run-input-file.sh    # questions 2 and 3; opens and closes real dialogs for a moment
# A live-folder check: start a test that stops mid-test, then
spikes/window-tests/WindowTests.Win/bin/publish/WindowTests.Win.exe check --root <root>/<folder>
```

Logs go to `artifacts/window-tests/`; the copies in `results/` have the repository path replaced with `<repo>`. `tools/gen-webview.mjs` prints the flat `ICoreWebView2_22` declaration from `WebView2.h`.

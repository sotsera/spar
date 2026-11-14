# Startup prerequisites: results

Recorded 2026-11-14.

## Question

A Spar app ships as one NativeAOT executable with no installer. When it starts on a machine that lacks what it needs, the user, who often launched it from a desktop icon or a file manager with no terminal, must see a clear native error. The error says what is missing and how to fix it, and the process exits with a defined code. How is this done on Windows and on Linux, and how fast?

## Versions

| | |
|---|---|
| Host | Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 157.0.4341.46, WebView2 SDK headers 1.0.4314.35, Windows SDK 10.0.26100 |
| Windows guest | Windows Sandbox, Windows 11 Enterprise Insider 26100.9877 (24H2), 1520×823 at 96 DPI, no network, elevated `WDAGUtilityAccount`, the same runtime files (157.0.4341.46) |
| Linux images | Ubuntu 24.04 (`spar-startup-prereq:*`, see `docker/Dockerfile`): GTK 4.14.5, WebKitGTK 6.0 2.52.6 (noble-updates) and 2.44.0 (noble release pocket), bubblewrap 0.9.0, xdg-dbus-proxy 0.1.5, Xvfb + openbox 3.6, sway headless + grim, zenity 4.0.1, kdialog 23.08, yad, dunst, libnotify-bin, x11-utils (xmessage) |
| WSL | Ubuntu 24.04 with systemd running (real journald), WebKitGTK 2.52.6, no window opened |
| Executables | `Startup.Win.exe` 2,360,832 bytes (with manifest), 2,359,808 (without); `Startup.Linux` 2,941,808 bytes, linking only libc and libm; `Startup.Probe.exe` 2,746,368. All NativeAOT, 0 warnings, no suppressions. |

## Method

| Path | Contents |
|---|---|
| `Startup.Win/` | The Windows check (WinExe, NativeAOT, WebView2 loader linked statically). Order: OS build (`RtlGetVersion`), `GetAvailableCoreWebView2BrowserVersionString`, `CompareBrowserVersions` against a declared minimum, environment creation, then a window and controller creation (`--stage version\|environment\|controller`). A problem goes to stderr, then the event log (`--eventlog`), then a task dialog with a fallback to `MessageBoxW` (`--ui auto\|taskdialog\|messagebox\|none`). `-p:StartupManifest=none` builds it without an application manifest. `--kill-browser` ends every `msedgewebview2.exe` child as soon as it appears, until controller creation completes. |
| `Startup.Probe/` | Guest-only driver: starts the app with a redirected stderr, times launch to the first visible window and to the `#32770` dialog (2 ms polling), saves the screen and the dialog as PNG, dumps the dialog with the native UIA client, invokes buttons and links through UIA, records the exit code. Also sets the guest's colour mode. Refuses to run outside `WDAGUtilityAccount`. |
| `sandbox/` | `start.sh`, `connect.sh`, `find-client.ps1`, `close-client.ps1` adapted or copied from `spikes/windows-sandbox-checks`; `guest.sh` stages the three executables in a new folder per call; guest scripts `prepare.ps1` (guest facts, broken runtime copies under `C:\rt`), `cases.ps1` (the case table), `theme.ps1`, `console.ps1`, `hide-runtime.ps1`, `regfix.ps1`, `regdump.ps1`. |
| `host-runs.sh` | Host-safe runs: `--ui none` only, so nothing appears on the desktop. |
| `Startup.Linux/` | The Linux check (NativeAOT linux-x64). Every GLib, GTK and WebKit import is called only after `NativeLibrary.TryLoad` found its library. Order: display environment, GLib, GTK 4, `gtk_init_check`, WebKitGTK 6.0, `webkit_get_*_version`, the helper folder (from `/proc/self/maps`), `bwrap` and `xdg-dbus-proxy`, and a trial `bwrap` run. A problem goes to stderr, the journal (native protocol, else `/dev/log`) and then the first graphical way that works. `--try-view` loads a page in a real `WebKitWebView` after the checks. |
| `docker/` | `Dockerfile` (targets `base`, `nogtk`, `gtk4`, `webkit`, `webkit-old`), `build.sh` (AOT publish inside the existing `spar-linux-desktop:webkit2.52` image, offline restore from the Windows NuGet cache), `cases.sh` (one throwaway container per case and round), `in-container.sh` (breaks one piece as root, starts socat on the journal and `/dev/log` sockets, then as uid 1000 with private `HOME`/`XDG_*`: session D-Bus, Xvfb + openbox or sway headless, the app, window detection, screenshot, `Return` to dismiss), `summarize.sh`. |
| `wsl/journal.sh` | The no-display case in WSL against the real journald (display variables removed, so no window). |
| `tools/win-summary.mjs` | Per-case table from the guest logs. |
| `results/` | `windows/host/`, `windows/guest/` (logs, probe summaries, UIA dumps, screenshots), `linux/` (per case and round: `app.jsonl`, `stderr.txt`, `journal.txt`, `devlog.txt`, `summary.txt`, round-1 `shot.png`; `summary.txt` for all runs), `wsl/`, `distros/` (package manifests). |

**Times.** On Windows the app logs milliseconds since the kernel created the process (`GetProcessTimes`), so they include loading; the probe measures from `Process.Start` to a visible dialog. On Linux the app logs from `Main`; the harness measures from launch to a new top-level window (openbox's `_NET_CLIENT_LIST`, dunst's override-redirect popups, sway's tree). In the containers the executable was read from a Windows bind mount, and process creation to `Main` read 20–130 ms from `/proc` (10 ms resolution); in WSL from ext4 the whole no-display run took 6–23 ms. Values are median (min–max) over 3 rounds unless stated.

**Simulated versus real.** Windows "runtime missing" is real in three forms (an empty fixed-version folder; the guest's default state, whose registration is stale; the guest with its EdgeUpdate client key renamed). "Too old" and "OS too old" are simulated with a declared minimum above the installed version (999.0.0.0, build 99999). "Broken" uses copies of the guest's runtime with `msedgewebview2.exe` deleted, replaced by `where.exe`, or `EmbeddedBrowserWebView.dll` deleted, plus a browser killed at every start. On Linux every missing piece is real (an image without the package, or files removed in a throwaway container). "Older than the minimum" is a real WebKitGTK 2.44.0 against a declared minimum of 2.46: the 6.0 API first shipped with 2.40, so no `libwebkitgtk-6.0` older than the plan's 2.40 exists.

## Windows results

### Detection (guest, `results/windows/guest/summary-cases.txt`)

| Case | Detected by | HRESULT | Shown as | Exit | App: process creation to dialog, ms | Probe: launch to dialog, ms |
|---|---|---|---|---|---|---|
| Runtime missing: empty fixed-version folder (real) | `GetAvailableCoreWebView2BrowserVersionString` | `0x80070002` | task dialog | 3 | 54 (46–112) | 99 (97–195) |
| Runtime missing: guest default, registration points at a deleted folder (real) | same, no folder passed | `0x80070002` | task dialog | 3 | 81 (68–102) | 173 (125–247) |
| Runtime missing: EdgeUpdate client key renamed (real) | same | `0x80070002` | task dialog | 3 | 67 (55–128) | 137 (129–278) |
| Runtime folder without `EmbeddedBrowserWebView.dll` | same: reported as not found, not as broken | `0x80070002` | task dialog | 3 | 58 (50–108) | 124 (95–194) |
| Runtime older than the declared minimum (simulated) | `CompareBrowserVersions` = −1 | – | task dialog | 4 | 66 (49–104) | 110 (92–141) |
| OS older than the declared minimum (simulated) | `RtlGetVersion` | – | task dialog without link | 7 | 112 (50–123) | 175 (101–256) |
| `msedgewebview2.exe` deleted | environment created (5–6 ms); **controller creation fails** | `0x80070002` after 4–6 ms | task dialog | 5 | 79 (71–110) | 115 (111–143) |
| `msedgewebview2.exe` replaced by a program that exits at once | controller creation fails | `0x800700AA` (`ERROR_BUSY`) after 113–138 ms | task dialog | 5 | 245 (182–285) | 319 (268–324) |
| Browser process killed at every start | controller creation fails | `0x800700AA` after 59–63 ms | task dialog | 5 | 168 (144–192) | 229 (206–234) |
| Browser process killed once (first start) | nothing: controller created after 437 ms | `S_OK` | – | 0 | – | – |
| Everything present | controller created in 281–305 ms | `S_OK` | – | 0 | – | – |
| Guest registration repaired (`regfix.ps1`) | evergreen found, controller created (6.0 s, first browser start in the guest) | `S_OK` | – | 0 | – | – |

- **`BrowserProcessExited` never fired** in any failure case.
- **Environment creation does not start the browser.** On the host, `--stage environment --hold-ms 2000` left no `msedgewebview2.exe` child, and environment creation succeeded in 5–6 ms with `msedgewebview2.exe` deleted or replaced. A broken runtime shows only at controller creation, which the app performs anyway.
- **The loader follows `ClientState\{F3017226-…}\EBWebView`.** The guest ships with `Clients\…\pv` = 157.0.4341.42 and `ClientState\…\EBWebView` pointing at a `157.0.4341.42` folder that does not exist (the files are 157.0.4341.46). Setting `pv` to .46 changed nothing; pointing `EBWebView` at the .46 folder made the runtime found (`results/windows/guest/regdump.txt`, `hide.txt`). The registry check that the documentation offers as an alternative would have reported this runtime as installed.
- **Without `[STAThread]`,** the main thread is in the MTA and environment creation fails with `RPC_E_CHANGED_MODE` (`0x80010106`) after 5.6 ms (host, first run). A check that maps every creation failure to "runtime broken" would blame the user's runtime for a host bug.
- **Renaming the runtime folder** in the guest failed with "access denied"; only the key rename was used.

**Host, no window (`results/windows/host/`):** the version-stage cases took 25–29 ms from process creation to the exit decision (`Main` itself at about 25 ms), with exit codes 0, 3, 4 and 7 as above; the wall time from Git Bash was 103–143 ms.

### How it was shown

**Task dialog** (`TaskDialogIndirect`, `results/windows/guest/cases/*-shot-dialog.png`):
- Title: the app name. Main instruction: "Spar Sample can't start". Content: what is missing, what to do, and a link "Get the WebView2 Runtime from Microsoft". A custom button "Download the WebView2 Runtime", plus Close. "Show details" expands the HRESULT, the step, the folder and the exit code.
- The link and the button open `https://developer.microsoft.com/microsoft-edge/webview2/consumer/`, the end-user page that Microsoft's [distribution guide](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution) sends users to. The task dialog never opens links itself ([`TASKDIALOGCONFIG`](https://learn.microsoft.com/windows/win32/api/commctrl/ns-commctrl-taskdialogconfig)): `TDN_HYPERLINK_CLICKED` calls `ShellExecuteW`, which returned 42 (success). The button returns `S_FALSE` so the dialog stays open after opening the page. The guest has no network, and no browser window was seen within 5 s, so the page itself was not observed.
- The dialog is created 31–171 ms after the call: 46–123 ms after process creation for a version-stage failure.
- `TASKDIALOGCONFIG` is declared under `pshpack1.h`; the run-time size check gave 160 bytes on x64.

**Without a Common Controls 6 manifest** (`app-nomanifest.exe`): `TaskDialogIndirect` threw `EntryPointNotFoundException` at its first call (1.1–1.4 ms). comctl32 is not in NativeAOT's direct-call list (`WindowsAPIs.txt`), so it is bound lazily and the exe still starts. The fallback `MessageBoxW` appeared, with the classic look and DPI awareness 0 (unaware). With `--ui taskdialog` and no fallback, nothing appeared: only stderr and exit 3. The manifest given through `ApplicationManifest` is embedded in the NativeAOT exe (the comctl and `PerMonitorV2` strings are in the binary, and the process ran per-monitor aware).

**Message box** (`MessageBoxW`, `MB_YESNO | MB_ICONERROR | MB_SETFOREGROUND`): no links, so it asks "Open Microsoft's download page now?", and Yes calls `ShellExecuteW` after it closes. It appeared 25–81 ms after process creation. With `MB_YESNO` the title-bar Close button is disabled.

| | Task dialog | Message box |
|---|---|---|
| Link, download button, expandable details | yes | no (Yes/No question instead) |
| Needs a Common Controls 6 manifest | yes, or `EntryPointNotFoundException` | no |
| DPI awareness of the dialog (process `PerMonitorV2`) | 2, per monitor | **1, system aware** |
| Without a manifest | not available | 0, unaware |
| Dark mode (guest switched to dark, `results/windows/guest/dark/`) | stays light | stays light |
| Process creation to dialog | 46–123 ms | 25–81 ms |

**UI Automation** (native client, `results/windows/guest/cases/*-uia.txt`):
- **Task dialog:** a `Window` named with the app name contains:
  - `Text` "Spar Sample can't start" (`MainInstruction`);
  - a `Pane` (`ContentLink`) whose name is the whole content text, with a `Hyperlink` child named after the link text;
  - after expanding, a `Text` (`ExpandedInformationText`) with the details;
  - `Button`s "Show details"/"Hide details", "Download the WebView2 Runtime" and "Close";
  - an `Image` named "MainInstructionIcon", which is not a meaningful name.

  UIA `Invoke` worked on the expando, the link, the custom button and Close.
- **Message box:** a `Window` named with the caption, one `Text` with the whole message, `Button`s Yes and No, and an unnamed `Image`.

**stderr** (the app is a GUI-subsystem exe):

| Started from | Path taken | Result |
|---|---|---|
| Git Bash with `2> file`, the probe's pipe, PowerShell (`&`, piped, `Start-Process -NoNewWindow`) and `cmd /c` inside this tool's session | the inherited handle (file or pipe) | text written; PowerShell's `&` did not wait and left `LASTEXITCODE` empty |
| A batch file in a real console window in the guest, no redirection (`results/windows/guest/console/console.png`) | `AttachConsole(ATTACH_PARENT_PROCESS)`, `CONOUT$`, `WriteConsoleW` | text in the console; the batch waited and saw errorlevel 3, and so did `start /wait` |

**Event log** (guest only): `ReportEventW` with the unregistered source "Spar Sample" succeeded. `Get-EventLog` shows the text after "The description for Event ID '1000' … cannot be found". `Get-WinEvent -FilterHashtable @{ProviderName=…}` found nothing (`results/windows/guest/eventlog.txt`).

**OS too old:** only simulated. The PE header gives OS and subsystem version 6.0. So the Windows loader would start the exe on Windows 7/8.1, and whether the .NET 11 runtime fails there before `Main` with the loader's own error is untested.

## Linux results

### Detection and display (`results/linux/summary.txt`, 3 rounds each)

| Case (image; what was removed) | Detected | Shown as | Exit | Launch to window, ms |
|---|---|---|---|---|
| No GTK 4, no helpers (`base`, xmessage removed) | `libgtk-4.so.1` not loadable | stderr + journal only | 3 | – |
| No GTK 4, xmessage present (`base`) | same | xmessage (`results/linux/nogtk-xmessage-r1/shot.png`) | 3 | 137 (128–144) |
| No GTK 4, KDE (`nogtk`, `XDG_CURRENT_DESKTOP=KDE`) | same | kdialog | 3 | 114 (82–178) |
| No GTK 4, yad only | same | yad (GTK 3) | 3 | 158 (123–162) |
| No GTK 4, notification daemon running (dunst) | same | D-Bus `Notify` through GIO, critical, no expiry | 3 | 97 (82–176) |
| No GTK 4, `--ui notify-send` | same | notify-send | 3 | 129 (82–133) |
| No GTK 4, dunst installed but not running | same | D-Bus activation crashed the daemon (signal 5); notify-send exit 1; then xmessage | 3 | 364 (301–449) |
| GTK 4, no WebKit (`gtk4`), X11 | `libwebkitgtk-6.0.so.4` not loadable | GtkAlertDialog (`gtk4-alert-r1/shot.png`) | 3 | 483 (464–502) |
| Same, `GSK_RENDERER=cairo` | same | GtkAlertDialog | 3 | 132 (114–212) |
| Same, Wayland (sway headless) | same | GtkAlertDialog (`gtk4-alert-wayland-r1/shot.png`) | 3 | 754 (749–817) |
| Same, Wayland, `GSK_RENDERER=cairo` | same | GtkAlertDialog | 3 | 129 (91–141) |
| Same, `--ui zenity` | same | zenity | 3 | 554 (492–556) |
| WebKit helper folder removed | `/proc/self/maps` → `webkitgtk-6.0/WebKit*Process` missing | GtkAlertDialog | 5 | 541 (520–545) |
| `bwrap` removed | not on `PATH` or in `/usr/bin` | GtkAlertDialog | 3 | 483 (457–488) |
| `xdg-dbus-proxy` removed | same | GtkAlertDialog | 3 | 542 (516–554) |
| Sandbox denied (Docker's default seccomp, uid 1000) | trial `bwrap` exits 1 in 1.5 ms: "No permissions to create new namespace" | GtkAlertDialog | 5 | 538 (491–608) |
| Same, **without the trial** (`--no-sandbox-trial --try-view`) | nothing | WebKit's GLib `ERROR`: "Failed to fully launch dbus-proxy", SIGTRAP | **133** | none; process ended after 4.4–6.7 s |
| WebKitGTK 2.44.0 (real), minimum 2.46 | `webkit_get_*_version` | GtkAlertDialog | 4 | 455 (420–524) |
| WebKitGTK 2.44.0, minimum 2.40, `--try-view` | passed; page loaded | – | 0 | ready at 532–567 ms after `Main` |
| Everything present, `--try-view`, X11 / Wayland | passed; page `load-changed` FINISHED 100–113 ms after the view | – | 0 | ready at 514–545 / 497–555 ms |
| No display (`DISPLAY` and `WAYLAND_DISPLAY` unset) | environment probe | stderr + journal; nothing graphical tried | 6 | – |
| Display named but no server (`DISPLAY=localhost:7`) | `gtk_init_check` false after 1.6 ms | stderr + journal | 6 | – |
| No display, WSL, real journald (`results/wsl/`) | same | stderr + journal entry | 6 | 6–23 ms wall for the whole run |

**Costs inside the app:**
- library loads: GLib 0.7–2.1 ms, GTK 4 3.6–4.3 ms, WebKitGTK 7.8–9.4 ms;
- `gtk_init_check`: 29–43 ms;
- trial `bwrap`: 3.4–19 ms when allowed, 1.5 ms when denied.

`Main` to the stderr report took 14–135 ms. The rest of the GTK dialog's time is GTK 4's GL renderer on llvmpipe: with `GSK_RENDERER=cairo` the dialog appeared 3–6 times sooner.

**Journal:**
- Native protocol (`/run/systemd/journal/socket`): `MESSAGE` (binary-length form, because it has newlines), `PRIORITY=3`, `SYSLOG_IDENTIFIER=spar-sample`, and the custom fields `SPAR_STARTUP_PROBLEM` and `SPAR_EXIT_CODE`. Real journald in WSL stored all of them with `_TRANSPORT=journal` (`results/wsl/journal-verbose.txt`).
- Without that socket, the RFC 3164 line went to `/dev/log` (`journal-devlog-*/devlog.txt`).
- With neither, only stderr remained.

**GtkAlertDialog details:**
- It was built with `g_object_new_with_properties` (`message`, `detail`, `buttons` as a strv `GValue`, `cancel-button`, `default-button`, `modal`), because `gtk_alert_dialog_new` is variadic.
- It ran with no parent; GTK logs "GtkDialog mapped without a transient parent".
- The first runs had no `g_set_prgname`, so the window had no `WM_CLASS`: openbox titled it "Unnamed Window", and a class search did not find it. After calling `g_set_prgname("spar-sample")` and `g_set_application_name` before `gtk_init_check`, it was found.
- The harness dismissed it with `Return`, and the `Close` default button then returned 0.

**zenity 4 on Ubuntu 24.04 links GTK 4:** where GTK 4 was removed, it exited 127 in 7 ms with a loader error. So zenity never helps exactly when GTK 4 is missing.

### What desktops have by default (`results/distros/`)

| | GTK 4 | WebKitGTK 6.0 | bwrap | xdg-dbus-proxy | zenity | kdialog | notify-send | xmessage (x11-utils) |
|---|---|---|---|---|---|---|---|---|
| Ubuntu 24.04.5 desktop (ISO manifest) | 4.14.5 | **2.52.6** | yes | yes | 4.0.1 (GTK 4) | no | yes | yes |
| Kubuntu 24.04.5 (ISO manifest) | 4.14.5 | **no** | yes | **no** | no | yes | yes | yes |
| Fedora 43 Workstation (`@workstation-product-environment` resolved by dnf with weak dependencies, not an ISO list) | 4.20.4 | **2.54.2** | yes | yes | no | no | yes (`libnotify`) | not listed |

So a Spar app runs as shipped on Ubuntu desktop and Fedora Workstation. On Kubuntu it gets the GtkAlertDialog asking for `libwebkitgtk-6.0-4`, whose dependencies bring `xdg-dbus-proxy`.

## What failed or needed fixing

1. **Environment creation as the "broken runtime" check:** it passed for every broken copy. Moved the check to controller creation.
2. **The MTA main thread:** `0x80010106`. Fixed with `[STAThread]` on `Main` (top-level statements cannot carry it).
3. **The first `--kill-browser`** killed only the first browser process, and WebKit recovered by itself. Changed to "kill at every start".
4. **The guest's evergreen runtime** is unusable by default (stale `ClientState` path). Renaming its folder was denied; renaming the key worked.
5. **The event log read-back** with `Get-WinEvent` and a provider filter found nothing. `Get-EventLog` was used instead.
6. **The first console test** quoted the command line through `Start-Process` and failed with "The specified path is invalid" (exit 161). A batch file fixed it.
7. **On Linux, the graphical chain ran without a display** when GTK 4 was missing, because the display was only checked after GTK loaded. Now the display environment is probed first.
8. **notify-send exited 1** after 450–634 ms when D-Bus activation crashed the daemon, and was counted as shown. Now any non-zero exit of notify-send is a failure.
9. **Window detection in the harness:**
   - a class search missed the class-less GTK dialog (fixed by `g_set_prgname` and `_NET_CLIENT_LIST`);
   - dunst popups are override-redirect;
   - `swaymsg` needed `SWAYSOCK`.
10. **`wsl.exe -- bash -c '…'` expanded `$w` too early,** and Git Bash rewrote `/mnt/...` paths. A script file and `MSYS_NO_PATHCONV=1` fixed both.

## Not tested

- **DPI** above 100 %, a DPI change while the dialog is open, and mixed-DPI monitors: the Sandbox guest stays at 96 DPI. Only the reported awareness values are known.
- **Narrator** on the dialogs (speech), and Orca/AT-SPI on the GTK dialog. The containers have no accessibility bus, so GTK warns about it.
- **The download page actually opening** in a browser (no network in the guest).
- **A real Windows older than the minimum,** and Windows 10.
- **A WebView2 Runtime older than the declared minimum,** for real.
- **A per-user (HKCU) runtime install.**
- **Runtimes from non-stable Edge channels.**
- **An interactive `cmd` prompt,** which does not wait for a GUI exe, so its output lands after the prompt.
- **Linux on real desktops** (GNOME Shell, Plasma, real GPUs, HiDPI), and a launch from a real `.desktop` file through the desktop's launcher into the user journal.
- **A distribution other than Ubuntu:** the Fedora, Arch and openSUSE install commands in `Checks.cs` come from package indexes and were never run.
- **`GSK_RENDERER=cairo` set at run time** just before the dialog, rather than in the environment before start. Whether GTK picks it up after `gtk_init_check` is unknown.
- **The C# hook for replacing the dialog,** which was not prototyped.

## Recommendation

### Common to both hosts

- **Exit codes**, the same on both OSs and documented for scripts and agents:

  | Code | Meaning |
  |---|---|
  | 0 | started and closed normally |
  | 2 | invalid command line |
  | 3 | required component missing |
  | 4 | component older than the app's minimum |
  | 5 | component present but broken (creation failed, helpers missing, sandbox denied) |
  | 6 | no graphical session (Linux) |
  | 7 | OS too old (Windows) |

  Unhandled crashes keep the platform's codes (134/133 on Linux). The plan's earlier prototype used 3 for all of them.
- **The report always goes everywhere that is cheap,** in this order:
  1. stderr;
  2. the system log;
  3. one native graphical message, if a display exists.

  Each failure carries a stable kind, the component, what was found and what is required, the fix (a link on Windows, an install command for the detected distribution on Linux), technical details (HRESULT or `dlerror` text, step) and the exit code.
- **C# hook** (to settle in M1). Something like `SparApp.OnStartupProblem(Func<StartupProblem, StartupProblemResponse>)`:
  - It is called on the UI thread after stderr and the log are written and before the default dialog, with no window and no WebView.
  - It receives `Kind`, `Component`, `Found`, `Required`, `Details`, `DownloadUri` (Windows) or `InstallCommand` (Linux), `ExitCode` and the default title and text, for localisation.
  - It returns `ShowDefault`, `Handled` (the app showed its own UI) or a replacement text.
  - The exit code is not changeable, so scripts can rely on it.
  - The hook must not touch WebView or GTK APIs that may be missing. On Linux it gets a flag saying whether GTK 4 is initialised.
- **Log line:** one structured entry with the same fields, written to the app's per-user log folder (plan 3c) on Windows, and to the journal with `SYSLOG_IDENTIFIER=<app id>` plus the problem fields on Linux.

### Windows order and API

1. **`[STAThread]` Main.**
2. **OS build check** with `RtlGetVersion`, if Spar declares a minimum.
3. **`GetAvailableCoreWebView2BrowserVersionString(folder)`.** `0x80070002` means "not installed"; another failure means "broken". Never read the registry yourself: the stale `ClientState` path fooled `pv`.
4. **`CompareBrowserVersions(found, minimum)`.**
5. **Normal start-up**, where the failures map as follows:

   | Failure | Result |
   |---|---|
   | Controller creation `0x80070002` or `0x800700AA` | "broken" (exit 5) |
   | `RPC_E_CHANGED_MODE` or another host-side error | a host bug, reported as such, not as the user's runtime |

   Don't add a separate trial: environment creation proves nothing, and the controller is created anyway, 281–305 ms after it started.

   Hide or destroy the app's window before showing the dialog, or own the dialog by it. In the prototype the empty app window stayed open behind an ownerless dialog.

**Dialog:**
- `TaskDialogIndirect`, from an exe whose manifest declares Common Controls 6 and `PerMonitorV2`, which Spar's build should always embed.
- Content:
  - title: the app name;
  - main instruction: "<App> can't start";
  - content: what is missing and the fix, with a link to `https://developer.microsoft.com/microsoft-edge/webview2/consumer/`;
  - a "Download the WebView2 Runtime" button that keeps the dialog open;
  - "Show details" with the HRESULT and the exit code;
  - Close.
- Open only Spar's constant URL with `ShellExecuteW`.
- Fall back to `MessageBoxW` (Yes/No "open the download page?") when `TaskDialogIndirect` is missing or fails.
- Accept the light theme: neither dialog follows dark mode.

**stderr:** use the inherited standard error handle if it is a file, pipe or console. Otherwise use `AttachConsole(ATTACH_PARENT_PROCESS)` and `CONOUT$`, starting with a newline.

**Timing:** a missing or old runtime reaches the dialog 46–123 ms after process creation, and a broken one 71–285 ms after.

### Linux order and API

1. **Probe the display variables and sockets** (no libraries needed).
2. **Load GLib, GObject and GIO.**
3. **Load GTK 4.**
4. **Set the GTK names:** `g_set_prgname(<app id>)` and `g_set_application_name`.
5. **Set the renderer:** `GSK_RENDERER=cairo` when the user has not set it, at least for the error path; the software-GPU default is the plan's.
6. **`gtk_init_check`**, which fails with exit 6 when the display cannot be opened.
7. **Load WebKitGTK 6.0.**
8. **Version check** with `webkit_get_*_version` against the app's minimum, 2.40 by default.
9. **Helper folder:** the helpers next to the loaded library.
10. **`bwrap` and `xdg-dbus-proxy`.**
11. **A trial `bwrap` run** with `--unshare-all` (1.5–19 ms). Without it, a denied sandbox ends in SIGTRAP after 4–7 s with no message.

**Message:**
- `GtkAlertDialog` whenever GTK 4 initialised. That covers every WebKit, version, helper and sandbox case, and also Kubuntu.
- Without GTK 4, try in turn:
  1. kdialog (first when `XDG_CURRENT_DESKTOP` contains KDE), then zenity and yad;
  2. a critical D-Bus notification with no expiry, sent through GIO with non-variadic `GVariant` constructors;
  3. notify-send (any non-zero exit is a failure);
  4. xmessage.
- With no display, nothing graphical: stderr and the journal only.
- The text names the package and gives the command for the distribution family from `/etc/os-release`. For anything in the WebKit stack the command is `sudo apt install libwebkitgtk-6.0-4` on Debian and Ubuntu, which also brings GTK 4, bubblewrap and xdg-dbus-proxy.

## Checks that need the user at the PC

For the plan's table:

| Check | Why a person is needed |
|---|---|
| The start-up error task dialog and message box at 125 %, 150 % and 200 % and on a second monitor at another scale (`MessageBoxW` is only system aware) | The user's display scaling; the Sandbox stays at 96 DPI |
| Narrator reading the start-up task dialog: main instruction, content, link, details | Speech on the speakers |
| Clicking the dialog's link and download button on a connected PC: the default browser opens Microsoft's WebView2 page | Network and the user's browser |
| On a real Linux desktop (GNOME Wayland, KDE Plasma): the GtkAlertDialog and kdialog messages, Orca reading them, a critical notification staying until dismissed, and a launch from the app's `.desktop` entry leaving the journal entry (`journalctl --user -t <app id>`) | Real desktops are not reachable from here |

## Proposed pitfalls

Proposed for `docs/pitfalls.md` (not edited here):

**Windows host:**
- **WebView2 environment creation does not start the browser:**
  - it succeeded in 5–6 ms with `msedgewebview2.exe` deleted or replaced;
  - only controller creation failed: `0x80070002` (exe missing) after 4–6 ms, or `0x800700AA` when the browser exits at every start, after 59–138 ms;
  - `BrowserProcessExited` never fired;
  - one killed first start was recovered silently.
- **The Evergreen loader follows `ClientState\{F3017226-…}\EBWebView`,** not `Clients\…\pv`. A stale path (Windows Sandbox: .42 against .46 files) gives `0x80070002` even when `pv` is right. The documented registry check would say "installed". This is the cause of the existing Sandbox entry.
- **A runtime folder missing `EmbeddedBrowserWebView.dll`** is reported "not found" (`0x80070002`), not as broken.
- **Without `[STAThread]`, environment creation fails with `RPC_E_CHANGED_MODE`.** Don't report it as a broken runtime.
- **NativeAOT without a Common Controls 6 manifest:** `TaskDialogIndirect` throws `EntryPointNotFoundException` at its first call (comctl32 is bound lazily, so the exe still starts); `MessageBoxW` then looks classic and is DPI-unaware.
- **`MessageBoxW` in a `PerMonitorV2` process creates a system-aware dialog;** the task dialog is per-monitor. Neither follows dark mode.
- **The task dialog never opens its links:** handle `TDN_HYPERLINK_CLICKED`. `S_FALSE` from `TDN_BUTTON_CLICKED` keeps it open.
- **An unregistered event source:** `ReportEventW` succeeds, viewers prefix "description … cannot be found", and `Get-WinEvent` with a provider filter returns nothing.
- **A GUI-subsystem exe has standard handles only when they are inherited.** From a console without redirection it needs `AttachConsole(ATTACH_PARENT_PROCESS)`. A batch file waits for it and gets its exit code. In `cmd /c "a & echo %ERRORLEVEL%"` the variable is expanded before the app runs.

**Linux host:**
- **GTK 4 dialogs on a software GPU took 420–820 ms to appear** with the GL renderer, and 91–212 ms with `GSK_RENDERER=cairo`.
- **Without `g_set_prgname`, GTK 4 windows have no `WM_CLASS` or `app_id`:** openbox titled the dialog "Unnamed Window", and window searches by class missed it.
- **A denied sandbox:** under Docker's default seccomp, also as uid 1000, `bwrap` fails in 1.5 ms. Without a trial, WebKit aborts with "Failed to fully launch dbus-proxy" (SIGTRAP, exit 133) after 4.4–6.7 s. This extends the existing root-only entry.
- **zenity 4 links GTK 4,** so it is unavailable exactly when GTK 4 is missing (exit 127 in 7 ms).
- **D-Bus activation of an installed but not running notification daemon** started it without a display, and it crashed (signal 5). That cost 50–730 ms per attempt, and notify-send then exits 1.
- **Kubuntu 24.04 ships GTK 4 but not WebKitGTK 6.0 or xdg-dbus-proxy.**

**Tooling:**
- **dunst popups are override-redirect,** so they are absent from `_NET_CLIENT_LIST`.
- **`swaymsg` needs `SWAYSOCK`.**
- **`wsl.exe -- bash -c '…'` expands variables in the outer shell,** and Git Bash rewrites `/mnt/…` arguments (use `MSYS_NO_PATHCONV=1`).

## Leftovers

- **Docker images:**

  | Image | Size |
  |---|---|
  | `spar-startup-prereq:base` | 795 MB |
  | `spar-startup-prereq:nogtk` | 1.13 GB |
  | `spar-startup-prereq:gtk4` | 934 MB |
  | `spar-startup-prereq:webkit` | 1.17 GB |
  | `spar-startup-prereq:webkit-old` | 1.15 GB |
  | `fedora:43` | 272 MB |

  The Ubuntu images share layers. `ubuntu:24.04` was pulled as their base.
- **Downloads:** apt reported 113 MB (base), 53 MB (nogtk), 18.5 MB (gtk4), 49.5 MB (webkit) and 46.7 MB (webkit-old). The two Ubuntu manifests were 140 KB. The Fedora run downloaded repository metadata only, since it was a dry run.
- **No containers remain** (all `--rm`).
- **Windows Sandbox:** stopped, and its client window closed with it. The host's Sandbox and WebView2 process list matches the one taken before, except a `WindowsSandboxServer` started before this spike (15:05:58), which is gone too (`results/windows/guest/host-processes-*.csv`).
- **Folders:**
  - `artifacts/startup-prerequisites/` (383 MB: Sandbox in/out, Linux run folders, host logs). The 3.5 GB of runtime copies and this spike's WebView2 profiles were deleted.
  - WSL `~/spar-spikes/startup-prerequisites` (the executable, a private `HOME`, logs).
  - Journal entries tagged `spar-sample` in the WSL distribution's journal.

## Reproduce

From the repository root, Git Bash:

```bash
dotnet publish spikes/startup-prerequisites/Startup.Win -c Release
dotnet publish spikes/startup-prerequisites/Startup.Win -c Release -p:StartupManifest=none
dotnet publish spikes/startup-prerequisites/Startup.Probe -c Release
spikes/startup-prerequisites/host-runs.sh 3              # host, no window; broken runtime copies in artifacts/startup-prerequisites/runtimes/ are optional
spikes/startup-prerequisites/sandbox/start.sh && spikes/startup-prerequisites/sandbox/connect.sh &
spikes/startup-prerequisites/sandbox/guest.sh prepare
spikes/startup-prerequisites/sandbox/guest.sh cases -Out c3 -Rounds 3 -Only '^(?!link|button|mb-yes)'
spikes/startup-prerequisites/sandbox/guest.sh cases -Out c2 -Only '^(link|button|mb-yes)$'
spikes/startup-prerequisites/sandbox/guest.sh theme -Mode dark; spikes/startup-prerequisites/sandbox/guest.sh cases -Out dark -Only '^(missing-fixed|mb-missing|nomanifest-auto)$'
spikes/startup-prerequisites/sandbox/guest.sh console
spikes/startup-prerequisites/sandbox/guest.sh hide-runtime; spikes/startup-prerequisites/sandbox/guest.sh cases -Out hidden -Rounds 3 -Only '^evergreen-default$'
spikes/startup-prerequisites/sandbox/guest.sh regfix; spikes/startup-prerequisites/sandbox/guest.sh cases -Out regfixed2 -Only '^evergreen-default$'
node spikes/startup-prerequisites/tools/win-summary.mjs artifacts/startup-prerequisites/sandbox/out/c3
wsb stop --id "$(cat artifacts/startup-prerequisites/sandbox/id.txt)"

(cd spikes/startup-prerequisites/docker && for t in base nogtk gtk4 webkit webkit-old; do docker build --target $t -t spar-startup-prereq:$t .; done)
spikes/startup-prerequisites/docker/build.sh
spikes/startup-prerequisites/docker/cases.sh 3
spikes/startup-prerequisites/docker/summarize.sh
p=$(wsl.exe -d Ubuntu-24.04 wslpath -a "$(pwd -W)" | tr -d '\r'); MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash "$p/spikes/startup-prerequisites/wsl/journal.sh"
```

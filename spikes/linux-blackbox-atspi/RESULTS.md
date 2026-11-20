# The black box on Linux over AT-SPI: results

Recorded 2026-11-20. Host: Windows 11 Pro 10.0.26320 (32 CPUs), WSL 2 with Ubuntu 24.04.5 (kernel 6.18.58.1), SDK 11.0.100. Linux side: GTK 4.14.5, WebKitGTK 2.52.6, at-spi2-core 2.52.0 (`at-spi-bus-launcher`, `at-spi2-registryd`, libatspi), GLib/GIO 2.80, dbus-daemon 1.14.10, util-linux 2.39.3 (`setpriv`), Xvfb 21.1.12 (unpacked by `docs/evidence/m1c-host/wsl.sh tools`, not installed). The app under test is `tests/apps/BridgeProbe` published as a **Production NativeAOT** executable from the working tree (`wsl.sh probe`, 4,429,392 B). The reader is this spike's `Reader/`, a NativeAOT C# executable (no Python).

Decision this serves: "M1c step 7's shape: Linux window and black-box tests" (the black box gets full parity on Linux: `app.Window` and `WaitForDialogAsync`, over AT-SPI, from C#). Earlier evidence read, not copied: `spikes/window-automation-linux` (AT-SPI from Python), `spikes/startup-prerequisites` (containers without an accessibility bus), `spikes/blackbox-desktop`, `docs/evidence/m1c-startup.md`.

Every run had its own Xvfb on display `:63` (abstract socket only; `DISPLAY` and `WAYLAND_DISPLAY` unset first; nothing on WSLg), its own D-Bus session (`dbus-run-session`, or a session with no services, or none), `HOME` and the XDG folders under the copy's `artifacts/linux-homes/blackbox-atspi-<run>/`, `XDG_RUNTIME_DIR` under `/dev/shm/spar-lba-<run>` (removed after the run), and inherited `JSC_`, `WEBKIT_` inspector, `GTK_A11Y`, `NO_AT_BRIDGE` and `AT_SPI_BUS_ADDRESS` variables cleared. No root, nothing installed. After each run a census of processes carrying the run's `XDG_RUNTIME_DIR` found none (in every log). Another agent may have run Linux window tests in the same distribution meanwhile; its processes were not touched.

## Summary

| # | Question | Result |
|---|---|---|
| 1 | Route from C# | **GDBus directly** (`libgio-2.0`, which GTK and WebKit already load; Spar.Linux already imports it). About 400 lines for the bus, the AT-SPI calls, GVariant conversion and signals (`Reader/AtSpi/Bus.cs`) plus 140 lines of GLib and GIO imports. Find the app by pid and read its window's title: **1.2 ms** p50 (libatspi: 0.8 ms); one property 0.2 ms. libatspi works too (160 lines for the same subset) but is an extra package (`libatspi2.0-0t64`, not in WebKitGTK's dependency closure), brings libdbus and its own process-wide state and main-loop integration, and finds apps through the registry's list. |
| 2 | What GTK 4 needs | **A session bus that can activate at-spi2-core's `org.a11y.Bus`, and at-spi2-core installed.** The launcher starts on the first `GetAddress` (6.5–7.5 ms; 43–51 ms cold), the registry on GTK's first call. No registration of events, no `GTK_A11Y`, no `NO_AT_BRIDGE` (GTK 4 ignores it). `GTK_A11Y=none` hides the app completely, so the fixture must remove it from the inherited environment. An accessibility bus the fixture starts itself (plain `dbus-daemon`, `AT_SPI_BUS_ADDRESS`) is not enough: without the registry GTK 4 fails "Unable to register the application" and serves no windows. Launch to window over AT-SPI **83.8 ms** p50 (X11 viewable 71.6 ms, first AT-SPI event 71.4 ms, `ui-ready` 136.1 ms; n = 20). |
| 3 | Window, title, close | By pid: the **accessibility bus daemon** gives each connection's pid (`GetConnectionUnixProcessID`), so the app is found without calling any app (a stopped one included). The window is the application root's child with role frame; its `Name` is the title ("BridgeProbe", 20 of 20). **GTK 4 windows offer AT-SPI actions `window.close`, `window.minimize`, `window.toggle-maximized`, `default.activate`.** `DoAction(window.close)` goes through `close-request` exactly as `WM_DELETE_WINDOW` does (same host log, the page guard keeps the window for both): close to exit 46.2 ms p50 (WM_DELETE_WINDOW 34.3 ms). No X11 needed to close. |
| 4 | `GtkAlertDialog` | Role **dialog**, modal, **empty window title**; texts in order (heading, detail), buttons in order. Found by polling 6.5 ms after it became viewable (p50; events arrive 1.7 ms *before* the map). Read in **0.4 ms** with one `Cache.GetItems` call (same content as walking, 4.9 ms in 22 calls). **Press**: `DoAction(click)` returns in 0.2 ms, but the process exits **255.7 ms** later (GTK's keyboard activation holds the button pressed about 250 ms before "clicked"); an XTEST click at the button's window extents: **45.1 ms**. Relaunch dialog 20 of 20 (exit 8 every time), hung-page dialog 20 of 20 ("Reload page" taken, then exit 0). |
| 5 | Kill with the parent | **.NET 11's `ProcessStartInfo.KillOnParentExit`** works on Linux: test process killed with SIGKILL at 0, 20, 100, 300 ms after the launch or after `ui-ready`: **0 processes left in 25 of 25 runs** (also when started from a thread that ended at once), the app's whole tree (8 processes: app, network process, 4 bwrap, xdg-dbus-proxy, web process) gone 0.3–34 ms after. A plain `Process.Start`: **8 left in 25 of 25**. With a session bus of the fixture's own (also `KillOnParentExit`): the bus, the activated at-spi launcher, registry, accessibility bus and portals all gone 5.8–28.6 ms after, 25 of 25. WebKit's processes follow the app on their own: killing only the app empties the tree in 7.0 ms p50. No process group, subreaper or pidfd needed. |
| 6 | NativeAOT | `dotnet publish` of the reader with the trim, AOT and single-file analyzers on: **0 warnings**, no suppressions, on every build of the spike (`results/reader-publish.log` is the last); 3,072,024 B, `ldd`: libc, libm only (GIO, X11, XTEST and libatspi load at first use). |

**Recommendation:** build `SparBlackBox`'s Linux automation on **AT-SPI over GDBus**, with no libatspi and no X11 for anything but real input. Give each launched app a **private session bus** (a `dbus-daemon --session` the fixture starts with `KillOnParentExit`, its address in the app's `DBUS_SESSION_BUS_ADDRESS`), get the accessibility bus from it, find windows **by pid through the bus daemon**, read them with **`Cache.GetItems`** (one call, falling back to walking), **close with the window's `window.close` action**, **press dialog buttons with `DoAction`** (no input, like UI Automation's Invoke on Windows; about 250 ms slower than a click, by GTK's design), and start the app with **`KillOnParentExit`**. Details under [Recommendation](#recommendation).

**Dependencies.** The CI image needs **`at-spi2-core`** beyond the window tests' packages: an `apt-get -s` simulation on an empty package status (`apt-closure.sh`, `results/apt-closure.log`) adds exactly three packages, **142.5 KB to download, 491 KB installed**: `at-spi2-core` 2.52.0 (55.3 KB / 177 KB), `at-spi2-common` (8.5 KB / 52 KB), `libatspi2.0-0t64` (78.7 KB / 262 KB; pulled by at-spi2-core, not used by the GDBus route). Everything else it needs (`dbus-daemon`, `libdbus-1-3`, `gsettings-desktop-schemas`, `libxtst6`, GLib) is already in the set for `libwebkitgtk-6.0-4 xvfb xauth dbus dbus-x11 fonts-dejavu-core`. The fixture itself loads `libgio-2.0.so.0` (GDBus), `libX11.so.6` and `libXtst.so.6` (real input only), all already there for GTK 4 and the window tests.

## 1. The route from C#

Two readers were written and run against the same app in the same session (`Reader/AtSpi/Bus.cs`, `Reader/AtSpi/Lib.cs`; `results/run_standard_env_*.log`):

| | GDBus (GIO) | libatspi |
|---|---|---|
| Library | `libgio-2.0.so.0`, in every GTK 4 install; Spar.Linux already binds GDBus for the Settings portal | `libatspi.so.0` from `libatspi2.0-0t64` (78.7 KB / 262 KB), **not** in WebKitGTK 6.0's dependency closure (`results/packages.log`); links libdbus-1 |
| Code in the spike | ~400 lines plus ~140 of imports (`Native/GLib.cs`): connection, a generic GVariant-to-managed converter, typed helpers for `Accessible`, `Action`, `Component`, `Text`, `Cache`, `Properties`, signals on a thread with its own `GMainContext` | ~160 lines for the subset: init, desktop, children, pid, name, role, action |
| Finding the app | asks the **bus daemon** for the pid of each name (`ListNames`, `GetConnectionUnixProcessID`), then the app's own root | walks the **registry's** list of applications (the desktop) and asks each its pid |
| Find by pid + title, p50/p95 (n = 20) | 1.2 / 1.5 ms | 0.8 / 1.2 ms |
| First call | 1.2 ms | `atspi_init` 1.6–1.8 ms, then 1.1 ms |
| Events | `g_dbus_connection_signal_subscribe` on any thread's context | `atspi_event_listener_register` and `atspi_event_main`, which runs the default main context |
| Variadic C | none (non-variadic `g_variant_new_*` and `g_variant_new_tuple`) | none for this subset |

Both need the same accessibility bus and registry (part 2). libatspi is a little faster for this read (it caches), but the difference is a fraction of a millisecond, and it adds a package, process-wide state (`atspi_init` once per process, its own cache), and the default main context for events. GDBus keeps every call explicit, needs nothing new, and its synchronous calls are safe from any thread (the black box's operations are async calls on the thread pool; none touches a window's UI thread, since the app is another process).

## 2. What makes GTK 4 apps visible

Runs: `results/run_standard_env_{default,gtk-a11y-none,no-at-bridge,events}.log`, `run_bare_env_{default,private}.log`, `run_none_env_private.log`.

| Session | Variant | Result |
|---|---|---|
| `dbus-run-session` (the distribution's session services) | default | The reader's `org.a11y.Bus.GetAddress` **activated `at-spi-bus-launcher`** (6.5–7.5 ms; 43–51 ms on the first run after the distribution started), which started the accessibility bus at `$XDG_RUNTIME_DIR/at-spi/bus_63`; the first call to `org.a11y.atspi.Registry` activated `at-spi2-registryd`. The app registered (the registry listed it), its window appeared 91.5 ms after the launch |
| same | `GTK_A11Y=none` | **Not visible at all** (no bus name in 10 s) |
| same | `NO_AT_BRIDGE=1` | No effect (it is the GTK 3 ATK bridge's switch) |
| same | events registered with the registry first (`RegisterEvent`) | No difference: GTK 4.14 sends its events without it (18 signals in either case) |
| a session with **no** service files (a system without at-spi2-core) | default | No accessibility bus: GTK warns "Unable to acquire the address of the accessibility bus … If you are attempting to run GTK without a11y support, GTK_A11Y should be set to 'none'", and the web process "Can't connect to a11y bus". The app runs normally (`ui-ready`) |
| same | an accessibility bus the reader started (`dbus-daemon` with its own configuration, `AT_SPI_BUS_ADDRESS` for the app) | GTK 4 reads `AT_SPI_BUS_ADDRESS` and connects (bus name at 82.9 ms, events sent), but "Gtk-CRITICAL: Unable to register the application: … org.a11y.atspi.Registry was not provided", and its root serves **no window** |
| no session bus at all | same | The same; and GIO **autolaunched a session bus** through `dbus-launch` (dbus-x11), which outlived the app until the Xvfb closed (seen once in the census) |

So: at-spi2-core is needed (the registry above all), activated through a private session bus whose configuration has the standard service directories (`dbus-run-session`'s and `dbus-daemon --session`'s do). The fixture must give the app a session bus always (or GIO starts one through `dbus-launch` that outlives the test), and must remove `GTK_A11Y` from what the app inherits. Replacing the registry with a stand-in served by the fixture was not tried; at 142.5 KB the package is cheaper.

The page is exposed too: under the window, WebKit's tree (the document "Bridge probe" and its nodes, through the web process's `xdg-dbus-proxy`) hangs off a filler (`results/tree-default.txt`), 11 nodes from the application root. Its roles and names are WebKit's platform mapping, as `spikes/window-automation-linux` found, and `GetRoleName` returned an empty string for WebKit's nodes.

**Time to visibility** (`results/run_standard_window_20.log`, n = 20, polling every 5 ms):

| From the launch | p50 | p95 | min–max |
|---|---:|---:|---:|
| First AT-SPI event of the window (`Window.activate`) | 71.4 ms | 75.1 ms | 69.3–79.9 |
| X11 viewable (`_NET_WM_PID`, map state) | 71.6 ms | 79.0 ms | 70.4–83.1 |
| Window found by polling AT-SPI (frame with a name) | 83.8 ms | 85.5 ms | 77.2–95.4 |
| `ui-ready` (the host's log) | 136.1 ms | 147.5 ms | 129.0–199.1 |

The window is on AT-SPI well before the page is ready, so the fixture's ready signal stays the log's `ui-ready` line, as on Windows.

## 3. The window: by pid, its title, its close

- **By pid.** The accessibility bus is a plain dbus-daemon, so `org.freedesktop.DBus.GetConnectionUnixProcessID` names each connection's process. The app's own name serves `/org/a11y/atspi/accessible/root` (role application, name = the app id, `ToolkitName` "GTK", `Version` "4.14.5"); its children are the top-level windows. The app's other connections (WebKit's proxy, the portal) are other processes. Asking the bus daemon first matters for the failed relaunch: the stopped first instance is never called (a call to it would wait for the timeout).
- **Title**: the frame's `Name` property, 0.4 ms p50 (one call), "BridgeProbe" in 20 of 20. Role numbers, not role names, identify it (GTK's `GetRoleName` says "application" for a window whose role is frame, 23).
- **Close**: the window implements `org.a11y.atspi.Action` with `window.close`, `window.minimize`, `window.toggle-maximized`, `default.activate` (`results/run_standard_env_default.log`). `DoAction(window.close)` returned in 0.3 ms, and the host logged the same `close requested (User)` as for `WM_DELETE_WINDOW`; with the probe's guard `guard-keep`, both closes left the app running and the host logged "close kept by the page's guard" (`results/run_standard_window_20.log`). Close to exit (n = 10 each): `window.close` 46.2 ms p50 (30.0–51.0), `WM_DELETE_WINDOW` 34.3 ms p50 (28.2–47.1). So the fixture's `CloseWindowAsync` needs no X11 (and works the same under Wayland); `tests/Spar.Tests`' `X11Close` can stay for X11-specific checks.

## 4. `GtkAlertDialog`: the failed relaunch and the hung page

The flow, as `src/Spar.Linux/SingleInstance.cs`, `StartupFailure.cs` and `docs/evidence/m1c-startup.md` describe it: a first instance is stopped with SIGSTOP; a second one finds the lock held, connects, gets no acknowledgement within 2 s, logs `Start-up problem NotResponding … Exit code 8.` and (Production does not honour the automated mode) shows `GtkAlertDialog` over no window, on a main loop of its own; Close ends it with exit 8. The first instance, resumed, logged that the stale relaunch "waited … ms, longer than its sender waits, was dropped" in 20 of 20, and closed with exit 0.

What AT-SPI shows (identical in every run):

```
[dialog] "" modal=True
texts   [BridgeProbe can't start | The app is already running but did not answer within 2 seconds. Wait a moment, then start it again, or end it in the system monitor.  Exit code 8. Log: …/logs/app.log]
buttons [Close]
```

and for the hung page (`BRIDGE_PROBE_PLAN=hang`, the window's own dialog over the main window):

```
[dialog] "" modal=True
texts   [BridgeProbe is not responding | The page has not answered for 15 seconds. You can wait for it, or reload it: …]
buttons [Wait | Reload page]
```

- The dialog's window has **no title** (GTK's alert dialog sets none); the heading is the first label. Windows' task dialog has the app's name as its title. A choice for the fixture: report the Linux title as empty, or as the app's title (see Recommendation).
- **Detection**: a new top-level window of the pid with role dialog (16) or alert (2). GTK sends `StateChanged` (`showing`, `visible`, `active`, `focused`) for the new window slightly before it is mapped; the registry also announces a new application (`ChildrenChanged` on its root), which is not a window yet. Polling every 5 ms found it soon after the map.
- **Reading**: walking (children, role, then name or text per label) took 22–24 calls; `org.a11y.atspi.Cache.GetItems` on the app returns every node with parent, index, child count, interfaces, name, role and states in **one** call, and gave the same texts and buttons in 40 of 40 (both series).
- **Pressing**: the button implements `Action` with one action, `click`. `DoAction(click)` returns at once (0.2 ms), but GTK's activation of a button keeps it pressed for about 250 ms before "clicked", so the answer arrives 255–258 ms after the call. An XTEST click at the button's centre takes 45 ms. The button's **screen extents are wrong** under GTK 4 on X11 (`(0, 0, 528, 34)`: GTK 4 knows no window position); its **window extents** plus the X11 window's origin (`XTranslateCoordinates`) were right in every XTEST run.

Numbers (`results/run_standard_relaunch_20.log`, `results/run_standard_hang_20.log`; DoAction on even runs, XTEST on odd runs):

| | Relaunch, n = 20 | Hang, n = 20 |
|---|---|---|
| Launch to dialog found (polling) | 2,091.9 ms p50, 2,098.6 p95 (2,074.1–2,100.3; the 2 s acknowledgement wait included) | 20,081.9 ms p50, 20,100.8 p95 (the page hangs after `ui-ready`; 15 s without an answer) |
| Host log line to dialog viewable (X11) | 58.0 ms p50, 63.3 p95 (the line is logged before GTK starts) | 4.5 ms p50, 5.2 p95 (`hung-page dialog shown` is logged as GTK shows it) |
| Viewable to found by polling | 6.5 ms p50, 6.8 p95 (−0.7–6.8) | −0.7 ms p50, −0.6 p95 (−0.8–8.9) |
| Viewable to first AT-SPI event | −1.7 ms p50, −0.4 p95 (before the map) | −4.7 ms p50, −4.2 p95 |
| Read by walking | 4.9 ms p50, 5.4 p95 (22 calls) | 5.2 ms p50, 6.0 p95 (24 calls) |
| Read from the cache (one call) | **0.4 ms** p50, 0.6 p95 | **0.5 ms** p50, 0.6 p95 |
| `DoAction(click)` to done (exit 8 / dialog gone) | 255.7 ms p50, 257.7 p95 (n = 10) | 262.3 ms p50, 264.0 p95 (n = 10) |
| XTEST click to done | 45.1 ms p50, 45.8 p95 (n = 10) | 52.8 ms p50, 75.3 p95 (n = 10) |
| Outcome | exit 8, 20 of 20 | "Reload page" in the host log, then exit 0 on close, 20 of 20 |

## 5. Kill with the parent

`results/run_standard_kill_*.log` (`kill`): a holder process (the reader, verb `holder`) launches the app, the run kills the holder with SIGKILL 0, 20, 100 or 300 ms after `Process.Start` returned, or 1 s after `ui-ready`, and counts every process whose environment carries the run's tag, for 3 s. 5 runs per cell; at the kill the app's tree had 8 processes (BridgeProbe, WebKitNetworkProcess, 4 bwrap, xdg-dbus-proxy, WebKitWebProcess) except right after the start.

| How the app was started | Runs with processes left after 3 s | All gone after the holder died, p50 per delay 0 / 20 / 100 / 300 / ready (max of all) |
|---|---|---|
| `Process.Start` | **25 of 25** (8 processes each) | never |
| `KillOnParentExit = true` (.NET 11) | **0 of 25** | 0.3 / 0.3 / 17.2 / 6.0 / 6.0 ms (33.6) |
| `KillOnParentExit`, started from a thread that ended right after | 0 of 25 | 0.3 / 0.3 / 22.1 / 5.9 / 5.9 ms (28.0) |
| `setpriv --pdeathsig KILL -- app` | 0 of 25 | 0.3 / 5.8 / 22.3 / 6.0 / 6.3 ms (22.5) |
| `setpriv`, started from a thread that ended | 0 of 25 | 0.3 / 0.3 / 22.6 / 6.0 / 6.0 ms (23.0) |
| A session bus of its own (`dbus-daemon --session`, `KillOnParentExit`), the accessibility bus and registry activated through it, then the app (`KillOnParentExit`) | 0 of 25 (5 processes at the kill before the app's tree was up, 15–17 after: 2–4 dbus-daemon, launcher, registry, portals, the app's 8) | 5.8 / 5.9 / 28.1 / 6.1 / 6.0 ms (28.6) |

In 3 of the 150 runs the kill at 0 or 20 ms came before the child had executed the app (no tagged process yet at the kill): with `Process.Start` the app then started anyway and stayed; with `setpriv` nothing was left.

- `KillOnParentExit` needs no launcher of Spar's own and was immune to the thread that started the process ending, which a raw `PR_SET_PDEATHSIG` is documented not to be (here `setpriv` from an ended thread did not die early either, so .NET evidently does not fork on the calling thread; not investigated).
- WebKit's processes die with the app: killing only the app (`dispose kill`, 10 runs) emptied the tree in 7.0 ms p50 (9.7 p95); `Kill(entireProcessTree: true)` 20.3 ms p50; SIGTERM 8.5 ms p50 (`results/run_standard_dispose_*_10.log`). The app's children share the **test process's process group and session**, so a group kill is not available without a `setsid` wrapper, and is not needed.
- `PR_SET_CHILD_SUBREAPER` on the test process: WebKit's bwrap and network processes were reparented to it for a few ms and then exited, but became **zombies of the test process** until it reaped them (one was still there at the next run). Not useful here, and a cost.
- `pidfd_open` works (fd returned): a race-free handle for waiting and signalling, not a kill-with-parent mechanism.
- **SIGTERM ends the Spar Linux host at once with exit 143** (10 of 10): no handler, no two-stage close. Outside this spike's question, but the black box's `EndSessionAsync` has no Linux counterpart to call yet.

## Recommendation

For `SparBlackBox` on Linux (the `IBlackBoxAutomation` and `IBlackBoxProcess` implementations):

1. **Process**: start the app with `ProcessStartInfo.KillOnParentExit = true`, with its environment as on Windows plus `DISPLAY` (the private Xvfb), `GDK_BACKEND=x11`, private `HOME`/`XDG_*` (runtime folder under `/dev/shm`), its own `DBUS_SESSION_BUS_ADDRESS`, and without `GTK_A11Y`, `AT_SPI_BUS_ADDRESS` and `NO_AT_BRIDGE` (add them to the removed variables). Dispose: `Kill(entireProcessTree: true)` after the close timeout; nothing else is needed for WebKit's processes. `RunningProcesses`/`ProcessIds` can walk `/proc` from the app's pid (the job object's counterpart).
2. **Session bus**: one `dbus-daemon --session --print-address=1 --nofork --nopidfile` per launched app (or per fixture), also `KillOnParentExit`. It activates at-spi2-core's launcher and registry, the portals WebKit asks for, and takes them all down with it.
3. **Accessibility client**: GDBus through `LibraryImport` (`libgio-2.0.so.0`), one connection to the address `org.a11y.Bus.GetAddress` returns, one signal thread with its own main context. No libatspi.
4. **Windows by pid**: `ListNames` + `GetConnectionUnixProcessID` on the accessibility bus, then `GetChildren` on `/org/a11y/atspi/accessible/root` of the app's name. The main window: role frame (23), its `Name` the title. Poll every 5–10 ms until the deadline, woken early by the window's events; events are not needed for correctness. A node that vanishes between two calls answers `UnknownMethod` ("Object does not exist at path"): that means gone, never an error. Each call has a timeout (the spike used 2 s) and never targets a name before its pid is checked.
5. **Close**: `Action.DoAction` on the window's `window.close` (looked up by name in `GetActions`). It is the window manager's close as far as Spar's host is concerned. `WM_DELETE_WINDOW` stays available for X11-only checks.
6. **Dialogs**: a top-level of the pid with role dialog (16) or alert (2), read with `Cache.GetItems` (one call; walk only if the toolkit serves no cache). `Texts`: the labels in order (heading, then detail); `Buttons`: the push buttons in order. `Title`: empty from GTK; decide whether `SparNativeDialog.Title` reports it empty on Linux or substitutes the app's title (the Windows host's task dialogs carry the app's name), and pin it in the shared suite.
7. **Press**: `DoAction(click)`, the counterpart of UI Automation's Invoke (no input), with GTK's ~250 ms activation in the latency budget. The internal real-input click (`ClickAsync`) uses XTEST at the button's **window** extents plus the X11 window origin, never its screen extents.
8. **Prerequisites**: no `org.a11y.Bus` on the session bus (at-spi2-core missing) is the black box's explicit skip with that message, and a failure when `CI=true`, like the other prerequisites (decision "M1c step 7's shape"). The CI image adds `at-spi2-core`.

## Not verified

- **Wayland** (only X11 under Xvfb). AT-SPI and `window.close` do not depend on X11; XTEST does.
- A real desktop session (GNOME, KDE): their own launcher and registry, screen readers attached, other apps on the bus.
- **The Notebook sample** (the probe only), the close confirmation "Close anyway / Keep open" (the probe has no guard that answers `{ ask }`), and the shutdown prompt.
- **The rest of `IBlackBoxAutomation`**: `FindAsync`/`InvokeAsync`/`ValueAsync` on page elements (WebKit's tree is reachable under the window, with platform roles and names that differ from ARIA's, and empty role names), `BoundsAsync`, `FocusAsync`, `WindowPartsAsync`, `TopLevelWindowsAsync` (context menus), `DumpAsync`, and `EndSessionAsync` (no Linux counterpart; the host has no SIGTERM handler).
- A registry stand-in served by the fixture (to drop at-spi2-core): not tried.
- Why `setpriv --pdeathsig` started from an ended thread survived (how .NET 11 forks); `KillOnParentExit`'s own implementation was not read.
- CI containers (Docker was not used); the package delta is an `apt-get -s` simulation, not an installed image.
- GTK versions other than 4.14.5 (`Cache.GetItems`, the window actions and the activation delay are GTK's behaviour and may change).

## Problems met

- `[assembly: SupportedOSPlatform]` placed before the `using` directives failed the build (CS1529) while the publish step's `tee` hid the exit code, so one run used the previous binary; the step now cleans `bin`, `obj` and the output first.
- `ProcessStartInfo.KillOnParentExit` is marked supported on Linux, Windows and Android only (CA1416); the reader declares `SupportedOSPlatform("linux")` for its assembly.
- The first dialog-event filter waited for `showing 1`; GTK 4.14 sent `StateChanged showing` with detail 0 for the new dialog, plus `active 1`. The filter now takes any `showing`, `visible` or `active` change or a window event of the app (`results/run_standard_relaunch_1.log` has the raw signals).
- A no-session-bus run left GIO's `dbus-launch` autolaunch alive until the Xvfb closed.
- **A window can go between two calls**: the first hang series stopped at run 5 with "Object does not exist at path" from `GetRole` on a dialog that `GetChildren` had listed a moment before, while the reader waited for the dialog to go (`results/run_standard_hang_20-first-attempt.log`). Every read of the black box must treat that error (`org.freedesktop.DBus.Error.UnknownMethod` on a vanished path) as "gone", not as a failure. `Reading.WindowsOf` now does; the hang series was run again with it. The window, relaunch, kill and dispose series ran with the binary before this change (it only touches that catch).

## Files

| Path | What |
|---|---|
| `Reader/Reader.csproj`, `Directory.Build.props` | The NativeAOT reader (analyzers on, warnings counted, not fatal) |
| `Reader/AtSpi/Bus.cs` | AT-SPI over GDBus: connection, calls, GVariant conversion, signals |
| `Reader/AtSpi/Lib.cs` | The libatspi comparison |
| `Reader/AtSpi/Reading.cs` | Windows by pid, dialogs by walking and from the cache, tree dump |
| `Reader/Native/` | GLib/GIO, X11 (windows by `_NET_WM_PID`, `WM_DELETE_WINDOW`, XTEST), libc |
| `Reader/Runs/` | The verbs: `env`, `window`, `relaunch`, `hang`, `kill`/`holder`, `dispose` |
| `wsl.sh`, `series.sh`, `apt-closure.sh` | The runs (steps `sync client probe reader packages`, then `series.sh`, then `collect`) |
| `results/` | Raw logs (redacted), the trees, the package lists |

## Reproduce

```bash
# From Git Bash at the repository's root; Xvfb from docs/evidence/m1c-host/wsl.sh tools.
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/linux-blackbox-atspi/wsl.sh sync packages client probe reader
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/linux-blackbox-atspi/series.sh
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/linux-blackbox-atspi/apt-closure.sh
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/linux-blackbox-atspi/wsl.sh collect
```

# Why the Notebook's Linux launch grew: the desktop portal started inside the launch

Recorded 2026-11-21. Decision "The Notebook's Linux launch: the portal started inside the launch (implementation record; 2026-11-21)". It follows `m1c-exit.md`, where the Notebook reached `ui-ready` in 306–311 ms from the process's start (210–228 ms in steps 1–6), 391.5 ms on CI's runner, and WindowHost in 213–222 ms.

Machine and rules as in `m1c-exit.md`: WSL 2 Ubuntu 24.04.5, WebKitGTK 2.52.6, GTK 4.14.5, at-spi2-core 2.52.0, xdg-desktop-portal 1.18.4 with xdg-desktop-portal-gtk 1.15.1 and xdg-dbus-proxy 0.1.5 as the distribution installs them (nothing installed for this); a private Xvfb per condition on an abstract socket, `DISPLAY` and `WAYLAND_DISPLAY` unset, the core limit 1 byte.

## Method

The runner is [`m1c-notebook-launch/wsl.sh`](m1c-notebook-launch/wsl.sh). It launches a Production NativeAOT executable directly (not through the black box), each launch with a new data root and runtime folder, as both the old launcher and the black box's Notebook test did. It reads the host's own log: the process's start ("Host started … after the process") plus `ui-ready`, and the phase lines. One warm-up round, then 10 rounds of one launch per condition, in an order that rotates every round. [`summary.py`](m1c-notebook-launch/summary.py) gives medians and ranges without the warm-up.

| Condition | The launch's D-Bus session |
|---|---|
| a | none (`DBUS_SESSION_BUS_ADDRESS` unset), as the launcher of steps 1–6 ran the apps; GLib autolaunches a bus through `dbus-launch`, which outlives the launch, so the condition keeps one runtime folder for all its launches |
| b | a private `dbus-daemon` with no `DISPLAY`, nothing started first |
| c | the black box at `9e675cc`: (b) with `org.a11y.Bus` and the registry started first (`LinuxAppProcess.Start`, `LinuxDesktop.StartAccessibility`) |
| d | (c) with `GTK_A11Y=none` |
| e | (c) with `WEBKIT_A11Y_BUS_ADDRESS` set empty, WebKitGTK 2.52's only switch here: the UI process takes the variable in place of asking for the bus (`UIProcess/glib/WebProcessPoolGLib.cpp`), and the web process connects nowhere for an empty address (`AccessibilityAtspi::connect` returns) |
| f | (c) with `xdg-desktop-portal` also started first, as a desktop session has it running |
| g | (c) on a bus whose only activatable service is `org.a11y.Bus`, so no portal can start (as in the CI image, whose package list has none) |

The apps: today's Notebook (5,141,248 B, from `9e675cc`), the Notebook of `97d26c5` (M1c step 6, the commit that recorded 228.3 ms, built from `git archive` in `/var/tmp`, 5,086,816 B), and today's WindowHost.

## Results

Milliseconds from the host's start (the process's start for the total), median (range), n = 10 per cell ([`summary.txt`](m1c-notebook-launch/summary.txt)):

| App | Condition | Total | Window shown | Page committed | First bridge message | `ui-ready` |
|---|---|---|---|---|---|---|
| Notebook, today | a | **227.2** (219.4–311.8) | 82.5 | 126.7 | 152.8 | 221.2 |
| | b | 314.6 (286.7–634.6) | 81.3 | 209.2 | 237.1 | 306.1 |
| | c | **295.6** (290.5–300.5); run 1: 301.8 | 70.9 | 192.0 | 218.8 | 285.4 |
| | d | 308.9 (284.0–352.0) | 73.6 | 202.5 | 230.3 | 299.4 |
| | e | 317.0 (288.2–330.8) | 75.2 | 202.1 | 230.8 | 305.1 |
| | f | **219.9** (215.2–233.6); run 1: 230.0 | 70.9 | 117.3 | 143.7 | 211.4 |
| | g | 227.5 (210.8–256.3) | 74.9 | 122.5 | 150.2 | 219.0 |
| Notebook, step 6 (`97d26c5`) | a | **229.6** (219.8–243.3) | 83.2 | 127.9 | 154.6 | 220.5 |
| | c | **291.9** (289.6–304.1); run 1: 302.4 | 70.5 | 189.6 | 216.8 | 285.8 |
| | f | 223.5 (210.9–230.0); run 1: 226.9 | 71.8 | 118.0 | 145.1 | 215.4 |
| | g | 224.8 (208.8–228.6) | 72.2 | 120.4 | 147.9 | 215.8 |
| WindowHost, today | a | **156.1** (152.6–164.0) | 81.2 | 124.6 | 147.9 | 148.6 |
| | c | **225.1** (218.2–240.6); run 1: 230.8 | 72.5 | 192.7 | 216.7 | 217.4 |
| | f | 146.9 (139.9–155.8); run 1: 153.1 | 69.8 | 115.8 | 139.1 | 139.8 |
| | g | 149.9 (145.8–164.7) | 72.3 | 117.1 | 140.7 | 141.6 |

Run 1 had all seven conditions on today's Notebook and c, f, g on the others; run 2 had a, c and f on all three. Run 1's condition a is left out: its driver deleted each launch's runtime folder, where the accessibility bus that the first launch's autolaunched session started kept its socket, so later launches' web processes could not bind it (`bwrap: Can't find source path …/at-spi`) and the app ended with exit 133 before `ui-ready` (that is not how the old launcher ran: it kept the runner's runtime folder). Run 2 keeps condition a's runtime folder.

What the numbers show:
- **Code is not the cause.** The step-6 Notebook and today's are equal in every condition: a 229.6 against 227.2 ms, c 291.9 against 295.6 ms, f 223.5 against 219.9 ms.
- **The phase that grew is from the window shown to the page committed:** about 45 ms in a, f and g, about 120 ms in b to e. Everything before the window is the same or faster with a bus, and everything after the commit takes the same time. WindowHost grew by the same amount (156 → 225 ms in a → c); its total is lower because its page reports ready with its first bridge message, while the Notebook's own work after that takes about 70 ms more.
- **Accessibility is not the cause.** With GTK's accessibility off (d) the registry listed 0 applications instead of 1, and with WebKit's off (e; by its source the web process then connects to no accessibility bus, not observed) the registry still listed the app's GTK side; both stayed at c's time (308.9 and 317.0 ms against 301.8 ms in the same run). The black box's change of `68db043` (the registry started first) did not add time either: c is faster than b (301.8 against 314.6 ms, run 1).
- **The cause is the desktop portal started inside the launch.** In b to e, every launch's private bus starts `xdg-desktop-portal` after the window appears (10 of 10 launches each); in f it is already running, in g it cannot start, and in a no portal was started in any launch (why the autolaunched bus never started one was not checked). f and g both remove the 75 ms.

## Why the portal costs 75 ms

A bus monitor on one c launch ([`monitor-c.bus.txt`](m1c-notebook-launch/monitor-c.bus.txt), with its [`app.log`](m1c-notebook-launch/monitor-c.app.log); the monitor itself slows the launch) shows the sequence, with times from the host's start:
- about 40–50 ms: GTK's application asks for `org.gnome.SessionManager` (absent); about 63 ms: Spar's theme reads the Settings portal without starting it (`NameHasNoOwner`, as designed);
- about 118 ms, after the window is shown: WebKit's sandbox proxy (`xdg-dbus-proxy`, the connection of the sandboxed process) calls `StartServiceByName` for `org.freedesktop.portal.Desktop`;
- `xdg-desktop-portal` starts, starts the document portal and the permission store, and tries 15 times to start its GTK backend (`org.freedesktop.impl.portal.desktop.gtk`), which exits with status 1 each time because the bus gives it no display; only then does it take its name, about 155 ms after the request;
- right after: `GetAll` of `org.freedesktop.portal.PowerProfileMonitor`, then `org.freedesktop.portal.Realtime.MakeThreadRealtimeWithPID` for PID 2 (the sandboxed process's own PID namespace), which fails ("ServiceUnknown: org.freedesktop.RealtimeKit1").

WebKit's source explains why this blocks: `WTF::RealTimeThreads` (`Source/WTF/wtf/linux/RealTimeThreads.cpp`, WebKitGTK 2.52.6) promotes a thread to real time on the main thread; when `sched_setscheduler` is refused it creates the portal's proxy with `g_dbus_proxy_new_for_bus_sync`, without `G_DBUS_PROXY_FLAGS_DO_NOT_AUTO_START`, so the call starts the portal and waits for it, then calls `MakeThreadRealtimeWithPID` synchronously (20 ms timeout). A sandboxed WebKit process waits while the page loads.

On a GNOME or KDE desktop the portal is already running when an app starts, so the request costs one call; condition f is that footing. The black box gave every launch a new bus with nothing but accessibility running, so every measured launch paid the portal's start, here with its backend failing for want of a display. Steps 1–6 never paid it (condition a). The CI image installs no portal, so the bus refuses the name at once (condition g's footing): CI's 391.5 ms for the Notebook is not this cause, and stays unexplained beyond the runner's general slowness (its window test took 301.7 ms against 189 ms here).

## The fix (the black box) and its before/after

`src/Spar.Testing/BlackBox/Linux/LinuxDesktop.cs`: the launch's bus now also starts `org.freedesktop.portal.Desktop` before the app (`StartPortal`, after `StartAccessibility`), as a desktop session has it running; a bus with no portal service refuses at once and a portal that fails to start is left to the app, both without failing the launch. Spar's host is unchanged: the wait is WebKit's, and on a desktop the portal is running. Accessibility stays on.

| Measure (black-box fixture, WSL) | Before (`m1c-exit.md`) | After |
|---|---|---|
| `BudgetTests`, WindowHost launch alone, profile reused, from the process's start (n = 10, two runs) | 217.1 ms (205.5–228.6), 222.5 ms (211.7–229.6) | **146.2 ms (136.9–161.9), 153.1 ms (141.2–173.4)** |
| The Notebook's Linux test, from the process's start (n = 11) | 305.7–311.2 ms (four runs) | **226.2 ms (213.5–283.5)** |
| The same test by its own clock, from before the fixture starts the launch's buses | 281.6–316.9 ms | 320.9 ms (291.9–365.6): it now includes the portal's start too, which is not the app's time |
| PSS of WindowHost and its WebKit processes | 200.0 MB | 200.0 MB |

Raw: [`after-fix-launch.log`](m1c-notebook-launch/after-fix-launch.log), [`after-fix-notebook.log`](m1c-notebook-launch/after-fix-notebook.log). The black box's Linux tests after the change, on the Production probe as CI's black-box step selects them: 82, 77 passed, 5 skipped (the runner's optional executables and the flood measurement), 0 failed, 0 processes and 0 mounts left ([`after-fix-blackbox.log`](m1c-notebook-launch/after-fix-blackbox.log)); the Notebook's first-frame theme test with the Notebook named: passed. The fixture costs each launch about 80 ms more before the app starts in WSL (the portal's start: condition f's 87–91 ms before the app, of which about 13 ms is accessibility), nothing where no portal is installed.

**The 350 ms budget on a real-desktop footing:** met with room. The Notebook takes about 220 ms (condition f, new data root), WindowHost about 147 ms (f) and 146–153 ms through the fixed fixture with its profile reused. The M1c exit's recorded 213–222 ms for WindowHost included about 70 ms of the portal's start.

## Also seen

- The document portal mounts a FUSE file system at `$XDG_RUNTIME_DIR/doc`. Ended with `SIGKILL` together with its bus, it left its mount behind (this runner's first smoke run, 4 mounts, unmounted with `fusermount3 -u`); ended by its bus going away first, it unmounts itself. The black box ends the bus's tree and left no mount in any run here; one run left an empty `doc` folder in its runtime folder, which the next test process deleted, as the fixture does with a gone process's folders.
- With condition a's first-launch accessibility bus gone, WebKitGTK 2.52 did not start a web process (`bwrap` could not bind the bus's socket) and the app ended by `SIGTRAP` (exit 133) instead of reporting a web-process failure. Outside Spar's control; no core was written.

## Not verified

- A real GNOME or KDE session (the portal running with its real backend), where the request should cost one call; f is the stand-in.
- Which sandboxed process (web or network) made the request: the monitor shows WebKit's proxy and PID 2 of a sandbox namespace, and both processes run sandboxed.
- The cause of CI's 391.5 ms, and a CI run with the fixture change.
- The fixture change on Windows: built (0 warnings), Windows is not affected (Linux-only code).

## Files of this record

| Path | What |
|---|---|
| `m1c-notebook-launch/wsl.sh`, `summary.py` | The runner and the summary |
| `m1c-notebook-launch/run1-ab-*.log`, `run2-ab-*.log`, `summary.txt` | Every launch's line (phases, the processes of the launch, the registry's application count) and the summary |
| `m1c-notebook-launch/monitor-c.bus.txt`, `monitor-c.app.log`, `monitor-b.app.log` | The bus monitor of one c launch, and the logs of that run |
| `m1c-notebook-launch/old-build.log`, `new-build.log` | The two Notebooks' builds |
| `m1c-notebook-launch/after-fix-*.log` | The fixture after the change: the launch budget, the Notebook's test, the black-box tests |

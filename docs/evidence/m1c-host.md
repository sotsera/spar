# M1c: the Linux host, first slice

Recorded 2026-11-19. What it shows: Spar's Linux host (`src/Spar.Linux`, GTK 4 and WebKitGTK 6.0) running the bridge probe and the Notebook sample in real windows under an Xvfb of the run's own: the page served on `spar://app`, calls through the relay in the isolated script world, a second document with its own bridge session, a frame of another origin that cannot reach the bridge, the close through the window manager's close request with exit 0 and no WebKit process left, and recovery from a lost web process. Plan M1c, build step 1; decision "The Linux host, first slice (implementation record; choices for review)".

## Environment

- **Linux:** WSL2 `Ubuntu-24.04` (24.04.5 LTS, kernel 6.18.58.1-microsoft-standard-WSL2), the normal user, no root; `libwebkitgtk-6.0-4` 2.52.6, `libgtk-4-1` 4.14.5, `bubblewrap` 0.9.0 (the web-process sandbox on), SDK 11.0.100 from eng/wsl's shared cache, Node 24.22.3. Xvfb 21 unpacked from Ubuntu's packages (no root; run in a bubblewrap mount namespace so it finds the unpacked `xkbcomp`), listening on its abstract socket only; Mesa's llvmpipe (no DRI3 under Xvfb). A private D-Bus session (`dbus-run-session`) per test run. Never WSLg's display: `DISPLAY` and `WAYLAND_DISPLAY` are unset and each app gets the Xvfb display explicitly.
- **Isolation:** each app run has its own `HOME` and `XDG_*` folders under the working copy's `artifacts/linux-homes/host-tests/<run>/`; `XDG_RUNTIME_DIR` in `/dev/shm/spar-m1c-host` (WebKit's sandbox refuses one under `/var/tmp`).
- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, the same tree.
- **Working copy:** the Windows tree as it was, uncommitted changes included, copied to `/var/tmp/spar-m1c-host/repo` without `bin`, `obj`, `node_modules`, `artifacts` and `spikes` (`wsl.sh sync`). The GPU and CSP spike that ran in WSL meanwhile used its own folders; nothing of it was touched.

## Commands

All Linux steps through [`m1c-host/wsl.sh`](m1c-host/wsl.sh), from Git Bash at the repository's root:

```
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-host/wsl.sh tools sync client appmodel build test probe hosttests probeaot notebook
```

| Step | What it runs |
|---|---|
| `client` | `npm ci`, `npm run check` (TypeScript 6 and 7), `npm test`, `npm run build` in `src/Spar.Client` |
| `build`, `test` | `dotnet build Spar.slnx -c Debug`; the solution's tests without the `Window` and `BlackBox` surfaces, with no display |
| `probe`, `hosttests` | the bridge probe as a Development build with its page (`-p:SparDevServer=false`), then `Spar.Tests`' `LinuxHostTests` and `LinuxHostUnitTests` with `SPAR_LINUX_HOST_APP` and `SPAR_LINUX_TEST_DISPLAY` |
| `probeaot` | the probe published as a Production NativeAOT executable, and the three host tests that need no automated mode against it |
| `notebook` | `dotnet publish samples/Notebook -c Release -r linux-x64` (both hosts in the build, as by default) and `The_Notebook_starts_and_closes_and_its_numbers_are_recorded`, twice: as built, then with `GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1`, the settings step 2's software-GPU default will make |

On Windows: `dotnet build Spar.slnx -c Debug`, `dotnet test --solution Spar.slnx -c Debug --no-build --filter-not-trait "Surface=Window" --filter-not-trait "Surface=BlackBox"`, `dotnet run eng/check-size.cs`, `dotnet test --project tests/Spar.WindowTests -c Debug --no-build`.

## Results (observed)

| Check | Result |
|---|---|
| Windows build | 0 warnings, 0 errors (warnings as errors, trim and AOT analyzers on) |
| Windows solution tests | 892: 869 passed, 23 skipped, 0 failed (the four `LinuxHostTests` skip: "The Linux host runs on Linux only") |
| Windows window suite | 118: 106 passed, 12 skipped (Sandbox-tagged), 0 failed, 78 s |
| Size check | 479 source files, 0 errors, 0 warnings |
| SDK | Vitest 262 of 262 (15 files; 10 new in `test/webkit.test.ts`), `npm run check` clean, on both OSes |
| Linux build | `dotnet build Spar.slnx -c Debug`: 0 errors, 0 warnings, 40 s |
| Linux solution tests (no display) | 892: 846 passed, 46 skipped, 0 failed |
| Linux host tests, Development probe (CoreCLR) | 25 passed, 1 skipped (the Notebook measurement, which needs `SPAR_LINUX_NOTEBOOK`): the four real-window tests and the unit tests, 8 s in all |
| Linux host tests, Production NativeAOT probe | 3 of 3 (calls, navigation and close; the foreign frame; recovery) |
| Notebook NativeAOT, linux-x64 | one executable of **4,676,160 B** (budget ≤ 8 MB); `ldd`: `libc`, `libm` only; no `SparWindowsHost`, `WebView2Loader` or `CreateCoreWebView2Environment` in it, `SparLinuxHost` and the WebKit imports present |

**Start-up and close** ([`linux-host-default.txt`](m1c-host/linux-host-default.txt), [`linux-host-software.txt`](m1c-host/linux-host-software.txt); launch to `ui-ready` from the test's clock at `Process.Start` to the time of the host's ready line; one run discarded, then 11):

| Notebook, NativeAOT, Xvfb, llvmpipe | Launch to `ui-ready` (budget ≤ 350 ms) | Close request to exit, from outside | Total PSS, app and WebKit processes |
|---|---|---|---|
| As built: GTK's GL renderer, WebKit's acceleration | median 577.0 and 581.6 ms in two runs of 11 (548–1,620) | median 540.1 and 530.8 ms | 503–504 MB |
| `GSK_RENDERER=cairo`, `WEBKIT_DISABLE_DMABUF_RENDERER=1` (step 2's default for a software GPU) | median **211.7** and **210.4 ms** (192–220) | median 34.5 and 33.0 ms | 233 MB |

Where the time goes ([`notebook-default.log`](m1c-host/notebook-default.log), [`notebook-software.log`](m1c-host/notebook-software.log)): as built, `gtk_window_present` of the first window holds the main loop about 320 ms (the stall probe's warning, "outside Spar's callbacks" before the start was named), so the window is shown at about 390 ms instead of 58–66 ms; and after the window is destroyed the WebKit processes take 510–545 ms to exit instead of 17–27 ms. Phases with the software settings, from the host's start: services built 2.5 ms, GTK ready 34 ms, view created 50 ms, window shown 58 ms, page committed 103 ms, first bridge message 128 ms, `ui-ready` 191 ms; the host started 4–7 ms after the process (the kernel's process start time has 10 ms steps).

**Round trip:** the probe's page times 1,000 sequential `echo` calls after 50 warm ones: mean **0.263 and 0.231 ms** in two runs on the Production NativeAOT probe, 0.300 ms on the Development CoreCLR probe (budget ≤ 1 ms; WebKitGTK's `performance.now()` has 1 ms steps, so the mean of many is reported, not a median).

**The bridge in a real window** ([`probe-aot-calls-navigation-close.log`](m1c-host/probe-aot-calls-navigation-close.log)):
- the page's own world sees neither the handler nor the relay: `probe note: main world: handler undefined, relay undefined`;
- a navigation to `/other.html` closes the first document (`closed (navigation)`) and opens a second with its own id, which reports `ui-ready` and answers its call;
- the close: `close requested (User)` from a `WM_DELETE_WINDOW` client message, the backend's guard, the page's close work (2.4 ms), calls stopped, the window destroyed, `web processes exited after 19–49 ms`, services disposed, exit 0; every WebKit process the test saw before the close was gone.

**A frame of another origin** ([`probe-aot-foreign-frame.log`](m1c-host/probe-aot-foreign-frame.log)): the probe embeds `http://127.0.0.1:<port>/frame.html`, served by the test (`csp.FrameSrc("http://127.0.0.1:*")` in the probe). The frame's script reported to its own server: `window.webkit.messageHandlers.spar` absent; `parent.dispatchEvent(new CustomEvent("spar:send", …))` threw (cross-origin access to the top window); `top.postMessage(<a call>, "*")` sent, and ignored (neither the SDK nor the relay listens to `message` events); its own window's `spar:send` reached nothing; `typeof sparRelay` undefined. The app's log has no note from the frame, no dropped or refused bridge message, and one document only.

**Recovery** ([`notebook-web-process-lost.log`](m1c-host/notebook-web-process-lost.log)): with the page ready, the test kills the `WebKitWebProcess` below the app (SIGKILL). `web-process-terminated` (crashed) closes the document, the page is reloaded once, and a new document reports `ui-ready` 102 ms after the loss in the Notebook; no command was replayed, and the window then closes with exit 0. A second loss before the reloaded page is ready ends the app with exit 5 and "The page stopped working again right after it was reloaded." (Development probe, automated mode, so no dialog).

## Not verified

- A real desktop: GNOME, KDE, Wayland (the host ran on X11 under Xvfb only), a real GPU, HiDPI, IME; and the "has to close" and "Close anyway / Keep open" dialogs, which were never shown (automated mode, and no backend guard that asks).
- The session end (logind), the page's close guard (`close-request`, step 3 with the heartbeat), and a hung web process at close (step 3).
- Service workers on `spar://app`, shared workers, and the scheme's back-pressure (M3's streams).
- The start-up checks and exit codes other than 6 (no display) and 70 (the window or the data folder), single instance (step 2): a second instance on the same folders still starts.
- Two windows in one process: the scheme is registered on WebKit's default context, once per process.

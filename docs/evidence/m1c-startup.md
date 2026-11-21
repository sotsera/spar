# M1c: Linux start-up

Recorded 2026-11-19. What it shows: the Linux host's start-up (`src/Spar.Linux`) on the bridge probe and the Notebook sample under an Xvfb of the run's own: every fixed exit code forced and observed with its message, single instance through the lock file and the Unix socket (a hand-over and a stopped first instance), the software-GPU default in the web processes' environment with `HardwareAcceleration` `Auto`, `Always` and `Never`, and the theme from the window's first frame. Plan M1c, build step 2; decision "Linux start-up: prerequisites, single instance, theme and the software-GPU default (implementation record; choices for review)".

## Environment

- **Linux:** WSL2 `Ubuntu-24.04` (kernel 6.18.58.1-microsoft-standard-WSL2), the normal user, no root, not in the `render` group (so `/dev/dri/renderD128`, WSL's vgem node, cannot be opened); `libwebkitgtk-6.0-4` 2.52.6, `libgtk-4-1` 4.14.5, `bubblewrap` 0.9.0, Mesa llvmpipe (LLVM 20.1.2), SDK 11.0.100. The Xvfb of `docs/evidence/m1c-host/wsl.sh tools`, on its abstract socket only; a private D-Bus session per test run (`dbus-run-session`), whose `xdg-desktop-portal` has no Settings interface (its GTK backend fails without a display: the repeated "Activated service … failed" lines in the logs). Never WSLg's display.
- **Isolation:** each app run has its own `HOME` and `XDG_*` folders under the working copy's `artifacts/linux-homes/host-tests/<run>/`; `XDG_RUNTIME_DIR` is `/dev/shm/spar-m1c-startup`. A relaunch shares its first instance's folders.
- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, the same tree.
- **Working copy:** the Windows tree as it was, uncommitted changes included, copied to `/var/tmp/spar-m1c-startup/repo` (`wsl.sh sync`).
- **Not used:** containers. Docker Desktop was not running, so the missing pieces were hidden inside bubblewrap mount namespaces in WSL instead (below), which needs no root.

## Commands

All Linux steps through [`m1c-startup/wsl.sh`](m1c-startup/wsl.sh), from Git Bash at the repository's root (after `docs/evidence/m1c-host/wsl.sh tools` once, for the Xvfb):

```
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-startup/wsl.sh sync client appmodel build test probe startup probeaot notebook egl
```

| Step | What it runs |
|---|---|
| `client`, `appmodel`, `build`, `test` | as in step 1: the TypeScript SDK's checks and tests, `dotnet build Spar.slnx -c Debug`, the solution's tests without the `Window` and `BlackBox` surfaces |
| `probe`, `startup` | the bridge probe as a Development build with its page, then `LinuxStartupTests`, `LinuxStartupUnitTests`, `LinuxHostTests` and `LinuxHostUnitTests` against it (and against the Notebook of an earlier `notebook` step) |
| `probeaot` | the same tests against the probe published as a Production NativeAOT executable; the two that need the Development build's automated mode or variables skip |
| `notebook` | the Notebook published (Release, NativeAOT), its numbers (one run discarded, then 11) and its first frames in light and dark |
| `egl` | the EGL half of the GPU check in the test process, as the system has it and with `GALLIUM_DRIVER=d3d12` |

How each path is forced (`LinuxStartupTests`): no display, `DISPLAY` and `WAYLAND_DISPLAY` removed; a display nobody serves, `DISPLAY=:59`; a missing library, `bwrap --dev-bind / / --ro-bind /dev/null <the library's file> -- <app>`; missing helpers, `--tmpfs /usr/lib/x86_64-linux-gnu/webkitgtk-6.0`; a missing `bwrap`, `--ro-bind /dev/null /usr/bin/bwrap`; a sandbox the system denies, `--unshare-user --disable-userns` (no further user namespaces inside, as a container's seccomp profile or AppArmor's restriction would); "too old", `SPAR_DEV_REQUIRE_WEBKITGTK=99.0` in a Development build; a stopped first instance, `SIGSTOP`. Each message is waited for as a viewable window of the app's process, then closed with a `WM_DELETE_WINDOW` client message.

On Windows: `dotnet build Spar.slnx -c Debug`, the solution's tests as above, `dotnet test --project tests/Spar.WindowTests -c Debug --no-build`, `dotnet run eng/check-size.cs`, `npm run check` and `npm test` in `src/Spar.Client`.

## Results (observed)

| Check | Result |
|---|---|
| Windows build | 0 warnings, 0 errors |
| Windows solution tests | 960: 915 passed, 45 skipped, 0 failed (the static-state list and `tests/AppModel.Tests`, 11 of 11, among them) |
| Windows window suite | 118: 106 passed, 12 skipped (Sandbox-tagged), 0 failed, 78 s |
| Size check | 493 source files, 0 errors, 0 warnings |
| SDK | Vitest 262 of 262, `npm run check` clean, on both OSes |
| Linux build | 0 warnings, 0 errors |
| Linux solution tests (no display) | 960: 897 passed, 63 skipped, 0 failed |
| Start-up and host tests, Development probe (CoreCLR) | 94 of 94, the Notebook's included |
| Start-up and host tests, Production NativeAOT probe | 90 passed, 4 skipped (the raised minimum and the automated exit 5 need a Development build; the two Notebook tests ran in their own step) |
| Notebook (Release, NativeAOT, linux-x64) | 4,864,944 B (step 1: 4,676,160 B); `ldd`: `libc`, `libm` only |

**Exit codes** ([`linux-startup-production.txt`](m1c-startup/linux-startup-production.txt), [`linux-startup-development.txt`](m1c-startup/linux-startup-development.txt); "reported" is the log line's time from the test's launch, "shown" when the message's window was viewable):

| Path | Code | Production NativeAOT | Development (CoreCLR) |
|---|---|---|---|
| No display | 6 | reported 1.9 ms | 32.3 ms |
| A display nobody serves (`gtk_init_check` fails) | 6 | 18.5 ms | 65.1 ms |
| WebKitGTK hidden | 3 | reported 7.1 ms, shown 123.6 ms | 46.6 ms, 162.5 ms |
| GTK 4 hidden (no message program in WSL) | 3 | reported 4.3 ms, none shown | 41.9 ms |
| Helpers hidden | 5 | reported 15.0 ms, shown 76.8 ms | 53.4 ms, 140.0 ms |
| `bwrap` hidden | 3 | reported 141.0 ms, shown 206.8 ms | 277.5 ms, 393.4 ms |
| Sandbox denied (bwrap: "Creating new namespace failed: nesting depth or /proc/sys/user/max_*_namespaces exceeded (ENOSPC)") | 5 | reported 15.7 ms, shown 99.4 ms | 90.2 ms, 179.6 ms |
| WebKitGTK older than required | 4 | (no override in Production) | 50.2 ms, 115.3 ms |
| A relaunch | 0 | exits 3.3 ms after its launch, handed over 1.6 ms after its host started | 40.5 ms, 21.1 ms |
| A stopped first instance | 8 | message at 2,075.8 ms | exit at 2,059.1 ms |

The `bwrap` case is slow only because a missing program is looked for on the whole of WSL's `PATH`, which holds the Windows folders on `/mnt/c` (an earlier run, before `/usr/bin` was looked in first, took 503 ms). The first instance, resumed after the stopped case, logged "a relaunch that waited … ms, longer than its sender waits, was dropped" and never ran the relaunch. The hook (`spar.OnStartupProblem`) ran for no display and printed the probe's own message.

**Software GPU** (same files, and [`linux-startup-egl.txt`](m1c-startup/linux-startup-egl.txt)): each run's log line and the environment of its `WebKitWebProcess` (`/proc/<pid>/environ`):

| Option | Log | Web process |
|---|---|---|
| `Auto` (default) | off, "GPU software (renderD128 not openable)", checked in 0.0 ms (2.7 ms CoreCLR) | `WEBKIT_DISABLE_DMABUF_RENDERER=1`, `GSK_RENDERER=cairo` |
| `Auto`, the user's `GSK_RENDERER=ngl` | off, "the user's GSK_RENDERER=ngl kept" | `WEBKIT_DISABLE_DMABUF_RENDERER=1`, `GSK_RENDERER=ngl` |
| `Always` | on, not checked | neither |
| `Never` | off, not checked | both |

Six threads existed when the host set the environment (ten on CoreCLR), all the runtime's. The EGL half, which runs where a render node can be opened, ran in the test process (CoreCLR): `llvmpipe (LLVM 20.1.2, 256 bits)`, software, 22.2–25.6 ms first and 9.5–10.3 ms again; with `GALLIUM_DRIVER=d3d12`, `D3D12 (Intel(R) UHD Graphics 770)`, hardware, 118.4 ms and 43.5 ms. Its first version found no GLES 2 configuration on Mesa's surfaceless display; it now uses `EGL_KHR_no_config_context` there.

**The Notebook** ([`linux-host-startup-default.txt`](m1c-startup/linux-host-startup-default.txt), [`notebook-start.log`](m1c-startup/notebook-start.log)), with the default now on and nothing set in the environment: launch to `ui-ready` median **226.1 ms** (211.8–235.2, n = 11), and 223.9 ms (218.2–245.6) in the run before (budget 350 ms; step 1 measured 211 ms with the variables set by hand and 577–582 ms without them); close request to exit median 43.6 and 25.2 ms; total PSS 231 and 233 MB. From the host's start in one run: single instance at 1.8 ms, prerequisites checked at 16.8 ms (the trial run 2.5 ms of it), GPU checked at 16.9 ms, theme read at 18.1 ms, GTK ready at 47.5 ms, window shown at 77.4 ms, `ui-ready` at 225 ms, web processes exited 32 ms after the window, exit 0.

**Theme** ([`linux-startup-notebook.txt`](m1c-startup/linux-startup-notebook.txt)): the Notebook with its saved choice (`color-scheme` in the data root), ten launches alternating light and dark, so each window opened where the other scheme's had just been. One pixel near the window's bottom-right corner, inside the view, sampled from the moment the window was viewable until 500 ms after `ui-ready` (about 11,500 samples a run): **0 samples of the other scheme in all ten**, the first sample in the theme's colour (`#ffffff`, `#1d1f24`) at 73–77 ms, then the page's own colour there (`#fbfaf7`, `#16171b`) from about 146 ms. Before the X window got a background pixel, the first 6 ms showed what had been on the screen beneath (70 white samples in a dark run after a light one, 58 black in the first light run). Without a portal, GTK's `gtk-application-prefer-dark-theme=1` in `settings.ini` made the probe's window dark; with a fake Settings portal on a private bus, `color-scheme` 1 made it dark and a `SettingChanged` to 2 turned it light while it ran.

## Not verified

- A real desktop: GNOME, KDE, Wayland, a compositor, a real portal, HiDPI; focus after a relaunch with a real activation token.
- A real GPU with a native driver: no render node could be opened here, so the EGL half never ran inside an app; it ran in the test process only.
- The message programs without GTK 4 (kdialog, zenity, xmessage): none is installed in WSL, so the GTK-missing case showed no message and exited 3.
- Exit 9 (another user on the same data root), and Fedora's paths and hints (`/usr/libexec/webkitgtk-6.0`, `dnf`), which come from package indexes.
- Containers (Ubuntu or Fedora images): Docker Desktop was not running.

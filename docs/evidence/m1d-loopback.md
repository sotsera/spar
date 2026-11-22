# M1d step 2: loopback in the development loop (evidence)

Recorded 2026-11-21 and 2026-11-22. Plan M1d, build step 2; decisions "M1d step 2's shape: loopback in the development
loop" (user decisions, 2026-11-21) and the implementation record "Loopback in the development loop (implementation
record; choices for review)". Design: `docs/design/dev-loop.md`, section 4, "Launch profiles".

## Setup

- Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24.21.0, Vite 8.3.8 (the Notebook's frontend), Playwright 1.64.1's
  headless Chromium (build 1248, the copy the repository's browser tests use).
- Linux: WSL2 Ubuntu-24.04 (kernel 6.18.58.1), Node 24.22.3, the same Playwright's `chromium_headless_shell-1248` from
  `/var/tmp/spar-linux/cache/ms-playwright`; `DISPLAY` and `WAYLAND_DISPLAY` unset, so nothing opened on WSLg's display;
  the core limit 1 byte. The tree is the copy `docs/evidence/m1c-blackbox/wsl.sh sync client appmodel build` keeps in
  `/var/tmp/spar-m1c-blackbox/repo` (written `<copy>` in the logs).
- The app: the Notebook's Debug build (the Development variant on CoreCLR, served from Vite through the app's origin),
  started as `dotnet run --no-build -lp Loopback`.
- **The runner** [`loopback-run.mjs`](m1d-loopback/loopback-run.mjs), the same on both OSes: the app's data folders in a
  disposable root (`LOCALAPPDATA` under `artifacts/test-host/m1d-loopback/` on Windows; `HOME` and every XDG folder under
  `<copy>/artifacts/linux-homes/m1d-loopback/` on Linux); `SPAR_LOOPBACK_BROWSER` set to the runner's own recorder, so
  Spar's request to open the browser is written to a file and **no browser of the user's ever opened**; the page driven
  in Playwright's headless Chromium. A backend restart ends the app's own process (`TerminateProcess` on Windows; on
  Linux `SIGKILL`, and `SIGTERM` in the `loopbackterm` run), never its tree, since Vite is a descendant of the app and a
  restart keeps it. Linux runs: [`wsl.sh`](m1d-loopback/wsl.sh) `loopback loopbackterm nodisplay desktop`.
- `dotnet watch`: [`watch-run.mjs`](m1d-loopback/watch-run.mjs), Windows only (below: it could not make watch restart).
- The desktop window on Windows: step 1's `DevRun.cs` (run from a copy outside the repository) on the Debug executable
  without `SPAR_HOST`, which is what the `Desktop` profile runs: shown without activation and offscreen, closed with
  `WM_CLOSE`; `LOCALAPPDATA` under `artifacts/webview2-profiles/m1d-loopback/desktop/`.
- Raw logs in [`m1d-loopback/logs/`](m1d-loopback/logs/), the repository's path written `<repo>`, the user's folders and
  name `<user>`. Each line is the runner's clock (ms), then `run:` (the runner) or the run's name (the app's standard
  output and error). The tokens in them are of sessions whose records were removed.

## Results

| Check | Windows | Linux (WSL) |
|---|---|---|
| A first run: a new dev session, the browser asked once to open the printed address | pass (1 request) | pass, twice (`loopback`, `loopbackterm`) |
| The page works through the bridge (the empty notebook, a page created), `ui-ready` | pass | pass |
| The token leaves the address bar (fragment moved to session storage) | pass | pass |
| A backend restart keeps the port and the token | pass | pass (SIGKILL; SIGTERM: the app logs "SIGTERM received: the app stops." and exits 0) |
| The open page reloads by itself, once, and lists the page created before | pass: 771 ms after run 2 listened | pass: 891 ms (SIGKILL), 906 ms (SIGTERM) |
| No further reload in 3 s; nothing more asked to open | pass | pass |
| Vite reused by every later run | pass (checked in 25–26 ms) | pass (21–22 ms) |
| Wrong token 401, no token 401, another `Origin` 403, another `Host` 403, Retry from another origin 403 | pass | pass |
| `SPAR_LOOPBACK_PORT` and `SPAR_LOOPBACK_TOKEN` used as given, nothing opened, the record unchanged | pass | pass |
| A malformed `SPAR_LOOPBACK_TOKEN` stops the start | pass (exit 70) | pass (exit 70) |
| A record last held 5 h ago: a new token on the same port, the browser asked again | pass | pass |
| The page from the earlier session is told its token is refused, and does not reload | pass | pass |
| CSP violations: page console and `app.log` | 0 and 0 | 0 and 0 |
| The record `obj/spar-loopback.json` readable by its owner only | `icacls`: only the user, `(F)`, not inherited | mode 0600 (unit test, in WSL's `Spar.Tests`) |
| The `Desktop` profile opens the window | pass (`DevRun.cs`: `window shown`, `ui-ready`, exit 0 after `WM_CLOSE`) | pass (`dotnet run -lp Desktop` on a private Xvfb: `ui-ready` at 921.5 ms, SIGTERM, exit 0) |
| No display: the app stops and names the Loopback profile, no fallback | not applicable (Windows has no no-display check) | pass: exit 6, "In development, run the page in a browser instead: dotnet run -lp Loopback (or SPAR_HOST=Loopback)." |
| Processes left (the app, `dotnet run`, Vite, the browser) | 0 | 0 |

- **Timings** (the app's own clock): the host listening 54–85 ms after its start; a later run's `ui-ready` 851–983 ms
  after its start; the reload's delay is mostly the page's look interval (250 ms, then 500 ms, then 1 s): Chromium's
  refused connections to the stopped port failed in well under a second on both OSes, unlike .NET's 2 s on Windows
  (pitfalls).
- **The waiting page's console** has one "Failed to load resource: net::ERR_CONNECTION_REFUSED" error per look while
  the backend is down (Chromium logs every failed fetch), and Spar's info line "the backend at … stopped; the page
  reloads when it runs again."

## `dotnet watch`

`dotnet watch --non-interactive -lp Loopback` started the app on loopback (the profile's variable reached it, the page
worked), but never restarted it in three tries: a new optional parameter on a private method was applied by Hot Reload
("C# and Razor changes applied in 1493ms"), and an edited project file and a static method made an instance one were
both answered "No managed code changes to apply" ([`windows-watch.run.log`](m1d-loopback/logs/windows-watch.run.log)).
The reload after a restart is the same mechanism as after `dotnet run`, but it is **not verified under `dotnet watch`**;
step 3 owns watch's restarts.

## Observed while building

- **Killing the app's process tree also kills its Vite server** (Windows `taskkill /T`): Vite runs under the app's
  `cmd.exe` and npm. The page then lost its HMR server too, and Vite's client's `SharedWorker` ping was refused by Trusted
  Types (one violation, the pitfall of step 1). A first runner ended the tree; the recorded runs end the app's process.
- **A relative `LOCALAPPDATA` is ignored:** Spar takes the variable only when it is a full path, so a first run wrote
  to the user's real `%LOCALAPPDATA%\com.example.Notebook.Development` (a log, and one Notebook page), and npm, started
  in the frontend folder, put its cache under `frontend/artifacts/` (removed). The recorded runs use absolute roots.
- **A Development build first built before this step keeps its old runtime configuration:** adding the ASP.NET Core
  framework reference in a target did not rewrite `Notebook.runtimeconfig.json` (its inputs hash covers runtime
  configuration options, not frameworks); the new `Spar.Loopback.SessionFile` option changes that hash, so the first
  build with this step writes the file again.

## Not verified

- The real default browser (ShellExecute on Windows, `xdg-open` on Linux) opening the page: no run opened a browser of
  the user's (the opener was the recorder every time).
- A graceful Ctrl+C in a Windows console (the runs end the process hard); `SIGINT` on Linux.
- `dotnet watch` restarts (above), and F5 in an IDE.
- The development server's error page and Retry on loopback (only the Retry's origin check ran).
- The loop guard (three short-lived reloads in a row) in a real browser: Vitest only (`test/restart.test.ts`).
- An in-flight call during a real restart: Vitest only (it ends `transport` / `lost`, nothing sent again).
- `SPAR_LOOPBACK_BROWSER` with a real browser command line, and the Linux opener (`xdg-open`).
- An app built from packages running on loopback: `Spar.PackageTests` builds its Debug consumers with the package's
  assembly referenced by path (12 of 12, after a fix for a `NuGetPackageRoot` without a trailing slash), but none runs.

A repeat of the Windows loopback run after the last two changes of the record passed again, the reload 796 ms after the
new run listened.

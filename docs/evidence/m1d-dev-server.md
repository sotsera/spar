# M1d step 1: Debug through the app's own origin (evidence)

Recorded 2026-11-21. Plan M1d, build step 1; decisions "Debug serves Vite through the app's own origin" (2026-11-14),
"M1d step 1's shape: Debug through the app's own origin" (user decisions, 2026-11-21) and the implementation record
"Debug through the app's own origin (implementation record; choices for review)". Design: `docs/design/dev-loop.md`,
section 4, "As built".

## Setup

- Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 157.0.4341.60, Node 24 (through nvm), npm 11.19.0, Vite
  8.3.8 with `@vitejs/plugin-react` 6.3.0 and Tailwind 4.3.5 (the Notebook's frontend).
- Linux: WSL2 Ubuntu-24.04 (kernel 6.18.58.1), WebKitGTK 2.52.6, GTK 4.14.5, Node 24.22.3, a private Xvfb (`:97`, an
  abstract socket only) and a private D-Bus session per run; `DISPLAY` and `WAYLAND_DISPLAY` unset first, so nothing
  opened on WSLg's display; the core limit 1 byte.
- The app: the Notebook's Debug build (`dotnet build samples/Notebook -c Debug`, the Development variant on CoreCLR),
  its executable started as `dotnet run` would start it.
  - Windows: by [`DevRun.cs`](m1d-dev-server/DevRun.cs) (grown from the security defaults run's harness), run from a copy
    outside the repository: `LOCALAPPDATA` in a disposable root under `artifacts/webview2-profiles/m1d-dev-server/`
    (so the WebView2 profile is there too), the window shown without activation and moved offscreen at once, its output
    in a file, no standard input (or a pipe the run holds), closed with `WM_CLOSE`. No mouse or keyboard input reached
    the desktop; the error page's Retry was pressed with the Development-only `SPAR_DEV_INPUT` (`key:Enter`, the
    DevTools Protocol's `Input` events inside the app's process).
  - Linux: by [`linux-run.mjs`](m1d-dev-server/linux-run.mjs) from [`wsl.sh`](m1d-dev-server/wsl.sh), on a copy of the
    working tree in `/var/tmp/spar-m1d-dev-server/repo`, `HOME` and every XDG folder under the copy's
    `artifacts/linux-homes/m1d-dev-server/<run>/`, closed with `SIGTERM` (the Linux host's session end). Retry was
    pressed with xdotool (XTEST) on the private Xvfb, unpacked beside it, not installed.
- **The HMR measurements** use a marker written into `Sidebar.tsx`: a `useRef` holding a random id per mount, and an
  effect that requests `/__spar-hmr/<n>/<id>/<Date.now()>` after each render. The request goes through the host's proxy,
  whose Debug log line carries the path, so the log shows when the page rendered the edit (the page's clock, which is the
  system's) and whether the component kept its state (the same id). Marker 0 adds the two hooks, so Fast Refresh
  remounts the component then; markers 1–5 change only the number. A CSS edit changes `--color-accent` in `styles.css`;
  its time is the write to the end of the updated stylesheet's fetch through the proxy (Vite's client applies it right
  after). Each edit came 1.5 s after the last (Vite's watcher coalesces quicker writes; pitfalls). The edited files were
  restored after every run.
- Raw logs in [`m1d-dev-server/logs/`](m1d-dev-server/logs/), the repository's path written `<repo>`, the Linux copy's
  `<copy>`, the user's folders `<user>`. Each line is the run's clock (ms), then `app:` (a line of `app.log`), `out:` (the
  app's standard output and error) or `run:` (the harness).

## Results

### The page through the app's origin, with the strict policy

| | Windows | Linux |
|---|---|---|
| Origin of the page | `https://app.localhost` | `spar://app` |
| Debug policy (logged) | `script-src 'self' 'nonce-…'; style-src 'self' 'nonce-…'; connect-src https://app.localhost ws://127.0.0.1:<port>`, the rest as Release | the same with `spar://app` |
| CSP violations in the HMR run (12 page updates, 1 load) | 0 | 0 |
| The channel works: a deliberate image from another origin | reported by the listener and `report-uri`, 1 group | the same (2 `report-uri` reports, 1 group) |
| HMR WebSocket to `ws://127.0.0.1:<port>` | connected (updates arrived) | connected from `spar://app` |

Vite's inline preamble (`@vitejs/plugin-react`) and every injected `<style>` ran under the nonce, with Trusted Types on.
The only violation seen in Debug is Vite's own, when its server dies (below).

**A worker script gets the policy from its own proxied response** (the `worker` step: a module worker added to the frontend,
its address through a Trusted Types policy, that tries `eval` and reports the outcome through a request the proxy logs):
on Windows the worker was fetched through the proxy (`GET /src/spar-worker-check.js 200`), `eval` was refused inside it
(`/__spar-worker/EvalError`; a worker without the header would have answered `2`), and its violation reached
`report-uri` (2 reports, `require-trusted-types-for … ("eval|1 + 1") at https://app.localhost/src/spar-worker-check.js`).
On Linux the same: fetched through `spar://app` (`GET /src/spar-worker-check.js 200`), `eval` refused inside it
(`/__spar-worker/EvalError`); WebKitGTK reports a worker's violations through neither channel (pitfalls), so no report.

### Fast Refresh and CSS (the Notebook; compare the spike's 23 ms and 9 ms, a smaller app without Tailwind)

| | Windows (WebView2) | Linux (WebKitGTK) |
|---|---:|---:|
| Fast Refresh, write → the edited component rendered, markers 1–5 | 43, 36, 37, 36, 35 ms; median **36** | 32, 32, 31, 37, 32 ms; median **32** |
| Marker 0 (hooks added, a remount) | 51 ms | 47 ms |
| State kept (the same mount id) | 5 of 5 | 5 of 5 |
| CSS, write → updated stylesheet fetched, 5 edits | 48, 54, 47, 35, 32 ms; median **47** | 47, 60, 51, 37, 37 ms; median **47** |
| …of which Vite's transform of the stylesheet (the proxied fetch) | 29–51 ms | 34–58 ms |

Every `.tsx` edit also updated the stylesheet (Tailwind rescans the classes: `hmr update /src/components/Sidebar.tsx,
/src/styles.css`), and Tailwind's rebuild is most of a CSS update here; the spike's 9 ms was a plain CSS file. The Linux
times use WSL's wall clock on both sides of each pair (pitfalls: it can jump; no jump showed in these runs).

### Starting, reusing and restarting

| Run (Windows) | Development server | `ui-ready` from the process start |
|---|---|---:|
| First ever (Vite's dependency pre-bundling, 2.5 s) | started, ready in 3,893 ms | 7,890 ms |
| Started, warm caches | ready in 640–721 ms (4 runs) | 1,087–1,109 ms |
| Reused by the next run (a backend restart) | checked in 25–29 ms | 595–663 ms |
| `SPAR_DEV_SERVER_URL` / `SparDevServerUrl` (a Vite the run started itself) | used as given, nothing started | 728 / 646 ms |
| `-p:SparDevServer=false` (the bundle embedded) | none (`Spar.DevServer.IsSupported` false) | 448 ms |

| Run (Linux) | Development server | `ui-ready` |
|---|---|---:|
| Started | ready in 264–346 ms | 826–1,033 ms |
| Reused | checked in 27–28 ms | 500–514 ms |
| `SPAR_DEV_SERVER_URL` | used as given | 708 ms |
| `-p:SparDevServer=false` | none | 387 ms |

A page load through the proxy (reused server): 35 requests on Windows, 0.7–24.1 ms each (median 8.0), and 34 on Linux,
0.5–15.7 ms (median 9.5), Vite's own time included.

The idle watchdog: after the first run's window closed, Vite wrote "no request and no HMR connection for 60 s: the
development server ends", its whole tree (cmd, npm, Vite) was gone and its record removed. With the app's input a pipe
(`--stdin-pipe`), closing it ended a server the app had started (the plugin's line about 60–100 ms later, by the run's
clock, on both hosts) and the window went to the error page within a second; a reused server keeps its own launcher's
input. Before the plugin read its input, closing the pipe left the server running (that run's log was not kept).

### When the server fails

| | Windows | Linux |
|---|---|---|
| Vite killed (its tree) → the error page | 452 ms (the pid is polled each second) | 361 ms; 1,198 ms in the final run |
| Retry pressed → a new server ready | 0.7 s (`npm run dev` started 6 ms after the key, ready in 698 ms), `ui-ready` 1.07 s after the key | 261 ms, `ui-ready` 0.55 s after the key |
| Nothing at `SPAR_DEV_SERVER_URL` | the error page: "Nothing answers at http://127.0.0.1:5173: No connection could be made…" (2.3 s, Windows' refused-connection retry) | the same, "Connection refused", 123 ms |
| `SPAR_DEV_SERVER_URL=http://example.com:5173/` | the error page: "… is not on this machine: the development server must listen on 127.0.0.1, [::1] or localhost.", also logged as an error | not run |
| No Node or npm on `PATH` | exit 3, "Start-up problem Missing: Node.js. … Install Node.js … or build with -p:SparDevServer=false …", help link (automated mode: no dialog) | the same |

The first Windows kill run, before a fix (its log was not kept; the one in `logs/` is after it), took 2 s more to start each new server: the stale record's port was asked
first, and a refused loopback connection takes about 2 s on Windows. The session now checks the recorded process first
(pitfalls). In both kill runs Vite's client, losing its server, made a `SharedWorker` from a `blob:` address to wait for it,
which Trusted Types refused: one violation and one unhandled rejection in the page, on both hosts (pitfalls).

### What the Production build carries

The Production Notebook (`dotnet publish samples/Notebook -c Release`, NativeAOT, Windows) is 5,243,904 B; the same
publish of `HEAD` (before this step) 5,244,416 B. None of the new markers (the dev-server types, `SPAR_DEV_SERVER_URL`,
`spar-dev-server.json`, the nonce placeholder, the proxy's route, the error page's text) is in it. A first version
passed the session through the window's and page server's constructors, and the type's name stayed in the image's
signature metadata: the session now reaches them only through methods called behind the feature switch. The boundary
test (`tests/Spar.BuildTests`) has the new markers and a control that finds them in `Spar.dll`; it passed on Windows and in WSL.

## Tests

- New: `tests/Spar.Tests/DevServerTests.cs` (41 cases: the address rules, the record, the settings, `PATH` lookup, the
  Debug policy, the proxy against a fake server: forwarded headers, the nonce in body and policy, the policy on worker
  scripts, redirects, encoded bodies, other methods, the host's own paths, the page without the plugin, a server that
  does not answer and Retry, the error page's escaping, and the agreement of the C# constants with the plugin's source),
  and `src/Spar.Client/test/devServer.test.ts` (7: the launch's variables, the server's settings, the record, the
  watchdog's rule).
- Suites, on both OSes: the implementation record's "Runs" (every suite green, the production boundary on Windows and in WSL). Their logs
  stay under `artifacts/m1d-runs/` (disposable), with the TRX reports under `artifacts/test-results/`.

## Not verified

- `dotnet watch` restarts (step 3) and F5 in an IDE; a real console's input (a terminal's `dotnet run`), where the server
  gets the self-held pipe as with no input.
- The error overlay's latency through the proxy; a full page load against Vite served directly (the spike's 35 ms).
- `SparDevServerUrl` and an off-machine address on Linux (host-neutral code, run on Windows only).
- The start-up dialog for a missing Node (the runs were automated; the dialog is the existing start-up message).
- A page served straight from Vite (a browser on its URL): the plugin sends no policy header of its own.

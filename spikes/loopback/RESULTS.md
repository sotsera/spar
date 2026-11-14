# Loopback host spike: results

Recorded 2026-11-13 on the development machine: Windows 11, SDK 11.0.100, Playwright for .NET 1.64.1, Edge 158, Node 24.21, Vite 8.3.8. It tests the "a Spar app also runs as a plain web app" idea from the plan discussion. The D1 spike's generator and tasks are reused through `../d1-pipeline/Spike.targets`.

## What was built

| Project | Role |
|---|---|
| `Loop.Loopback` | Development-only host on ASP.NET Core (`CreateSlimBuilder`). It binds `127.0.0.1:0` and creates a 24-byte random token per run. Assets come from the embedded frontend zip. `POST /_spar/call/{contract}/{command}` requires the bearer token, a matching `X-Spar-Contracts` fingerprint (SHA-256 of the manifest, 16 hex characters), and the host's own `Origin` when one is sent. It prints `SPAR_READY url=… build=… pid=…`, `SPAR_CALL id=… ok|error ms bytes` and `SPAR_UI_READY`. With `--exit-with-stdin` it exits when its stdin closes. |
| `Loop.App` | Same `notes` contract. The loopback host is referenced and compiled in **Debug only** (`SPAR_LOOPBACK`). Launch profiles: `Desktop` and `Loopback`. Plain-JS frontend with an authored `spar-sdk.js` that picks the transport at runtime: WebView2 when present, otherwise `fetch` with the token from the URL fragment, kept in `sessionStorage`. |
| D1 generator change | The client step also writes `notes.js`, a plain ES module that carries the contract fingerprint. |
| `Loop.Tests` | xUnit v3 (MTP v2) + Playwright. It starts the **real app executable** with `--loopback --exit-with-stdin` (and `KillOnParentExit` on Windows and Linux), reads the ready line, and drives the UI headlessly. |
| `vite-dev` | A Vite 8 dev server over the authored frontend that proxies `/_spar` to a running loopback backend. `hmr.mjs` measures frontend edit-to-visible time with `playwright-core`. |

## Results (observed)

### Bridge behaviour (curl)

| Request | Result |
|---|---|
| Valid save | 200 `{"text":"hi","tags":[],"version":1}`, logged as `SPAR_CALL … notes.save ok 3ms in=38B out=35B` |
| No token | 401 `unauthorized` |
| Wrong contract fingerprint | 409 `contract-mismatch`, with the host's fingerprint |
| Missing required property | 400 `invalid-request` with the generated serializer's message ("Note requires 'text'") |
| Foreign `Origin` | 403 `forbidden-origin` |
| Stdin closed | The app exited with code 0 |

### Headless browser tests (`dotnet test`, five consecutive runs, 4 tests each, all passed)

The whole suite took 2.9–3.0 s per run: two browser journeys, the stale-client rejection, and the token check. Per journey test:

| Browser | App ready | Browser launch | Page to `ui-ready` | Journey (fill, save, verify, reload) | Whole test |
|---|---:|---:|---:|---:|---:|
| `chromium-headless-shell` 156.0.8078.4 | 103–109 ms | 65–70 ms | 80–83 ms | 85–100 ms | **618–647 ms** |
| Edge 158.0.4370.16, `channel: msedge`, headless | 102–104 ms | 175–191 ms | 95–98 ms | 150–231 ms | **879–968 ms** |

No browser download was needed: the Chromium headless shell was already cached, and Edge is installed.

### Production boundary

`dotnet publish -c Release` (NativeAOT, win-x64) produced a 1,364,480-byte `Loop.App.exe`.
- It contains no `LoopbackHost`, `Kestrel`, `AspNetCore`, `SPAR_READY` or `_spar/call` strings.
- Run with `--loopback`, it ignores the flag and takes the desktop path.

This was a string scan; the framework needs a real metadata test.

### `dotnet watch -lp Loopback --non-interactive`

- **Startup:** the first ready line came out normally, with Hot Reload enabled.
- **Method-body edit:** hot-reloaded into the running app (`applied in 453 ms`, then 47 ms on a later edit, about 0.2 s from save). In-memory state was kept.
- **Adding a `[SparCommand]`:** "Restart is needed". Watch restarted on its own (non-interactive) and ran a full MSBuild build, so the generator and client task ran: `notes.js` gained `count`. **2.4 s from save to the new ready line.**
  - The new instance reported a new fingerprint.
  - A request with the old fingerprint got 409 `contract-mismatch`: a stale page is told to reload, never mis-dispatched.
- **Port and token change on every restart.** The URL changes, so an open page or an agent holding the old URL loses the app.

### Vite frontend hot reload with a live backend (three runs)

| Measure | Result |
|---|---|
| First load through Vite to `Ready` | 152–161 ms |
| Edit `main.js` on disk → change visible in the headless page | **45–102 ms** |
| Backend state across the frontend edit | kept (the saved note is still listed) |

## Findings for the plan

1. **The loopback idea holds.** A full UI journey against the real backend, in a real headless browser, costs about 0.6 s (Chromium shell) to 0.9 s (Edge). That's fast enough for an agent's inner loop and for CI, without a window or a desktop session.
2. **The fingerprint handshake works.** Contract changes under `dotnet watch` restart the app, and stale clients are rejected with 409.
3. **Hot Reload covers method-body edits without a restart.** But under Hot Reload no MSBuild build runs, so no client task runs either. That's fine while the contract is unchanged, which is exactly when Hot Reload applies. Contract edits forced a restart here; that was one observation, and it needs a test that proves contract edits always restart.
4. **Dev sessions need a stable address.** Every watch restart picks a new port and token. Proposal: the first instance of a dev session picks the port and token and records them (for example in `obj/spar/dev-session.json`), and restarts reuse them while the port is free.
5. **The generated client must resolve as a module.** Vite refused to import it from `publicDir`. The planned `@spar/*` packages linked into `node_modules` are the right shape; the spike used aliases instead.
6. **The dev-server proxy must present the backend's origin.** The backend's origin check correctly refused Vite-originated calls (403). The proxy, a trusted local component Spar would start, sets `Origin` to the backend's; the backend keeps its check strict.
7. **The loopback host can stay out of production.** Debug-only references and a compile symbol worked. The framework should use the `SparVariant` property and prove the boundary with a test.

## Not tested

- **Linux:** WSL has only the `docker-desktop` distribution, and Docker Desktop wasn't running. Building and running loopback mode on Linux, and `EnableWindowsTargeting`, are untested.
- **Hosted CI runners:** no run on GitHub-hosted Windows or Linux runners.
- **Desktop transport:** the WebView2 transport in `spar-sdk.js` (a stub here).
- **Watch owning Vite:** `dotnet watch` starting and stopping Vite itself. Here the two were started separately.
- **TypeScript compilation:** the spike frontend is plain JS.
- **Unsaved edits through `dotnet watch`:** only file saves were tested.
- **Contract-edit restarts:** only one restart was observed; a test should prove contract edits always restart.

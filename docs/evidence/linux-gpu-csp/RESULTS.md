# Software-GPU detection and `connect-src spar://app` on WebKitGTK: spike results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/linux-gpu-csp/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/linux-gpu-csp) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-19 on the development machine: Windows 11 (build 26320), WSL 2 kernel 6.18.58.1, 32 logical CPUs, an
Intel UHD Graphics 770 reachable only through WSL's D3D12 paravirtualisation (`/dev/dxg`). Environments:

| Name | What | WebKitGTK / GTK / Mesa | GPU devices |
|---|---|---|---|
| `wsl` | WSL Ubuntu 24.04.5 as the user is now (not in `render`) | 2.52.6-0ubuntu0.24.04.1 / 4.14.5 / 25.2.8 | `renderD128` (vgem, `root:render 0660`, not openable), `/dev/dxg` (0666) |
| `ubuntu-nodri` | Docker, Ubuntu 24.04 | 2.52.6-0ubuntu0.24.04.2 / 4.14.5 / 25.2.8 | none |
| `ubuntu-dxg-norender` | same image, `/dev/dri`, `/dev/dxg`, `/usr/lib/wsl` mounted, user not in the node's group (mirrors `wsl`) | same | as `wsl` |
| `ubuntu-dxg-render` | same, user in the node's group only inside the container (`--group-add 992`) | same | `renderD128` openable |
| `fedora-nodri`, `fedora-dxg-render` | Docker, Fedora 43 | 2.54.2 / 4.20.4 / 25.3.6 | as above |

A real GPU with a native Linux driver was not reachable (WSL exposes the GPU only through D3D12), so **no run saw a
hardware renderer by default**. The hardware path was exercised only with `GALLIUM_DRIVER=d3d12` forced (control below).

Every GTK and WebKit window ran under an Xvfb of our own (WSL: the unpacked Xvfb of `spikes/window-automation-linux`
inside a bubblewrap mount namespace; containers: the image's Xvfb), never on WSLg. Nothing was installed in the WSL
distribution, no group or setting changed; the render-group case exists only inside a container.

## Layout

| Path | Contents |
|---|---|
| `GpuProbe/` | NativeAOT C# probe (`LibraryImport`, function pointers from `eglGetProcAddress`), 0 warnings, 1.6 MB. Modes: `none` (baseline), `node` (B), `egl --platform surfaceless\|device\|default [--no-context]` (A), `combined` (C), `devices` (EGL device enumeration), `gdk [--window] [--gsk-after-init cairo] [--pre-egl]` (GDK's signal). One JSON line per run. |
| `tools/series.py` | Runs each method as a separate process: cold runs (page cache of every shared library and the probe dropped with `posix_fadvise(DONTNEED)` first), then warm rounds, methods interleaved. |
| `tools/summarize.py` | Writes `results/<env>/summary.md`. |
| `tools/container.sh`, `docker/` | Docker runner, `Dockerfile.ubuntu`, `Dockerfile.fedora`, `in-container.sh` (tasks `series`, `gdk`, `csp`, `accel`, `forced`). |
| `wsl/` | `build.sh` (NativeAOT publish in `/var/tmp/spar-linux/linux-gpu-csp`), `gpu.sh` (`series`, `gdk`), `csp.sh` (`csp`, `accel`). Homes under the working copy's `artifacts/linux-homes/<run>/`, runtime folder in `/dev/shm`. |
| `csp/` | `csp_host.py` (Python GI WebKitGTK 6.0 host: `spar://app` scheme registered secure and CORS-enabled, CSP and `nosniff` headers with `Content-Type` repeated, one counting loopback listener per probe target), `pages/` (page, shared probes, dedicated worker), `accel_host.py` (webkit://gpu, GSK renderer, memory). |
| `results/<env>/` | `series.jsonl`, `gdk.jsonl`, `forced-d3d12.jsonl`, `csp/*.json` + logs, `accel/*.json` + logs, `versions.txt`, `summary.md`. |

Commands (repository root; Git Bash needs `MSYS_NO_PATHCONV=1` for the `wsl --cd` calls):

```
wsl -d Ubuntu-24.04 --cd /mnt/e/<repo> --exec bash spikes/linux-gpu-csp/wsl/build.sh
wsl -d Ubuntu-24.04 --cd /mnt/e/<repo> --exec bash spikes/linux-gpu-csp/wsl/gpu.sh series 25 5   # and: gdk 25 5
wsl -d Ubuntu-24.04 --cd /mnt/e/<repo> --exec bash spikes/linux-gpu-csp/wsl/csp.sh csp           # and: accel
cd spikes/linux-gpu-csp
docker build -t spar-spike-linux-gpu-csp:ubuntu24.04 -f docker/Dockerfile.ubuntu docker/   # and Dockerfile.fedora → :fedora43
bash tools/container.sh <ubuntu|fedora> <nodri|dxg-norender|dxg-render> <series 25 3|gdk 25 3|csp|accel|forced 10>
```

## 1. Detecting a software GPU

### What each method returned

Ground truth in every environment, by default: GTK's GL (GDK context under Xvfb) and WebKit's web process
(`webkit://gpu` → `GL_RENDERER`) both ran on **llvmpipe**. Every method below agreed with it in every run, except where noted.

| Method | `wsl` | `ubuntu/fedora-nodri` | `*-dxg-norender` | `*-dxg-render` |
|---|---|---|---|---|
| B: render node | not openable → software | no node → software | not openable → software | openable, `drmGetVersion` = `vgem` ("Virtual GEM provider", sysfs `faux_driver`) → software (virtual-driver list) |
| A: EGL surfaceless + context | llvmpipe | llvmpipe | llvmpipe | llvmpipe (Mesa: "DRI2: failed to load driver" for vgem, then "Falling back to surfaceless swrast without DRM", which forces software) |
| A: EGL device platform | llvmpipe | llvmpipe | llvmpipe | Ubuntu llvmpipe; **Fedora: `eglInitialize` fails** (picks the vgem device) |
| A: EGL default platform, no display | `eglInitialize` fails (0x3001) everywhere: useless before GTK | | | |
| A without context: `eglGetDisplayDriverName` | `swrast` | `swrast` | `swrast` | `swrast` — **also `swrast` when the renderer is D3D12** (control), so the driver name cannot tell |
| C: B, then A if a node is openable | software, EGL skipped | software, EGL skipped | software, EGL skipped | EGL ran → llvmpipe |
| GDK GL context after `gtk_init` | llvmpipe | llvmpipe | llvmpipe | llvmpipe |

**Hardware control** (`GALLIUM_DRIVER=d3d12`, `forced-d3d12.jsonl`, 10 runs each): EGL surfaceless read
`D3D12 (Intel(R) UHD Graphics 770)`, vendor `Microsoft Corporation` → hardware, **also without render-group access**
(`dxg-norender`), because D3D12 uses `/dev/dxg`, not the render node. There B (and so C) says software. GTK then picked
`GskNglRenderer` (on llvmpipe it picks the older `GskGLRenderer`, so GTK 4.14 already tells software GL apart for its own
renderer choice, but never picks cairo). Not forced, nothing chose D3D12 in any environment (surfaceless forces
software after the vgem failure; Xvfb has no DRI3), and WebKit's sandboxed web process cannot reach `/dev/dxg` at all
(pitfall from `linux-wsl-desktop`).

### Cost

In-process detection time, NativeAOT, median (range) in ms; warm n=25 per method and environment, cold n=5 (`wsl`) or
3 (containers). Process wall time adds about 1.1–1.3 ms (the `none` baseline). "RSS" is the probe's growth.

| Method | `wsl` warm | `wsl` cold | containers warm (5 envs) | containers cold | RSS |
|---|---|---|---|---|---|
| B: render node (+`drmGetVersion` when openable) | 0.09 (0.09–0.18) | 0.11 (0.10–0.11) | 0.03–0.17 | 0.04–0.56 | +0.2 MB |
| C, no openable node | 0.09 (0.08–0.11) | 0.10 (0.10–0.12) | 0.03–0.09 | 0.04–0.10 | +0.2 MB |
| C, node openable (EGL runs) | — | — | 20.8 (20.2–24.8) Ubuntu, 22.4 (21.7–23.6) Fedora | 35.8 (33–94), 84 (68–104) | +54–74 MB |
| A: EGL surfaceless + context | 20.3 (18.4–26.8) | 39.7 (38.6–114.4) | 18.7–22.4 | 77–86 (31–100) | +54–74 MB |
| A: EGL device + context | 20.2 (18.3–27.6) | 38.4 (32.5–88.0) | 18.8–21.0 (fails on Fedora dxg) | 26–70 | +48–74 MB |
| A without context (driver name only) | 9.5 (8.8–11.1) | 89.5 (81–93) | 8.2–11.5 | 65–75 | +35–56 MB |
| EGL device enumeration only | 7.9 (7.6–9.2) | 73 (69–78) | 7.0–9.3 | 63–71 | +33–54 MB |
| A with D3D12 forced (control) | — | — | 67 (59–93) | — | +97 MB |
| GDK: `gtk_init` + GL context | 34.5 (33.2–43.5), of which GL 5.9 | 110 (101–113) | 38.8–43.0, GL 6.3–7.4 | 90–129 | +88–99 MB |

Phases of A (warm): library load 0.2 ms, `eglGetPlatformDisplay` 7–9 ms (loads Mesa and LLVM), `eglInitialize` 1–4 ms,
context + `glGetString` 8.5–9.8 ms, teardown 1 ms. Cold runs vary widely because the Windows host still caches the
distribution's disk image; the cold numbers are an upper band, not a true first boot.

**What the EGL probe costs the host overall:** GTK and WebKit load the same Mesa and LLVM libraries anyway (pitfall
"libLLVM and Mesa stay partly resident"). Running A before `gtk_init` in the same process (`gdk --pre-egl --window`):

| | `gtk_init` alone → realize | A, then `gtk_init` → realize | Difference |
|---|---|---|---|
| `wsl` warm | 91.5 ms (`gtk_init` 28.4) | 99.5 ms (A 18.3, `gtk_init` 16.5) | **+8 ms**, +6 MB RSS |
| `wsl` cold | 176.9 ms | 175.0 ms | none measurable |
| `ubuntu-nodri` warm | 99.4 ms | 107.4 ms | +8 ms |
| `fedora-nodri` warm | 58.1 ms | 67.4 ms | +9 ms |

### GDK's own signal

- It cannot run before GTK starts, which is when the host must set `GSK_RENDERER` with libc `setenv`. `gtk_init` already
  left **71 threads** running in every environment (2 before; llvmpipe's worker threads, since GTK 4 on X11 sets up EGL
  at init), so a `setenv` afterwards is not thread-safe.
- It did work: `GSK_RENDERER=cairo` set after `gtk_init` was honoured at the first window realize
  (`GskCairoRenderer` in every run). GSK reads the variable at realize, not at init.
- Cost after `gtk_init`: 5.9–7.4 ms. WebKit offers nothing cheaper before it starts; `webkit://gpu` exists only once a view
  runs.

Verdict: the GDK signal is cheap but needs a `setenv` with 70 threads alive; not recommended.

### Accuracy, in short

- A with a context is the only method that read the renderer that would be used; without a context, the Mesa driver
  name (`swrast`) was wrong for D3D12.
- B alone is right when no node is openable, but needs the virtual-driver list (vgem, vkms) to avoid calling WSL's vgem
  node hardware. It cannot see a GPU reached without a render node (D3D12 via `/dev/dxg`). That is harmless today: WebKit's
  sandboxed web process cannot use that GPU either.
- C is as accurate as A wherever a node exists, and as fast as B where none does.
- Not tested: a real GPU with a native driver (Intel/AMD/NVIDIA), a node whose Mesa driver is missing, NVIDIA's
  proprietary EGL, a GNOME/KDE Wayland session, WSLg's own display (out of bounds for this spike), a Flatpak.

### The chosen default takes effect

`accel_host.py`, 3 runs per configuration (the first run of each set is cold), page `webkit://gpu`, private memory
(statm resident − shared) of the Python UI process and every WebKit child after the page loaded. Medians of runs 2–3:

| Configuration | Settings policy read back | GSK renderer | `webkit://gpu` | Private MB (`wsl` / `ubuntu-nodri` / `ubuntu-dxg-render` / `fedora-nodri`) | Load finished ms (`wsl` / `ubuntu-nodri`) |
|---|---|---|---|---|---|
| Defaults | `always` | `GskGLRenderer` | Renderer "DMABuf (Supported buffers: Shared Memory)", `GL_RENDERER` llvmpipe | 257 / 255 / 255 / 196 | 296–311 / 188 |
| Policy `NEVER` (API) + `GSK_RENDERER=cairo` (**the decided default**) | `never` | `GskCairoRenderer` | no GL section | 82.8 / 82.5–84.6 / 82.5–84.5 / 73–82 | 200–204 / 100–107 |
| `WEBKIT_DISABLE_DMABUF_RENDERER=1` + `GSK_RENDERER=cairo`, policy untouched | `never` (the variable forces it) | `GskCairoRenderer` | no GL section | 72.8 / 72.3–72.5 / 72.4–72.6 / 73–82 | 182–199 / 91–93 |
| Policy `NEVER` only | `never` | `GskGLRenderer` | no GL section | 140.9 / 140.5 / 140.5 / 100 | 244–255 / 156 |

- The web process drops from 126 to 20 MB with either form; cairo saves 56–65 MB more in the UI process.
- `webkit_settings_set_hardware_acceleration_policy(NEVER)` works on 2.52.6 and 2.54.2; no environment variable is
  needed for WebKit. On 2.52 the DMA-BUF variable also saved about 12 MB in the UI process (56.8 against 44.5 MB);
  on 2.54 the two were within noise. The 2D canvas line still says "Accelerated" under `NEVER`; whether canvas then uses
  GL was not checked.
- `webkit://gpu`'s text is readable through `evaluate_javascript` (`document.body.innerText`).

### Recommendation

**Detection: C with the virtual-driver filter.** Check `/dev/dri/renderD*` (open read-write, `drmGetVersion`); with no
openable node, or only vgem/vkms nodes, decide "software" (0.1 ms). Otherwise open an EGL surfaceless display, create a
GLES context, read `GL_RENDERER` and match llvmpipe, softpipe, lavapipe, SwiftShader, swrast and "Basic Render Driver"
(about 21 ms warm, 35–100 ms cold, of which only 6–9 ms is net start-up cost because GTK reuses the loaded libraries).
Any EGL failure means "software". Apply it with the view setting `NEVER` and `GSK_RENDERER=cairo` (unless the user set
`GSK_RENDERER`), both before GTK starts.

## 2. `connect-src spar://app` on WebKitGTK

### Setup

`csp_host.py` serves `spar://app/` (scheme registered secure and CORS-enabled, as `spikes/webkit-wasm-off` did) with the
plan 3b policy on every response, the worker script included, varying only `connect-src`: `spar://app`, `'self'`, or
no CSP at all (control). One loopback listener per probe target counts every accepted TCP connection; in the containers
`--add-host app:127.0.0.1` makes `app` resolve, so `ws://app:<port>` and similar reach a listener too. 2 runs per
profile on 2.52.6 (WSL, distribution package) and 2.52.6 (Ubuntu container), and 2.54.2 (Fedora 43). All runs per
version gave identical rows.

### Results (2.52.6 and 2.54.2 identical)

| Probe | `connect-src spar://app` | `connect-src 'self'` | no CSP (control) |
|---|---|---|---|
| `fetch`, XHR to `data.json` and `spar://app/data.json` (page and worker) | ok | ok | ok |
| `EventSource` to `spar://app/events` and `events` | message received | message received | message received |
| `fetch("spar://other/…")` | refused (violation) | refused (violation) | reaches the handler (host `other`) |
| `ws://` / `wss://` to 127.0.0.1, `ws://localhost` | `SecurityError` thrown, 0 connections | same | 1 connection each |
| `ws://app/`, `ws://app:port`, `wss://app:port`, `https://app:port` | refused, 0 connections | refused, 0 connections | 1 connection each |
| **`wss://app/` (default port 443)** | **refused** | **allowed: no violation; the socket was created and failed with an error event (nothing listens on port 443)** | same error event |
| `fetch`/XHR to `http://127.0.0.1`, `http://localhost` | rejected / error, 0 connections | same | 200 |
| `EventSource` to loopback | `SecurityError` thrown, 0 connections | same | message received |
| `sendBeacon` to loopback | returns `true`, 0 connections, violation | same | 1 connection |
| `sendBeacon` to `spar://app/beacon` | throws `TypeError: Beacons can only be sent over HTTP(S)` (with or without CSP) | same | same |
| Worker: `fetch`/XHR/WebSocket to loopback | refused, 0 connections, **no event, no report** | same | 1 connection each |

- **`'self'` matches `spar://app`**: the same-origin probes pass and everything else is refused, except `wss://app` on
  port 443, which `'self'` lets through (as on Windows, where `'self'` allows `wss://app.localhost`). `spar://app`
  explicitly closes it. `ws://app` is refused by both.
- **What a violation looks like** (page): `securitypolicyviolation` with `blockedURI` the full URL with port and path
  (`ws://127.0.0.1:39687/`, `spar://other/data.json`), `violatedDirective` and `effectiveDirective` `connect-src`,
  `disposition` `enforce`, `statusCode` 0, source file and line. The page's call sees `SecurityError` (WebSocket,
  EventSource, thrown synchronously), `TypeError: Load failed` (fetch), an `error` event with status 0 (XHR), or `true`
  (`sendBeacon`). One report per violation is POSTed to `spar://app/_spar/csp-report` as `{"type":"csp-violation",
  "csp-report":{…,"blocked-uri":…}}` (14 for 14 under `spar://app`, 13 for 13 under `'self'`).
- **Workers:** blocked requests are refused and make no connection, but no violation event fires in the worker or the
  page, and no report is sent, on both versions. Violation logging cannot rely on workers.
- The page and the worker both had origin `spar://app`, `isSecureContext` true, and Trusted Types present.

### Not tested

Shared and service workers, `<link rel=preload>` and other non-script fetches, `fetch` with `keepalive`, WebTransport,
WebRTC, `navigator.sendBeacon` after unload, the Spar host itself (a Python host stood in), and `connect-src` additions
such as the Debug HMR WebSocket.

### Recommendation

**`connect-src spar://app` behaves as intended on 2.52.6 and 2.54.2:** same-origin fetch, XHR and EventSource work from
the page and its worker, relative and absolute; every `ws:`, `wss:`, `http:` and `https:` target, loopback and `app`
included, is refused before any connection. Keep it rather than `'self'`, which lets `wss://app` through. Do not count
on violation events or reports from workers.

# CSP, navigation lock and security headers: spike results

Recorded 2026-11-13/14 on the development machine: Windows 11 (build 26320), SDK 11.0.100, Node 24.21, WebView2 Runtime 157.0.4341.46, Playwright `chromium-headless-shell` 156.0.8078.4 (playwright-core 1.64.1), Docker Desktop with the existing image `spar-linux-desktop:webkit2.52` (WebKitGTK 2.52.6, GTK3/4.1 and GTK4/6.0, Xvfb, software rendering). Vite 8.3.8, React 19.3.1.

**Question.** Does the plan 3b proposal hold on both desktop engines and on loopback: a strict CSP sent as a header, host-injected boot scripts, a navigation lock, the other security headers, Trusted Types, SRI, Vite dev under the same policy, and a C# declaration the build can read?

**Short answer.** Yes, with changes. The base policy works unchanged with the React production bundle on all four engines (0 violations). Host-injected scripts run first everywhere, including under Vite. The navigation lock holds on both hosts, but needs one more WebView2 filter so that cancelled navigations never reach the network. The Linux host must repeat `Content-Type` as a header, or `nosniff` blocks its CSS, classic scripts and workers. WebKitGTK ignores `Permissions-Policy`, CORP, COEP isolation and the `Referer` question; the hosts' permission events are the real gate there. One camera permission prompt reached the user's desktop (see "Incident").

## Layout

| Path | Contents |
|---|---|
| `pages/` | Probe pages: `probe.html/js` (CSP, headers, permissions, Trusted Types, injection order), `nav.html/js` (navigation lock), `profiles.json` (the header profiles every host uses), small assets. |
| `frontend/` | React + Vite app (`src/`), `spar-security.ts` (the Spar-style Vite plugin: dev header with per-request nonce, `/_spar/log`, `assetsInlineLimit: 0`, SRI and the integrity import map), `vite.config.ts`. Builds: `dist/`, `dist-inline/` (Vite's default inline limit), `dist-sri/`. |
| `Csp.Shared/` | Code linked into every host: `AppRoutes` (files + profile headers + test routes), `ExternalServer` (the "external" origin on 127.0.0.1:5180, logs every hit), `MiniHttp`, `Injected` (the boot script), `LoopbackHost` (app on 127.0.0.1:5190 with the Host check). |
| `Csp.Windows/` | WebView2 host (NativeAOT; WebView2 COM declarations copied from `spikes/browser-telemetry` and extended), plus `--loopback` mode. |
| `Csp.Linux/` | WebKitGTK host (NativeAOT, `-p:SparGtk=4` for 6.0; native imports copied from `spikes/browser-telemetry` and extended). |
| `Csp.Runtime/`, `Csp.Generator/`, `Csp.Sample/`, `Csp.Invalid/`, `Csp.Base/`, `Csp.Extract/` | Q9: builder stub, one analysis shared by the incremental generator, the analyzer and the pre-compile extractor; a valid and an invalid app; the extractor that writes `csp.json`. |
| `tools/` | `run-desktop.sh` (WebView2 runs), `loopback.mjs` (Chromium runs), `vite-hmr.mjs` (Fast Refresh under the policy), `vite-serve.mjs`, `host-hmr.sh`, `show.mjs` and `table.mjs` (log summaries). |
| `linux/` | `sync.sh`, `publish.sh`, `run.sh`, `matrix.sh`, `rerun.sh`, `in.sh`, `rebuild.sh` (container side). |

Logs: `artifacts/csp-navigation/{windows,loopback,linux/gtk3,linux/gtk4,vite}/`. Each run log has the host's view (`SERVE`, `NAV_BLOCKED`, `NEW_WINDOW_BLOCKED`, `PERMISSION_DENIED`, `REPORT`, `NATIVE` = messages from the injected script) and the external server's (`EXT_HIT` with `Referer`, `Origin`, fetch metadata). A request that never reached the external origin shows as a missing `EXT_HIT`.

## How it was run

```sh
# Windows (Git Bash), from spikes/csp-navigation
dotnet publish Csp.Windows -c Release                      # NativeAOT, 0 warnings, 3,034,112 B
(cd frontend && npm install --offline && npx tsc --noEmit -p . && npx vite build)
SPAR_OUT=dist-inline SPAR_KEEP_INLINE=1 npx vite build; SPAR_OUT=dist-sri SPAR_SRI=1 npx vite build   # in frontend/
dotnet run --project Csp.Extract -c Release -- Csp.Base obj/spar/client/csp.json   # policy for the Vite plugin
tools/run-desktop.sh <label> <profile> <page> [--intercept-foreign-documents] [--real-open] [--tamper x] [--dev-url ...]
node tools/loopback.mjs <label> <profile> <page|url> [--dist dist-sri] [--tamper x]
node tools/vite-hmr.mjs dev-base; SPAR_TT=1 node tools/vite-hmr.mjs dev-tt
tools/host-hmr.sh windows|gtk3
node tools/table.mjs probe-base probe-coep probe-tt        # cross-engine tables

# Linux: container spar-csp-linux (seccomp=unconfined, /src read-only, /work volume, artifacts bind-mounted,
# NuGet volume seeded by copying spar-linux-desktop-nuget, Xvfb :99), apps run as uid 1000
linux/rebuild.sh                                           # sync + NativeAOT publish gtk3 and gtk4 (0 warnings, 2,874,464 B)
linux/in.sh 'linux/matrix.sh gtk3'; linux/in.sh 'linux/matrix.sh gtk4'; linux/in.sh 'linux/rerun.sh gtk3; linux/rerun.sh gtk4'
```

Origins: WebView2 `https://app.spar.invalid` (WebResourceRequested), WebKitGTK `spar://app` (custom scheme, registered secure and CORS-enabled unless `--no-secure`/`--no-cors`), loopback `http://127.0.0.1:5190`, external `http://127.0.0.1:5180` (inside the container on Linux).

Header profiles (`pages/profiles.json`): `none`; `base` (plan 3b CSP + nosniff, `Referrer-Policy: no-referrer`, COOP and CORP `same-origin`, the `Permissions-Policy` deny list); `meta` (same policy as `<meta>`); `ext` (external origin allowed: controls); `coep`; `tt` (`require-trusted-types-for 'script'`); `report-uri`; `report-to`/`report-to-ext`; `sri` (base + the import-map hash).

## Incident: a camera prompt on the user's desktop

- **Step:** `tools/run-desktop.sh probe-none none /probe.html` (WebView2, profile `none`, so no `Permissions-Policy`), probe `getUserMedia({ video: true })`, about 4.2 s into the run (log `artifacts/csp-navigation/windows/probe-none.log`).
- **What the host saw:** no `PermissionRequested` event for the camera (only geolocation, which the host denied in 1 ms). The call hung until the probe's 4 s timeout. The machine has a camera (HD Pro Webcam C920).
- **Which prompt:** not seen by me. Because WebView2 never raised `PermissionRequested`, it was not the WebView2 permission prompt (that one appears only when the event is left unhandled, and the handler answered every event it got). It is most likely the Windows camera privacy prompt for an unpackaged desktop app (Windows 11 build 26320). The `ConsentStore` registry keys showed no entry for the spike, so this is inferred, not confirmed.
- **Possible second source:** before the probe was restricted, the loopback runs in headless Chromium called `getUserMedia` under `Permissions-Policy: camera=()`. Six returned `NotSupportedError` after 0.7–0.9 s; one (`loopback/probe-base`) hung for 4 s. That could also have reached the OS. Not confirmed.
- **Changes made:** the camera probe now runs only with `?media=1`, which only the Linux container runs pass (no person can be prompted there). Baseline runs without `Permissions-Policy` no longer call `getUserMedia` on Windows. Geolocation still runs on WebView2, where the host's `PermissionRequested` handler is shown to answer it first.
- **Consequence for Spar:** on WebView2 the camera request did not go through `PermissionRequested` at all in this run. So a deny-by-default handler alone did not keep a prompt from a person. With `Permissions-Policy: camera=()` the call failed in about 250 ms (`NotAllowedError`) with no host event; whether anything reached the OS then was not checked.

## Q1. CSP header on host-served responses

**Honoured on every engine, as a header and as `<meta>`.** Under `base`, no probe reached the external server (0 `EXT_HIT` on WebView2, WebKitGTK 4.1 and 6.0; loopback the same); every blocked load raised a violation with the right directive.

| Probe (profile `base`) | WebView2 | Chromium loopback | WebKitGTK 4.1 | WebKitGTK 6.0 |
|---|---|---|---|---|
| inline script, inline handler, `javascript:` URL, string `setTimeout` | blocked | blocked | blocked | blocked |
| `eval`, `new Function` | blocked | blocked | blocked | blocked |
| `WebAssembly.compile` | **blocked** (`wasm-unsafe-eval` needed) | **blocked** | ran | ran |
| inline `<style>`, `style=""` | blocked | blocked | blocked | blocked |
| CSSOM `el.style.x = …` (what React does) | applied | applied | applied | applied |
| `data:` image | blocked | blocked | blocked | blocked |
| external img, font, fetch, script | blocked, no hit | blocked, no hit | blocked, no hit | blocked, no hit |
| `<base href=external>` | blocked | blocked | blocked | blocked |
| `<object>` | blocked (`object-src`) | blocked | blocked | blocked |
| external iframe | blocked (`frame-src`) | blocked | blocked | blocked |
| **same-origin iframe** | **blocked** (`default-src 'none'` covers `frame-src`) | blocked | blocked | blocked |
| form POST, external and same-origin | blocked (`form-action`) | blocked | blocked | blocked |
| same-origin worker / `blob:` worker | ran / blocked | ran / blocked | ran / blocked | ran / blocked |

- Control (`none`): every probe ran or loaded. CORP `same-origin` on an external image blocked it on every engine, as expected from a network response.
- **`<meta>` fallback** (`meta`): identical results to the header on all engines. Not needed: no engine ignored the header.
- **The React production bundle** (`dist/`) under `base`, `meta`, `tt` and `coep`: CSS applied, style prop applied, lazy chunk loaded, logo loaded, click handled, **0 violations** on all four engines (`react-*` logs). It needed `build.assetsInlineLimit = 0`: with Vite's default the 137-byte logo became a `data:` URL and was blocked (`react-base-inline`: 1 violation on Chromium engines, 2 on WebKitGTK).
- **Linux host bug found:** with `X-Content-Type-Options: nosniff`, WebKitGTK blocked the same-origin CSS, classic script and worker (no CSP violation, no error), although `webkit_uri_scheme_response_set_content_type` set the right type. With `Content-Type` also appended to the `SoupMessageHeaders` passed to `set_http_headers`, all three loaded (`probe-base` vs `probe-base-notypeheader`). Bare types without `; charset=utf-8` did not help.

## Q2. Host-injected scripts under a strict CSP

**They run despite `script-src 'self'`, before every page script, and the page reads `window.__spar` synchronously.**

- Order recorded by the page under `base`, on WebView2 and both WebKitGTK versions: `injected > head-classic:spar=object > module:spar=object`. The injected script sees `document.readyState === "loading"`. The value is frozen and non-writable; the module read `{ injectedAt, telemetry: { serviceName, delayMs } }` at evaluation time.
- **Pages served by Vite (Debug):** the same order and value on WebView2 (`windows/vite-dev`, page `http://127.0.0.1:5273/`) and on WebKitGTK 4.1 and 6.0 (page `http://host.docker.internal:5273/`; `vite/dev-hosts.jsonl`, `vite/dev-tt-linux.jsonl`), 0 violations.
- **Difference:** WebView2 exempts the injected script from the page's CSP for `eval` (`evalInInjected: ran` under `base`); WebKitGTK does not (`blocked:EvalError`). Injected and bridge code must never use `eval` or `new Function`.
- WebView2's `AddScriptToExecuteOnDocumentCreated` also fires in frames, but only the top frame posted (frames have no `chrome.webview` channel here). Not examined further.

## Q3. Navigation lock

`nav.html` runs 18 steps, each followed by an "alive" check, then a same-origin navigation that must succeed. Host logs: `windows/nav-*.log`, `linux/gtk*/nav-*.log`.

| Step | WebView2 (lock = NavigationStarting / FrameNavigationStarting cancel, NewWindowRequested handled) | WebKitGTK 4.1 and 6.0 (lock = `decide-policy` ignore) |
|---|---|---|
| `location.href`, `location.replace`, meta refresh | cancelled (TOP) | ignored (NAVIGATION) |
| unknown scheme (`spar-unknown-scheme:`) | cancelled; no external-protocol dialog | ignored |
| form POST, top-level and into an iframe | cancelled; **the POST never reached the server** | ignored; never reached |
| iframe `src`, iframe `location` | cancelled (FRAME) | ignored (subframe navigations go through `decide-policy` too; no main-frame flag) |
| `window.open` (external and same-origin), synthetic `a.click()` on `target=_blank` | NewWindowRequested, blocked (`window.open` returns null) | blocked by WebKit's own popup blocker before `decide-policy` (returns null) |
| trusted click on `target=_blank` | NewWindowRequested (`user=1`), blocked | NEW_WINDOW_ACTION (`user=1`), ignored |
| trusted middle, Ctrl and Shift click | NewWindowRequested, blocked | arrive as NAVIGATION_ACTION in the same view (button 2 or modifiers), ignored |
| trusted plain click on an external link | cancelled | ignored |
| host-served 302 to an external URL | followed, then cancelled (`redirected=1`) | **not followed**: the 302 body is committed as the document (`nav-redirect`) |
| same-origin navigation afterwards | allowed, page alive | allowed, page alive |

- Trusted input: in-process CDP `Input.dispatchMouseEvent` on Windows; XTEST (`xdotool`) on Xvfb.
- **WebView2 leak:** with only the navigation events, every cancelled **GET** navigation still reached the external server (`EXT_HIT … dest=document/iframe`: assign, replace, meta refresh, redirect target, iframe, plain click, in `nav-none` and `nav-base`). The cancel races a request already started. **Adding `AddWebResourceRequestedFilter("*", DOCUMENT)` removed every hit** (`nav-none-intercept`: 0 navigation hits, and the handler was never even called for them; foreign documents would be answered 403). WebKitGTK showed no hits.
- Subresource redirects are CSP's job: `<img src=/_test/redirect?to=external>` loaded with `none` (WebView2) and was blocked by `img-src` under `base`.
- **Open in the system browser:**
  - Windows: `ShellExecuteExW` (`SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI`, http/https only) returned true in 374.5 ms, and the default browser fetched `http://127.0.0.1:5180/opened` 98 ms later. That was done **once**; every other run logged a stub.
  - Linux in Docker: `g_app_info_launch_default_for_uri` failed in 0.3–5.4 ms with "Operation not supported" (no handler in the container). The XDG OpenURI portal was not tried.

## Q4. Other headers and the permission gate

| Header | WebView2 (host-served) | Chromium loopback | WebKitGTK 4.1 / 6.0 (`spar://`) |
|---|---|---|---|
| `X-Content-Type-Options: nosniff` (script served `text/plain`) | honoured (script not run; ran without the header) | honoured | honoured, **only with `Content-Type` in the header set** (see Q1) |
| `Referrer-Policy: no-referrer` | not observable: https app to http external sends no `Referer` anyway (strict-origin-when-cross-origin downgrade) | honoured: default sent `Referer: http://127.0.0.1:5190/`, with the header none | not applicable: `spar://` pages sent no `Referer` even without the header |
| COOP `same-origin` | not applicable on desktop (all new windows blocked) | honoured: an external page's popup handle to the app reported `closed=true` (false without) | not applicable (no popups) |
| CORP `same-origin` (app resource loaded by an external top page, lock off) | **not honoured**: external page loaded the app's image and no-cors fetch | honoured: both blocked (loaded without) | **not honoured** |
| `frame-ancestors 'none'` (external page framing the app) | inconclusive: WebView2 did not intercept the cross-origin frame's subresources, so the frame never ran either way | honoured: the app frame's script never ran (ran without) | **not honoured**: the `spar://` frame's script ran inside the http page |
| `Permissions-Policy` deny list | honoured: `featurePolicy.allowsFeature(...)=false`, `permissions.query` = denied, geolocation failed with code 1 in 0 ms with **no** `PermissionRequested`; camera `NotAllowedError` in ~250 ms | honoured (same API results) | **not honoured**: no policy API, `permissions.query` = prompt, and geolocation still reached the host's `permission-request` |
| COEP `require-corp` | honoured: `crossOriginIsolated = true`, `SharedArrayBuffer` available | honoured | **no isolation**: `crossOriginIsolated = false`, with or without the secure-scheme registration |
| `isSecureContext` | true | true (loopback) | true, **also without** `register_uri_scheme_as_secure` (and without `as_cors_enabled`) |

- CORP and `frame-ancestors` matter only when another origin can load the app, which on desktop the lock already prevents; on loopback both work.
- Host permission gate:
  - WebView2 `PermissionRequested` → `DENY`: geolocation was denied in 1 ms with no prompt (`probe-none`). For the camera, see "Incident": the event never fired.
  - WebKitGTK `permission-request` → `webkit_permission_request_deny`: geolocation was denied (`WebKitGeolocationPermissionRequest`) on both versions, also under `Permissions-Policy`. The camera ended in `OverconstrainedError` in 10–76 ms before any permission request: the container has no camera, so the camera path is untested there. `enable-media-stream` is on by default (logged 1).
- `X-Frame-Options` was not needed: no engine needed the `<meta>` fallback.
- Host header check: loopback answered 200 for `127.0.0.1:5190` and `localhost:5190`, and 421 for `evil.example:5190` and `127.0.0.1:5191`. Vite (default `allowedHosts`) answered 403 for `evil.example` and also for `host.docker.internal`; the Linux dev runs had to add `host.docker.internal` (test-only).

## Q5. Vite dev under the strict policy

The plugin sends the `csp.json` policy as a header on every response, adds `'nonce-<per request>'` to `script-src` and `style-src`, and swaps it for Vite's `html.cspNonce` placeholder (same length, so `Content-Length` stays right). Results in headless Chromium (`vite/dev-base.log`, three runs):

- Boot 314–321 ms. Component edit to DOM update 37–47 ms; React state was kept (`count 3`), with no reload (same boot id). CSS edit 12–15 ms. Error overlay shown in 26–31 ms and **styled** (its `<style>` carries the nonce). The fix cleared it in 31 ms.
- **0 CSP violations. No extra relaxation was needed:** the HMR WebSocket (`ws://127.0.0.1:5273`) was allowed by `connect-src 'self'` (CSP3), so the `hmrConnect` option was not needed. It also worked unchanged in WebView2 and WebKitGTK.
- Inside the desktop hosts (`tools/host-hmr.sh`): a component edit reached the page in about 90 ms in WebView2 and about 80 ms in WebKitGTK 4.1 (timer-adjusted; the container clock may be skewed), with no reload and 0 violations. CSS and overlay were not checked there.
- **Bug found and fixed in the plugin:** Vite's `ETag` is computed before the nonce swap. A WebView2 profile with a cached page revalidated it (304) and kept the old body, whose nonce no longer matched the new header, so Vite's injected `<style>` was blocked (2 violations, page stuck). The plugin now drops `If-None-Match` and `ETag` for HTML and sends `Cache-Control: no-store`; two repeated loads then had 0 violations.
- A harness detail, not CSP: writing the fix within milliseconds of the broken file was coalesced by the watcher (the same without the policy). A 1 s pause fixed it.
- `assetsInlineLimit = 0`: no `data:image` in `dist/assets/index-*.js`; with the default, the logo was inlined (`dist-inline`). The other `data:` strings in the bundle are React property names.

## Q6. Violation reporting

| | WebView2 | Chromium loopback | WebKitGTK 4.1 / 6.0 |
|---|---|---|---|
| injected listener (document creation) | 28 | n/a (no injected script) | 25 |
| page listener in a module (the SDK's position) | 24 | 24 | 20 |
| `report-uri` same-origin (`/_spar/report`, host-served) | **28** | **28** | **25** |
| `report-uri` external | 28 | 28 | 25 |
| `report-to` + `Reporting-Endpoints` (same origin / external) | 0 in 75 s / 0 in 75 s | 0 in 75 s / 0 in 75 s | **25 / 25, immediately** |

- The SDK's module listener misses the parse-time violations: the inline `<script>`, inline `<style>`, `style` attribute and inline handler. WebKitGTK also misses the injected script's own `eval` test.
- Both the host-injected listener (desktop, forwarded over the native message channel with directive, blocked URI and source line) and `report-uri` (every engine, including loopback where nothing is injected) received every violation.
- `report-uri` bodies carry `effective-directive`, `blocked-uri` and the original policy.
- SRI failures are not CSP violations (none reported). Only the console, and the injected `error` listener added later, see them.

## Q7. Trusted Types (`require-trusted-types-for 'script'`)

- `trustedTypes` exists on all four engines (WebKitGTK 2.52 included) and is enforced the same way: string `innerHTML`, `script.src`, `new Worker(url)`, string `setTimeout` and `<object>` (Chromium only) threw; policy-created values worked.
- **React production bundle: works on all four engines**, 0 violations (`react-tt`).
- **Vite dev client, Fast Refresh and the error overlay:** worked in headless Chromium with TT required (`vite/dev-tt.log`: the same timings, overlay styled, 0 violations). Dev boot under TT was clean in WebView2 and WebKitGTK 4.1/6.0.
- Cost to note: any `new Worker("...")` or dynamic `<script src>` needs a TT policy. That includes Vite's `?worker` pattern (not tested).

## Q8. Subresource Integrity

- The plugin's `writeBundle` hook reads the written files, adds `integrity="sha384-…"` to the entry script, CSS and `modulepreload` links, and puts an import map `{"integrity": {"/assets/Lazy-….js": "sha384-…"}}` first in `<head>`. It writes the map's CSP hash to `spar-sri.json`, which the hosts add to `script-src` (profile `sri`).
- **An inline import map is an inline script:** under the base policy it is blocked (1 violation) and the lazy chunk loses its integrity check (`sri-base-profile`, `sri-blockedmap-tamper-lazy`: the tampered chunk loaded). The policy must include the map's hash.

| Tampered file (one byte) | WebView2 | Chromium loopback | WebKitGTK 4.1 | WebKitGTK 6.0 |
|---|---|---|---|---|
| none (`sri-ok`) | works | works | works | works |
| entry JS (+CSS) | page never starts | never starts | never starts | never starts |
| CSS only | CSS not applied, app runs | same | same | same |
| lazy chunk (import-map integrity) | `import()` fails ("Failed to fetch dynamically imported module") | fails (console: no valid digest) | fails | fails |
| control: same tampering without SRI | everything loads | loads | loads | loads |

- **Import-map integrity is supported on WebView2 157, Chromium 156 and WebKitGTK 2.52 (both APIs).**
- Build cost: hashing 72.7–79.3 ms per build (3 integrity entries; 223 KB entry). Whole `vite build` 1.52–1.58 s with SRI against 1.46–1.49 s without (3 runs each; one SRI run took 6.7 s, an outlier).

## Q9. The C# declaration (prototype)

```csharp
var app = SparApplication.Create(args, spar =>
{
    spar.ContentSecurity(csp => csp
        .FontSrc(Fonts)                                 // const string
        .StyleSrc("https://fonts.googleapis.com")
        .Allow(Permission.Camera));
});
```

- **One analysis** (`Csp.Generator/PolicyAnalysis.cs`) is used three ways:
  - the incremental generator emits `Spar.Generated.SparSecurityPolicy.ContentSecurityPolicy` and `.PermissionsPolicy` as `const string`;
  - the `DiagnosticAnalyzer` reports located errors;
  - `Csp.Extract` runs it on a `CSharpCompilation` of the app's sources (no build) and writes `csp.json`, only when its content changes.
- **Valid app** (`Csp.Sample`): the generated policy equalled the policy the runtime builder computed from the same lambda (`match: True`; the plan's Debug check). The permissions line was `camera=(self), microphone=(), …`.
- **Invalid app** (`Csp.Invalid`), `dotnet build`, five located errors. The extractor reported the same five.
  - `Program.cs(8,56) SPAR102 'host' is not a compile-time constant`
  - `Program.cs(11,9) SPAR101 … inside an if statement`
  - `Program.cs(15,47) SPAR104 'https://a.example; script-src *' is not a valid source expression`
  - `Program.cs(17,25) SPAR103 the argument must be a lambda` (a delegate variable)
  - `Program.cs(22,52) SPAR101 … not inside the configuration lambda` (a helper method)
- **Extractor finding:** without the SDK's implicit global usings, binding failed (`Action`, `Environment` unresolved), so `Create` did not bind, and every call was misreported as SPAR101. Adding the `ImplicitUsings` set fixed it. A pre-compile step must reproduce the project's global usings, or read them from its `obj/`.
- Timing: 109–114 ms for an app with no additions, 571–574 ms for `Csp.Sample` (parse and bind setup 73–90 ms).
- `Csp.Base` (no additions) produced exactly the plan 3b base policy, which the Vite plugin read (`obj/spar/client/csp.json`).
- Analyzer release tracking (RS2008) was satisfied with `AnalyzerReleases.*.md`; no suppression.
- IDE squiggles were not checked.

## What changes from plan 3b

1. **WebView2 lock:** also register `AddWebResourceRequestedFilter("*", COREWEBVIEW2_WEB_RESOURCE_CONTEXT_DOCUMENT)` and answer foreign documents with 403. Without it, cancelled GET navigations (top level and frames) still reach the network.
2. **WebKitGTK responses:** put `Content-Type` in the `SoupMessageHeaders` as well as `set_content_type`, or `nosniff` silently blocks CSS, classic scripts and workers.
3. **Base policy side effects to document or decide:**
   - it blocks same-origin iframes (opt in with `FrameSrc("'self'")`);
   - it blocks WebAssembly on WebView2 and Chromium (`ScriptSrc("'wasm-unsafe-eval'")`), but not on WebKitGTK.
4. **Reporting:** add `report-uri /_spar/<route>` to the policy. It is the only channel that delivered every violation on all engines, including those before the SDK loads, and on loopback. Do not use `report-to`: WebView2 and Chromium delivered nothing in 75 s, and Chromium is documented to ignore `report-uri` when `report-to` is present (not verified here). On desktop, the injected script's listener is an equal alternative that also sees SRI and load errors.
5. **SRI:** the plugin must add the import map's `sha256` to `script-src` (in `csp.json` and the dev header). SRI failures need the injected `error` listener to become diagnostics.
6. **Vite plugin:** HTML must be sent with `Cache-Control: no-store` and no `ETag`, and `If-None-Match` ignored; otherwise a 304 reuses a stale nonce. No HMR `connect-src` addition is needed.
7. **Permission gate:**
   - On WebKitGTK `Permissions-Policy` is ignored, so the host's `permission-request` denial is the only gate.
   - On WebView2 the camera request bypassed `PermissionRequested` in the one run without the policy, and a prompt reached the desktop. Keep `Permissions-Policy` as the effective first gate there, and investigate the event path before relying on it.
8. **WebKitGTK cannot be cross-origin isolated** (`crossOriginIsolated` stays false under COEP): a COEP opt-in gives `SharedArrayBuffer` only on Windows and loopback.
9. **CORP, COOP and `frame-ancestors`** protect only the loopback host in practice (WebView2 and WebKitGTK ignored CORP and, on WebKitGTK, `frame-ancestors` for their custom origins). On desktop the navigation lock is what keeps other origins out.
10. **The secure-scheme registration** was not needed for `isSecureContext` on WebKitGTK 2.52. Keep it: it is cheap, and other checks, such as mixed content, were not compared.
11. **Host-served redirects:** WebKitGTK does not follow a 3xx from a scheme handler, and WebView2 does. Hosts should not rely on redirects.
12. **The pre-compile extractor** must include the project's global usings.

## Recommended default for Spar

Every host-served response (and every Vite dev response):

```
Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self';
  connect-src 'self'; worker-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none';
  frame-ancestors 'none'; object-src 'none'; report-uri /_spar/csp-report
X-Content-Type-Options: nosniff            (exact Content-Type; on Linux also in the header set)
Cache-Control: no-store
Referrer-Policy: no-referrer
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Resource-Policy: same-origin
Permissions-Policy: camera=(), microphone=(), geolocation=(), usb=(), serial=(), hid=(), bluetooth=(), payment=(), display-capture=()
```

- **Dev:** add `'nonce-…'` to `script-src` and `style-src`, with no-store HTML.
- **SRI builds:** add the import map's `'sha256-…'` to `script-src`.
- **Opt-ins declared in C#:** `FrameSrc`, `'wasm-unsafe-eval'`, external hosts, `Allow(Permission.X)`, COEP, Trusted Types.
- **Hosts:** the navigation lock with the WebView2 document filter; new windows always refused; permission events denied by default; `build.assetsInlineLimit = 0`.

## Open choices for the user

1. Trusted Types on by default? React production and the Vite dev client worked on all engines. The cost is that apps creating workers or script URLs need a TT policy.
2. Same-origin iframes: blocked by default (current base), or add `frame-src 'self'` to the base?
3. WebAssembly: keep it blocked by default (an explicit `'wasm-unsafe-eval'` opt-in), knowing that WebKitGTK does not enforce it?
4. Reporting channel: `report-uri` to a host route on every host, the injected listener on desktop, or both. `report-uri` is deprecated in the specification, but it was the only channel that worked everywhere here.
5. Whether to make SRI the default (an extra ~75 ms per build here) or an opt-in, given it mostly catches packing faults for self-served files.
6. How to handle camera and microphone on WebView2, given that the deny handler did not see the request (needs a follow-up check that cannot reach a person, for example on a machine without a camera, or with the Windows camera privacy setting off).

## Observed, skipped, not tested

**Observed:** everything in the tables above. Run counts:
- WebView2: 38 logs, including all 10 probe profiles, 7 React/SRI/inline runs, 4 navigation runs (with and without the document filter, one real `ShellExecuteEx`), 2 cross-origin page runs, 7 dev-server runs and 1 Fast Refresh run.
- Loopback: 26 logs.
- WebKitGTK 4.1: 40 logs; 6.0: 34 logs.
- Vite dev in Chromium: 4 runs.
- AOT publishes with 0 warnings: Windows host; Linux host GTK3 and GTK4.

**Skipped on purpose:**
- the camera request on Windows (desktop and loopback) after the incident;
- navigation-lock runs on loopback (that is the user's own browser: there is no lock there; one run showed Chromium simply navigating away).

**Not tested:**
- Whether the desktop prompt was the Windows privacy prompt or something else (not seen); the WebView2 camera/microphone path through `PermissionRequested`; the WebKitGTK camera path (no device in the container).
- `report-uri` and `report-to` together on Chromium; Reporting API delivery after more than 75 s.
- `Referrer-Policy` to an https external host from WebView2; `frame-ancestors` on WebView2 with an external top page (the frame's subresources were not intercepted, so it was inconclusive).
- COOP on desktop (no popups exist).
- Subresource redirects from a WebKitGTK scheme handler.
- The XDG OpenURI portal and a real Linux desktop (Wayland, a real browser handler).
- Fast Refresh of CSS and the error overlay inside WebView2 and WebKitGTK (only component updates there; the full flow was run in Chromium).
- Vite dev in WebKitGTK from a loopback origin (the container used `http://host.docker.internal`, which is not a secure context).
- Vite `?worker` and other worker creation under Trusted Types.
- SRI cost on a large bundle.
- IDE squiggles for the analyzer.
- A test proving that dev-only plugin code is absent from production builds.
- Whether `AddScriptToExecuteOnDocumentCreated` runs in child frames.

## Downloads and leftovers

- **Downloads:** none from the network.
  - npm: `npm install --offline` installed 24 packages from the local cache into `frontend/node_modules` (93 MB): React, Vite 8.3.8, `@vitejs/plugin-react`, TypeScript 7.0.2, `playwright-core` 1.64.1, and their dependencies. `playwright-core` is new to this project; it reused the installed `chromium-headless-shell` 1248.
  - NuGet: nothing new (the copied volume stayed at 516 MB).
  - Docker: no image built.
- **Removed:**
  - container `spar-csp-linux`, volumes `spar-csp-work` and `spar-csp-nuget`;
  - WebView2 and WebKit profiles `artifacts/webview2-profiles/csp-navigation-{desktop,linux-gtk3,linux-gtk4}`, after the browser had exited;
  - the scratch Playwright check and temporary backups;
  - every host, Vite and Node process this spike started (one Vite server I had left running was stopped).
- **Left behind:**
  - this folder, with `bin/`, `obj/` (including `obj/spar/client/csp.json`), `frontend/node_modules` and the `dist*` builds;
  - `artifacts/csp-navigation/` (logs, about 1.5 MB);
  - one tab in the user's default browser, opened by the single `ShellExecuteEx` check at `http://127.0.0.1:5180/opened?p=openExternal` (that server is gone).
- **Not touched:** `docs/` and other spikes (pitfalls proposed in the report, not written). Containers and volumes of other spikes (`spar-theme-startup*`) were left alone.

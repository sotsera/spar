# Desktop-origin spike: results

Recorded 2026-11-14. Question: does `https://app.localhost` hold up as the Windows desktop origin (decision of 2026-11-14)? What escapes the host's `WebResourceRequested` handler, does stored data survive restarts and an update, which secure-context APIs work, how does Chromium's local network access logic treat the page, and how fast do the candidate names load? Added during the run: can Debug keep `https://app.localhost` by proxying Vite through the host?

**Answer: `app.localhost` holds up, with three host requirements and one optional hardening.**
- Every request the handler leaves unanswered goes to the network: Chromium resolves `*.localhost` itself and connects to `[::1]:443` (TLS, SNI `app.localhost`). Answering every request (404 for unknown paths) closes this for everything the filter sees.
- The legacy filter (`AddWebResourceRequestedFilter`) **does not see shared-worker and service-worker requests**: service-worker registration fails and shared-worker fetches leak. `AddWebResourceRequestedFilterWithRequestSourceKinds(…, ALL)` (`ICoreWebView2_22`) sees all of them.
- WebSockets are never intercepted, and CSP `'self'` allows `wss://app.localhost`. `connect-src https://app.localhost` (explicit scheme) blocked every WebSocket, plain-`http` and other-`.localhost` probe.
- Even with every request answered, **each top-level or frame navigation opens 2 TLS connections to `[::1]:443`** (speculative preconnects; SNI only, no request). Only `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` stopped them. With it, the whole probe produced **0 loopback connections**, everything the host serves still worked, and page load did not change (66.3 ms against 68.5 ms).
- Stored data (`localStorage`, IndexedDB, a `__Host-` cookie, a saved notification grant) survived a restart and a different executable version on the same profile. `app2.localhost` saw none of it.
- `app.localhost` is a secure context. `crypto.subtle`, OPFS, service workers, Web Locks and `navigator.storage.persist()` (through the permission event) worked.
- Local network access: for documents served by the host, Chromium's client address space is `unknown`, and every loopback request was `allowed-by-policy-allow`. There was no prompt, no block and no permission event, for `app.localhost` and `app.spar.invalid` alike. LNA gives the page no protection here; CSP is the only gate.
- Load time: `app.localhost`, `app.spar.invalid` and `app.example` are equal (medians 67–69 ms). `app.spar.local` was 2.8× slower today (median 190 ms, range 108–315 ms): the `.local` penalty came back partly, not at 2 s.
- Debug through an in-process proxy works: Fast Refresh, CSS updates and the error overlay all worked through the proxy, with the same latencies. Page load is about 36 ms slower (153 ms against 117 ms median). Vite's HMR settings must point the client at `ws://127.0.0.1:<port>`. With the defaults it tries `wss://app.localhost`, which leaks to `[::1]:443` and gets no hot updates.

## Environment

| Item | Value |
|---|---|
| OS | Windows 11 Pro 10.0.26320, 32 CPUs |
| WebView2 Runtime | **157.0.4341.46** (`GetBrowserVersionString`; user agent `Chrome/157.0.0.0 … Edg/157.0.0.0`) |
| WebView2 SDK headers | Microsoft.Web.WebView2 1.0.4314.35 (hand-declared `[GeneratedComInterface]`, no WebView2 assemblies) |
| .NET | SDK 11.0.100, NativeAOT win-x64. Repository `Directory.Build.props` (`TreatWarningsAsErrors`); **0 warnings**, no suppressions, no analyzer overrides |
| Node | 24.21.0 (listener, drivers); Vite 8.3.8 with the React spike's `node_modules` copied (no download) |
| Network | Wi-Fi `172.20.10.7` (a phone hotspot range) plus `Ethernet 2` `172.20.10.3`, Hyper-V and WSL adapters. Not changed. |
| Ports | 80 and 443 were free on both loopback addresses before the run |
| Other load | Two other spikes (`csp-navigation`, `theme-startup`) were running WebView2 hosts on the same origin; see "Interference" below |

## What was built

| Part | Role |
|---|---|
| `Origin.Win/` | Minimal WebView2 NativeAOT host. `Interop/`, `UiContext.cs` and `Desktop.targets` are copied from `spikes/asset-packing/Pack.Win` (itself from `spikes/react`). Added declarations: `PermissionRequested` and `ICoreWebView2PermissionRequestedEventArgs3` (flat, `SavesInProfile`), `ICoreWebView2WebResourceRequestedEventArgs2` (`RequestedSourceKind`), `ICoreWebView2_22` declared flat (120 placeholders, then `AddWebResourceRequestedFilterWithRequestSourceKinds` at slot 123, counted from the C vtable), `PutAreDefaultScriptDialogsEnabled`, `GetBrowserVersionString`. |
| `Origin.Win/Host.cs` | Offscreen (`-32000,-32000`), `WS_EX_NOACTIVATE`, shown with `SW_SHOWNOACTIVATE`. Default script dialogs are off. **Every permission request is settled in `PermissionRequested`:** kinds listed in `--grant` are allowed and saved, everything else is denied and not saved. Serves embedded `www/` files and logs every request (context, source kind, cookies). `/unanswered/*` is deliberately left unanswered. Options: `--origin`, `--profile` (must start with `desktop-origin`), `--page`, `--url`, `--filter legacy\|sourcekinds`, `--csp`, `--grant`, `--browser-args` (sets `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` in-process; diagnostics only), `--proxy` (Debug proxy to Vite), `--extra-filter`, `--stay`, `--stop-file`, `--timeout`. It waits for the browser process to exit. |
| `Origin.Win/www/probe/` | `probe.js` with modes `leaks`, `store-write`, `store-read`, `secure`, `lna`; dedicated, shared and service worker scripts. |
| `Origin.Win/www/index.html`, `assets/` | The React spike's Release bundle (from `spikes/asset-packing/inputs/react`), plus an inline script that adds navigation-start-relative DOMContentLoaded and ready times to the bundle's `POST /_spar/ready`. |
| `tools/listener.mjs` | Accepts on `127.0.0.1` and `[::1]`, ports 443 and 80. Logs the TLS SNI (443) or request line and `Host` (80), then closes; it never speaks TLS. Each connection is attributed to its process (`netstat`) and that process to its WebView2 user-data folder (command line). With `--lna-port` it also runs a CORS-permissive HTTP server on `127.0.0.1:47811`. |
| `tools/leak-summary.mjs`, `load-summary.mjs`, `netlog-summary.mjs` | Match listener connections to probe tests by time; medians and ranges; net log extraction. |
| `vite-app/` | The React spike's frontend (`src/`, `index.html`, `tsconfig.json`, `package.json`, a dereferenced copy of its `node_modules`), with a spike `vite.config.ts` (port 5199; `SPAR_HMR_DIRECT=1` sets `server.hmr` to `ws`, `127.0.0.1`, port and clientPort 5199) and `src/spike-hmr.ts`, which reports HMR milestones to `/_spar/hmr`. |
| `tools/hmr-driver.mjs` | Edits `Header.tsx` (5 text changes), `spike.css` (5 colour changes), then a syntax error and its fix, and measures edit → page from the host's log. It restores the sources. |
| `scripts/` | `host.sh`, `with-listener.sh`, `load-times.sh`, `dev-mode.sh`. Outputs go to `artifacts/desktop-origin/`; profiles to `artifacts/webview2-profiles/desktop-origin-*`. |

Build and run (from `spikes/desktop-origin`, Git Bash):

```sh
dotnet publish Origin.Win/Origin.Win.csproj -c Release                                      # bin/v1, version 1.0.0
dotnet publish Origin.Win/Origin.Win.csproj -c Release -p:SpikeVariant=v2 -p:Version=2.0.0   # bin/v2
dotnet publish Origin.Win/Origin.Win.csproj -c Release -p:SpikeVariant=dev                  # bin/dev (question 6)

# 1. Leaks (listener on during the run only)
scripts/with-listener.sh leaks-legacy scripts/host.sh v1 leaks-legacy --profile desktop-origin-leaks-legacy --page 'probe/probe.html?mode=leaks' --timeout 120
scripts/with-listener.sh leaks-sourcekinds scripts/host.sh v1 leaks-sourcekinds --profile desktop-origin-leaks-sourcekinds --filter sourcekinds --page 'probe/probe.html?mode=leaks' --timeout 120
scripts/with-listener.sh csp-self  scripts/host.sh v1 leaks-csp-self  ... --filter sourcekinds --csp "connect-src 'self'; img-src 'self'; frame-src 'self'; worker-src 'self'"
scripts/with-listener.sh csp-https scripts/host.sh v1 leaks-csp-https ... --filter sourcekinds --csp "connect-src https://app.localhost; img-src https://app.localhost; frame-src https://app.localhost; worker-src https://app.localhost"
scripts/with-listener.sh resolver2 scripts/host.sh v1 leaks-resolver2 ... --filter sourcekinds --browser-args '--host-resolver-rules="MAP app.localhost ~NOTFOUND, MAP other.localhost ~NOTFOUND"'
node tools/leak-summary.mjs ../../artifacts/desktop-origin/leaks-legacy.log ../../artifacts/desktop-origin/listener-leaks-legacy.log desktop-origin-leaks-legacy
# 2. Stored data: store-write (v1, --grant 4,12), store-read (v1), store-read (v2), store-read on app2.localhost (v1), store-read (v2)
# 3. scripts/host.sh v1 secure-localhost --profile desktop-origin-secure --filter sourcekinds --page 'probe/probe.html?mode=secure'
# 4. scripts/with-listener.sh lna-<name> scripts/host.sh v1 lna-<name> --origin https://<name> --page 'probe/probe.html?mode=lna&port=47811&lan=172.20.10.7'
#    plus the same with --browser-args "--log-net-log=\"<abs path>\" --net-log-capture-mode=Default" for the net log
# 5. scripts/load-times.sh load-times 10 && node tools/load-summary.mjs ../../artifacts/desktop-origin/load-times.log
# 6. scripts/dev-mode.sh proxy 6 5; scripts/dev-mode.sh direct 6 5; scripts/with-listener.sh dev-proxy-default scripts/dev-mode.sh proxy-default 1 2
```

## 1. Requests the host does not answer (observed)

The listener held each connection until the first bytes arrived, then closed it, so a leaked request fails quickly. Connections are attributed to this spike's browser process by PID and profile folder. Every leak went to **`[::1]`**. There were **0 connections to `127.0.0.1`** in every run, because `[::1]:443` accepted first. Each leaked HTTPS request produced **2** connections (Chromium retries once after the closed handshake).

`ctx` is `COREWEBVIEW2_WEB_RESOURCE_CONTEXT`; `src` is the requested source kind (1 document, 2 shared worker, 4 service worker).

| Request | Reaches the handler, legacy filter | Reaches the handler, source-kinds filter | Unanswered or not interceptable: loopback connections | Page sees |
|---|---|---|---|---|
| Missing path (host answers 404) | yes, ctx 7 | yes | **0** | `status 404` |
| `fetch`, unanswered | yes, ctx 7 | yes | 2 × `[::1]:443`, SNI `app.localhost` | `TypeError: Failed to fetch` |
| `<img>`, unanswered | yes, ctx 3 | yes | 2 | `error` |
| `<iframe>`, unanswered | yes, ctx 1 | yes | 2 | `load`, but a cross-origin error page |
| `<iframe>`, **answered** | yes, ctx 1 | yes | **2 (preconnect)** | same-origin document |
| `sendBeacon`, answered path | yes, ctx 14, `POST` | yes | 0 | `queued=true` |
| `sendBeacon`, unanswered | yes | yes | 2 | `queued=true` (the page never learns) |
| `EventSource`, answered | yes, ctx 7 | yes | 0 | `open`, `message hello`, then `error` (the response ends; no streaming) |
| `EventSource`, unanswered | yes | yes | 2 | `error` |
| `new WebSocket('wss://app.localhost/x')` | **never** | **never** | 2 × `[::1]:443` | `error`, close 1006 |
| `new WebSocket('ws://app.localhost/x')` | never | never | 1 × `[::1]:80`, **plaintext `GET /x`, `Host: app.localhost`** | `error`, close 1006 |
| `fetch('http://app.localhost/…')` (outside the `https` filter) | no | no | 1 × `[::1]:80`, **plaintext request line with the path** | `TypeError` |
| `fetch('https://other.localhost/x')` | no | no | 2, SNI `other.localhost` | `TypeError` |
| Dedicated worker: script and fetches | yes (script ctx 16, src 1) | yes | 0 answered; 2 for its unanswered fetch | answered `200` |
| **Shared worker fetches** | script yes; **fetches no** | yes, src 2 | legacy: **4** (both fetches, the answered path included); source kinds: 2 (the unanswered one) | legacy: both fail |
| **Service worker** | **script fetch not seen: registration fails** | yes: script, its fetches and `fetch(event.request)`, src 4 | legacy: 2 (the script); source kinds: 2 (its unanswered fetch) | legacy: `TypeError: Failed to register a ServiceWorker … An unknown error occurred when fetching the script.`; source kinds: registration ok, fetches `200` |
| Page load itself (all answered) | — | — | **2 at `Navigate`, before the first `WebResourceRequested`** | — |

The `ws://` and `http://` cases are not blocked as mixed content: `*.localhost` counts as potentially trustworthy, so plaintext requests leave with their paths. Cookies marked `Secure` are not sent over them; non-`Secure` cookies would be (not checked).

### Blocking what leaks (observed, source-kinds filter)

| Variant | Loopback connections from the probe | Notes |
|---|---:|---|
| No CSP | 26 | Everything in the table above |
| CSP `connect-src 'self'` (+ img, frame, worker) | 22 | Blocks `ws://`, `http://app.localhost` and `other.localhost`. **`wss://app.localhost` still leaks** (2): `'self'` matches `wss` on an `https` page |
| CSP `connect-src https://app.localhost` (+ img, frame, worker) | 20 | Blocks every WebSocket, plain-`http` and other-name probe. Left: preconnects (page load, answered iframe) and the deliberately unanswered same-origin requests |
| `--host-resolver-rules="MAP app.localhost ~NOTFOUND, MAP other.localhost ~NOTFOUND"` (no CSP) | **0** | The 16 probe outcomes are the same as without the rule (answered requests still served, unanswered ones fail), and no preconnect leaves. Load time did not change (question 5) |

The resolver rule must be quoted inside the argument (`--host-resolver-rules="MAP …"`). Unquoted, Chromium split it at the spaces and silently kept only `--host-resolver-rules=MAP` (first attempt, `leaks-resolver.log`: no effect).

**Proposed host rules:**
1. Register the origin filter with `AddWebResourceRequestedFilterWithRequestSourceKinds(origin + "/*", ALL, ALL)` (`ICoreWebView2_22`). If that interface is missing, either refuse to start or block workers (not tested). The minimum Runtime for `ICoreWebView2_22` was not checked.
2. Answer every request to the origin, with a 404 for unknown paths (confirmed: an answered 404 never leaked).
3. The default CSP should name the origin with its scheme: `connect-src https://app.localhost` (and the same for the other fetch directives, or keep `'self'` there, since they cannot reach `wss`). Do not use `'self'` in `connect-src`. On Linux, `spar://app` needs its own check.
4. Recommended hardening: pass `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` through `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`. It is the only measure that stopped the navigation preconnects, and it covers anything CSP misses (for example, an app that overrides CSP). Tested only through the environment variable set in-process. Not tested: setting it through the options object, how it combines with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, and whether Microsoft documents it as supported in WebView2.
5. WebSockets can't be refused in the host (no WebView2 event sees them); CSP and the resolver rule are the controls.

What a leak exposes: a TLS ClientHello with SNI `app.localhost` to whatever listens on `[::1]:443` or `127.0.0.1:443`. Without a certificate the WebView trusts, no HTTPS request content leaves. The plaintext `ws://` and `http://app.localhost` cases do send the request line and headers.

### Interference

The listener received connections from the other spikes' hosts (`theme-startup-*` profiles: preconnect bursts of 4–6 every few seconds; 3–14 connections per run). The first, unattributed listener log (`listener-run1-unattributed.log`) is dominated by them, which is why attribution was added. Those hosts got an accepted-then-closed connection instead of a refusal while the listener ran (about 30 short windows, each under 3 minutes). Their measurements over those windows may differ slightly.

## 2. Stored data across restarts and an update (observed)

One profile, `desktop-origin-store`, used in order. The source-kinds filter was on. `--grant 4,12` allowed notifications and window management, saved.

| Step | Exe | Origin | `localStorage` | IndexedDB | Cookies (`document.cookie`) | Cookie header the host received | `Notification.permission` / `notifications` | Permission events |
|---|---|---|---|---|---|---|---|---|
| 1. write | v1 (1.0.0.0) | `app.localhost` | `s1` written | `s1` written | `__Host-spar=s1; plain-spar=s1` | yes, on the next request | `granted` | 1: kind 4 (notifications), `user-initiated=0`, allowed and saved |
| 2. restart | v1 | `app.localhost` | `s1` | `s1` | both | yes, from the first request (`probe.html`) | `granted` | **none** (remembered) |
| 3. update | **v2 (2.0.0.0, different hash, different folder)** | `app.localhost` | `s1` | `s1` | both | yes | `granted` | none |
| 4. control | v1 | **`app2.localhost`** | `null` | no database (`databases=[]`) | empty | none | `default` / `prompt` | none |
| 5. again | v2 | `app.localhost` | `s1` | `s1` | both | yes | `granted` | none |

- `__Host-spar` (`Secure; Path=/; SameSite=Strict; Max-Age=31536000`) was accepted on `https://app.localhost` and sent to `WebResourceRequested` in the `Cookie` header.
- `Notification.requestPermission()` without a user gesture reached `PermissionRequested`, and no prompt appeared. `getScreenDetails()` (window management) rejected with `NotAllowedError: Transient activation is required` before any permission event, so only one permission was granted.
- The browser took **6.48–6.51 s** to exit after close in all five storage runs, against 245–273 ms in the probe runs (the existing 6.5 s pitfall). The host waited each time before the profile was reused.

## 3. Secure context and APIs (observed; `app.spar.invalid` gave identical results)

| Check | Result on `https://app.localhost` |
|---|---|
| `isSecureContext` | `true` |
| `crossOriginIsolated` | `false` (no COOP/COEP sent) |
| `crypto.subtle` | `digest("SHA-256")` ok; `generateKey(AES-GCM 256)` ok; `randomUUID` available |
| Clipboard API | present (`read`, `readText`, `write`, `writeText`, `onclipboardchange`). `clipboard-write` permission `granted`, `clipboard-read` `prompt`. `readText()` rejected with **`NotAllowedError: Document is not focused`** (offscreen, never-activated window) before any permission event. `writeText` was deliberately not called, so the user's clipboard was never touched |
| `navigator.storage.persist()` | raised `PermissionRequested` kind **13** (persistent storage). Denied: `false`. With `--grant 13`: `true`, and `persisted()` became `true` |
| `navigator.storage.estimate()` | usage 0, quota about 11 GB |
| OPFS (`storage.getDirectory()`) | ok |
| Service workers, Web Locks | available (service worker registration works with the source-kinds filter, question 1) |

## 4. Local network access (observed)

The page fetched a CORS-permissive server on `127.0.0.1:47811` and the machine's own Wi-Fi address `172.20.10.7:47811` (nothing listens there, see "Not tested"), from `https://app.localhost` and `https://app.spar.invalid`.

| Request | `app.localhost` | `app.spar.invalid` |
|---|---|---|
| `fetch http://127.0.0.1:47811/lna` | `200 cors`, no preflight, no LNA headers | same |
| with `targetAddressSpace: "loopback"` | `200` | same |
| `fetch http://localhost:47811/lna` | `200` | same |
| `<img src=http://127.0.0.1:47811/…>` | request reached the server (`sec-fetch-storage-access: active`) | same |
| `fetch http://172.20.10.7:47811/lna` (plain, `targetAddressSpace: "local"`, `no-cors`) | pending for the probe's 14 s each. The net log shows a plain TCP connect, `os_error 10060` after 21 s. A Node `net.connect` to the same address was also still pending after 5 s: the machine drops the SYN. No LNA step ran | same |
| `PermissionRequested` events | **none** | **none** |
| `navigator.permissions.query` for `local-network-access`, `local-network`, `loopback-network` | all `prompt`, before and after | same |
| Net log `LOCAL_NETWORK_ACCESS_CHECK` for the loopback requests | `client_address_space: "unknown"`, `resource_address_space: "loopback"`, **`result: "allowed-by-policy-allow"`** | same |

- **Chromium 157 in WebView2 neither prompts nor blocks, and raises no permission event, for either name.** Documents served through `WebResourceRequested` have no IP, so their address space is `unknown`, and the policy allows loopback. The `.invalid` name made no difference, so the reason the decision log gave for rejecting it does not show here.
- Consequence: nothing in the browser stops the page from calling loopback services (another app's local API, the dev server). The strict `connect-src` is the control, which strengthens the case for it.
- The same net logs show runtime traffic the app did not ask for: SmartScreen `nav-edge.smartscreen.microsoft.com/api/browser/edge/navigate/3` on navigation, `data-edge` and `telem-edge.smartscreen…`, `config.edge.skype.com/config/v1/Edge/157.0.4341.46`, and `wpad` lookups (5 per run). Chromium also ran 4–5 host resolutions for the app origin itself (`https://app.localhost`, `app.localhost:443`).

## 5. Load times (observed)

The React bundle (2 requests) was loaded in the offscreen host, with one profile per name. Each round runs every name in turn (11 rounds; round 0 warms each profile and is reported apart). All 55 runs succeeded. Page times are measured from navigation start; the host time is from `Navigate()` to the arrival of `POST /_spar/ready`.

| Name | Runs | Navigation start → DOMContentLoaded ms, median (range) | Navigation start → `ui-ready` ms | Host `Navigate()` → ready ms | Round 0 (cold profile) ms |
|---|---:|---:|---:|---:|---:|
| `app.localhost` | 10 | 55.9 (50.4–59.8) | 66.5 (61.6–72.2) | 68.5 (65.6–75.6) | 87.1 |
| `app.spar.invalid` | 10 | 55.0 (43.0–60.9) | 65.7 (53.1–71.5) | 67.3 (54.7–73.3) | 96.0 |
| `app.example` | 10 | 56.0 (53.6–67.6) | 66.5 (63.7–79.4) | 68.8 (65.5–81.9) | 90.8 |
| `app.spar.local` | 10 | **176.4 (94.3–302.0)** | **187.9 (105.8–313.2)** | **190.0 (107.7–315.3)** | 111.5 |
| `app.localhost` + resolver rule | 10 | 54.5 (52.6–67.4) | 64.5 (62.4–78.8) | 66.3 (63.8–81.9) | 126.4 |

- `app.spar.local` was slower in every round (108–315 ms against at most 82 ms for any other name). `Resolve-DnsName` took 362, 217, 42 and 45 ms for it (against 142 ms for `app.example` and 19 ms for `app.spar.invalid`). The earlier 2 s condition did not recur: the network was not changed, and the exact condition behind the 2.0 s result could not be reproduced on purpose. Today's network is a phone hotspot on Wi-Fi plus a wired adapter on the same subnet.
- `app.example` was as fast as `app.localhost` here, but `Resolve-DnsName app.example` did go to the resolver (142 ms), so it depends on the network. The decision log's reason to reject it stands.
- The resolver rule costs nothing measurable.

## 6. Debug: keep `https://app.localhost` and proxy Vite (observed)

The React spike's frontend ran on Vite 8.3.8 on `127.0.0.1:5199`, started as `DevServer.cs` starts it (`node node_modules/vite/bin/vite.js`), and was shown in the host:
- **direct:** `Navigate("http://127.0.0.1:5199/")`;
- **proxy:** the page stays on `https://app.localhost`, and the host answers every request by fetching it from Vite (deferral, `HttpClient`, back to the STA for `PutResponse`).

The page reported HMR milestones (wall clock) to `/_spar/hmr`, and a driver in another process edited the sources. Each mode had 6 page loads and 5 edits of each kind.

| Measure | Direct | Proxy (`server.hmr`: `ws`, `127.0.0.1`, port and clientPort 5199) |
|---|---:|---:|
| Page load, navigation start → DOMContentLoaded, median (range) | 98.1 (88.2–112.1) ms | 130.6 (120.0–171.2) ms |
| Page load, host `Navigate()` → ready | 117.3 (106.0–129.2) ms | 153.2 (136.4–188.7) ms |
| Fast Refresh: file write → new text in the DOM | 25 (22–26) ms | 23 (22–29) ms |
| Component state kept across Fast Refresh | yes, 5/5 | yes, 5/5 |
| CSS hot update: write → computed colour changed | 5 (4–5) ms | 9 (5–12) ms |
| Error overlay: syntax error → overlay shown | 8 ms | 9 ms |
| Fix → new DOM / overlay gone | 22 / 2 ms | 32 / 3 ms |
| Requests per load | 18 module requests | 18, each through the proxy (0.5–15.5 ms each; the 3.1 MB `react-dom_client.js` took 15.5 ms) |

- With **Vite's default HMR settings** behind the proxy (`proxy-default`), the client derived its socket from the page URL and tried `wss://app.localhost`. That produced 6 loopback connections to `[::1]:443` from this profile, and **no hot update arrived** (0/2 edits, no overlay).
- In proxy mode, Vite accepted the WebSocket from a page whose origin is `https://app.localhost`. `ws://127.0.0.1:5199` from an `https` page is allowed: `127.0.0.1` is potentially trustworthy.
- **CSP in Debug through the proxy:** `connect-src https://app.localhost ws://127.0.0.1:<port>` (the HMR socket). Everything else stays as in Release, because the page's origin, storage, cookies, `'self'` and secure context are those of Release. Not measured: Vite's inline preamble and styles under a nonce through the proxy (the `csp-navigation` spike covers nonces).
- Proxy cost: about 33–36 ms per full load for this app (18 requests), and none on hot updates. The proxy must forward at least `Accept` and the `Sec-Fetch-*` headers, pass `Content-Type`, and never cache.
- Every proxy request runs a deferral that completes on the STA. A slow Vite response holds that request but not the message pump.

## Recommendations

1. **Keep `https://app.localhost`.** It is a secure context, it is never resolved through DNS, it is as fast as any other name, its storage persists across restarts and versions, and it is isolated from other names. Every leak found here is a host or CSP rule, not a property of the name.
2. Add to plan section 3 (Windows, assets):
   - Register the filter with request source kinds (`ICoreWebView2_22`, all kinds); the legacy filter misses shared and service workers.
   - Answer every request to the origin (404 for unknown paths): confirmed necessary, since an unanswered request goes to `[::1]:443`.
   - Name the origin with its scheme in `connect-src` (`https://app.localhost`), never `'self'`, which allows `wss://app.localhost`.
   - Set `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` through `AdditionalBrowserArguments`, as the guard for anything CSP misses and for navigation preconnects. Check support and option-object behaviour in M1.
   - LNA does not protect loopback services from the page (address space `unknown`, allowed by policy). The CSP is the control.
   - Consider `IsReputationCheckingRequired = false` (`ICoreWebView2Settings8`): the Runtime calls SmartScreen on navigation to the app origin (not tested).
   - Debug: keep `https://app.localhost` and proxy Vite through `WebResourceRequested`, with `server.hmr` `{ protocol: "ws", host: "127.0.0.1", port, clientPort: port }` written by the Spar Vite plugin. Debug `connect-src` adds `ws://127.0.0.1:<port>` only. This removes the Debug/Release difference in origin, storage, cookies, CSP `'self'` and secure context, for about 35 ms per full load.
3. The "Rejected" reasoning for `app.spar.invalid` in the 2026-11-14 decision (Chromium LNA might treat it as public) did not show up: LNA treated both names the same. `.invalid` is still a valid fallback name; its single 89 ms sample is now 67 ms over 10 runs.

## Pitfalls found (for `docs/pitfalls.md`)

- **An unanswered `WebResourceRequested` request goes to the network.** Chromium resolves `*.localhost` itself and connects to `[::1]:443` (then `127.0.0.1:443`), two attempts per request.
- **Navigations preconnect even when every request is answered:** 2 TLS connections to `[::1]:443` at each top-level or frame navigation to `app.localhost`. Only `--host-resolver-rules` stopped them.
- **The legacy `AddWebResourceRequestedFilter` misses shared-worker and service-worker requests:** service-worker registration fails with "unknown error when fetching the script", and shared-worker fetches leave. Use the source-kinds overload.
- **CSP `'self'` in `connect-src` allows `wss://` to the same host.** Name `https://app.localhost` explicitly.
- **`http://app.localhost` and `ws://app.localhost` are not mixed content** (`.localhost` is potentially trustworthy). They go in plaintext to `[::1]:80` with the path.
- **A value with spaces in `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` must be quoted inside the value** (`--host-resolver-rules="MAP x ~NOTFOUND"`). Otherwise Chromium silently keeps only the first word.
- **Local network access does not apply to host-served pages:** their client address space is `unknown` and loopback requests are allowed by policy, with no prompt and no event.
- **Clipboard reads in an offscreen, non-activated window fail with "Document is not focused"** before any permission event. Test clipboard permission paths in a focused window.
- **`getScreenDetails()` needs transient activation;** `Notification.requestPermission()` does not, and reaches `PermissionRequested`.
- **Vite's default HMR client behind a host proxy tries `wss://<app origin>`.** It leaks to loopback and gets no updates. Configure `server.hmr` host, port and clientPort.
- **Other apps using the same origin hit a loopback listener too.** When checking for leaks, attribute each connection to its process and profile.
- Tooling: a backgrounded `node` started from Git Bash survived `kill` and the bash task's stop. The next run's Vite failed with "port in use" and the measurement silently used the stray server. Kill by the Windows PID of the listener and wait for the port to be free.

## Not tested

- A real server on the LAN address: binding a non-loopback address could raise a Windows Firewall prompt, so nothing listened there. The TCP connect to the own Wi-Fi address timed out (SYN dropped), so Chromium never reached its LNA check for a `local` resource address.
- The LNA behaviour of a page whose address space is known (for example a page loaded over the network): not relevant to the host, not run.
- Setting `--host-resolver-rules` through `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`, and whether Microsoft documents it as a supported WebView2 flag. `MAP *.localhost ~NOTFOUND` was not tried.
- Behaviour on Runtimes without `ICoreWebView2_22`, and its minimum Runtime version.
- `IsReputationCheckingRequired = false` and whether it stops the SmartScreen calls.
- Clipboard read and write in a focused window; any clipboard write (deliberately, to leave the user's clipboard alone).
- Granting any permission that touches hardware or the OS (camera, microphone, geolocation): deliberately not requested.
- Non-`Secure` cookies over the plaintext `http://app.localhost` leak.
- Vite's inline preamble and nonce under the strict CSP through the proxy; Vite's HMR WebSocket token or origin checks beyond "it connected".
- Repeating the 2 s `.local` result: the network was not changed, by rule.
- Linux (out of scope).

## Left behind

- `artifacts/desktop-origin/`: logs, listener logs and two net logs (about 2 MB). Disposable.
- `artifacts/webview2-profiles/desktop-origin-*`: deleted after the runs (21 profiles, about 251 MB), once no browser process used them.
- `spikes/desktop-origin/vite-app/node_modules`: a 77 MB local copy of the React spike's dependencies (no download). `Origin.Win/bin` and `obj` (three variants).
- No processes left running; the listener, Vite and the hosts were stopped (Vite on port 5199 by its Windows PID).

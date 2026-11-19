# M1b: page security (the policy in C#, the navigation lock, permissions, violation reports, SRI)

Recorded 2026-11-17. What it shows: the Windows host's page security of plan M1b, build step 4, on a test app made for it (`tests/apps/SecurityProbe`) and on the Notebook sample: the policy declared in C# and sent on every response, worker scripts included; the navigation lock and new windows; the permission gate; the host-injected script answering the heartbeat and listening for violations; both violation channels with their flood limits; the style nonce; Subresource Integrity on the built page. Decision "Page security (implementation record; choices for review)".

## Environment

Windows 11 Pro 10.0.26320 (system theme dark), SDK 11.0.100, WebView2 Runtime 157.0.4341.46, `Microsoft.Web.WebView2` 1.0.4314.35, 32 logical CPUs. Every app run was started by [`SecurityRun.cs`](m1b-page-security/SecurityRun.cs) (a file-based C# program, run from a copy outside the repository) with `STARTF_USESHOWWINDOW` and `SW_SHOWNOACTIVATE`, so its window was shown without activation, and moved offscreen (`SetWindowPos` to x = −20000, `SWP_NOACTIVATE`) as soon as it existed, 60–145 ms after the start; standard error went to a file the run read with its own clock; the window was closed with `WM_CLOSE`. No mouse or keyboard input was sent. Each run had `LOCALAPPDATA` in its own folder under `artifacts/webview2-profiles/m1b-page-security/`, so the profile, data and `host.log` stayed there.

**Hung-page dialogs reached the desktop in six Production runs.** A page kept busy for 20 s by 100,000 distinct violations is a hung page, so a Production build showed its "Wait / Reload page" dialog at about 20 s, and "Wait / Close anyway" when the run closed the window (`aot-distinct-100k*.log`, `off-distinct-100k-1.log`, `off-distinct-100k-3.log`). The dialogs are owned by the window, which was offscreen; each was taken down by the host when the page answered again, after 15–25 s, and no input was sent to them. Later long runs used Development builds with `SPAR_AUTOMATED=1`, which show none (pitfall "A Production page busy for 15 s raises the hung-page dialog").

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Hosting/ContentSecurity.cs`, `Permissions.cs`, `src/Spar/Runtime/SparPageSecurity.cs` | `spar.ContentSecurity(csp => …)` with `CspBuilder` and `CspReports`, `spar.Permissions(p => …)` with `PermissionsBuilder` and `Permission`; the declaration the generated catalog carries |
| `src/Spar/Hosting/CspSourceRule.cs` | What a directive method accepts, shared with the generator (linked) |
| `src/Spar/Hosting/PagePolicy.cs`, `PageHtml.cs`, `PageSecuritySetup.cs`, `PageSecurityNames.cs` | The composed policy and headers, the import-map hash and the nonce's place in the HTML, the overrides logged at start and the Development comparison |
| `src/Spar/Hosting/CspViolationLog.cs`, `CspReportReader.cs`, `CspAdvice.cs`, `ReportLimiter.cs` | Both channels into one log, the readers of both formats, the Development advice, the endpoint's rate limit |
| `src/Spar/Bridge/` | The `csp` message (page to host) and `HostAnswersPing` instead of `hello`'s `ping` |
| `src/Spar.Generator/PageSecurityAnalysis.cs`, `PageSecurityAnalyzer.cs`, `Descriptors.cs`, `Emit/CatalogEmitter.cs` | SPAR101–SPAR105 and the catalog's `PageSecurity` |
| `src/Spar.Sdk.Tasks/Client/` | `csp.json` in `@spar/app` |
| `src/Spar.Windows/PageGuard.cs`, `ExternalLinks.cs`, `PageScript.cs`, `PageScript.js`, `PageServer.cs`, `BrowserView.cs`, `WebViewBridge.cs`, `WindowsApp.cs`, `Interop/WebView2.Security.cs` | The navigation lock, new windows and the permission gate; the injected script; the headers, nonce and `report-uri`; the heartbeat armed at the document's creation; `ComObjectWrapper` for the interfaces the host only calls |
| `src/Spar.Client/src/vite/integrity.ts`, `index.ts`, `src/connection.ts`, `src/protocol.ts` | SRI, the import map and the external-integrity check in the Vite plugin; the SDK no longer answers pings; the `csp` type |
| `tests/apps/SecurityProbe/` | The probe app: its page runs the probe `SECURITY_PROBE_MODE` names and reports through its contract to standard error |
| [`m1b-page-security/SecurityRun.cs`](m1b-page-security/SecurityRun.cs), [`scenarios.sh`](m1b-page-security/scenarios.sh), [`logs/`](m1b-page-security/logs/) | The run, every scenario, and the raw logs (the run's clock on the left, the app's standard error on the right) |

**Builds:** `aot` is `dotnet publish tests/apps/SecurityProbe -c Release` (NativeAOT, Production, 4,161,536 B); `dev` is its Debug build with `SparDevServer=false` (CoreCLR, Development: stall probe on); `off` is `aot` with `-p:SecurityProbeListenerOff=true`, which adds `csp.Reporting(listener: CspReports.Off)`; `notebook` is `dotnet publish samples/Notebook -c Release`. The probe app declares `csp.StyleNonce()` and `Permission.Midi`.

## Results (observed)

### The policy and its headers

| Case | Result |
|---|---|
| The policy the probe app sent (`aot-idle.log`) | `default-src 'none'; script-src 'self' 'sha256-…' 'sha256-…'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src https://app.localhost; media-src 'self'; worker-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'; require-trusted-types-for 'script'; report-uri /_spar/csp-report`: each directive once; the two hashes are its two pages' import maps |
| A module worker served by the host (`aot-worker.log`) | in the worker, `eval` and `new Function` threw `EvalError` and WebAssembly `CompileError`: **the CSP header applies to worker scripts on WebView2** (plan step 9). Its violations reached the host through `report-uri` only (3 reports, 0 listener batches), as designed |
| `new Worker(url)` under Trusted Types | refused (`require-trusted-types-for blocked trusted-types-sink ("Worker constructor|…")`) until the address went through a Trusted Types policy (pitfall) |
| The style nonce (`aot-nonce.log`) | `<meta property="csp-nonce">` present, nonce 24 characters; a `<style>` with it applied (`margin-left` 7px), one without it was blocked (`0px`) and logged once, `style-src-elem blocked inline`, although both channels reported it (1 batch, 1 report) |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), clipboard-read=(), accelerometer=(), gyroscope=(), magnetometer=(), local-fonts=(), midi=(self), window-management=(), autoplay=(), usb=(), serial=(), hid=(), bluetooth=(), payment=(), display-capture=()` |

### The navigation lock and permissions

| Case (`aot-navigate.log`, `aot-permissions.log`) | Result |
|---|---|
| `window.open("https://example.com/…")` from script | new window blocked, logged ("only a user's click opens an external address") |
| A `target="_blank"` link clicked from script | blocked the same way (not user-initiated) |
| `location.href = "https://example.com/…"` | navigation cancelled, logged |
| `location.href = "mailto:…"` | navigation cancelled, logged; no mail program opened |
| The page afterwards | still on `https://app.localhost/` |
| `Notification.requestPermission()` | `denied`; `PermissionRequested` (notifications) denied, logged once |
| `navigator.geolocation.getCurrentPosition` | `PERMISSION_DENIED` from `geolocation=()`, with no host event |
| `navigator.requestMIDIAccess({ sysex: true })` | granted: declared `Permission.Midi`, `PermissionRequested` allowed |

A user's click on an external link (the `ShellExecuteW` path) was **not** exercised: it needs real input.

### The heartbeat from the document's creation

A page that navigates to `hang.html`, whose module loops in its start-up code before it imports the SDK (`dev-hang-early-1..3.log`, Development, automated mode): the new document was opened and armed at once ("Heartbeat armed for the new document"), and "The page is not responding" came **19.6, 19.6 and 19.7 s** after that document opened, every run. With step 3's arming on `hello`, this page was never pinged.

### Flood tests and the numbers

| Case | Page | Listener | Host log | `report-uri` |
|---|---|---|---|---|
| 100,000 identical violations (one `style` attribute line; `aot-flood`, `dev-flood`) | raised in 6.5–9.4 ms | **1 event**: the engine deduplicates identical violations per document | 1 line | 1 report |
| 1,000 distinct (`aot-distinct`, `dev-distinct`) | raised in 1.9–4.8 ms | 1 batch: 100 groups, overflow 900 | 100 lines, then 1 summary ("past the group limit: 900") | 89 reached the host: 20 read, 69 over the rate limit, unread |
| 100,000 distinct, listener off (`off-distinct-100k-1..3`) | the engine kept the page busy **19.7–20.2 s** | – | 100 lines from `report-uri` | 89 (20 + 69) |
| 100,000 distinct, listener counting all (first version; `aot-distinct-100k-1..3`) | busy 19.3–19.9 s, then **about 9.5 s more** before it answered a ping | 1 batch | 100 lines + summary | 89 |
| 100,000 distinct, listener stopping after 1,000 (final; `dev-distinct-100k`) | 20.07 s, answering pings throughout (5 pings, no alarm) | 1 batch, `stopped` | 100 lines, "listener stopped counting", summary | 89 |

- **UI thread:** the heartbeat's longest tick 0.07–0.29 ms on NativeAOT and 0.77–1.37 ms on CoreCLR Debug in these runs; the Development stall probe logged no stall in any flood run (one 52–57 ms stall at start-up, outside Spar's callbacks, before the page existed, in every Debug run). `report-uri` costs the UI thread a body copy of at most 16 KiB and a token-bucket check; the listener costs it one web message a second at most.
- **Bridge messages:** one `csp` message per document in every case above (the flush interval is 1 s, the first after 100 ms).
- **Log lines:** at most 100 per document for new groups, plus a "stopped" line, plus a summary every 10 s while counts grow, plus one totals line at exit.
- **Memory:** the app process's working set 24.2 MB idle, 25.2 MB after 1,000 distinct, 29.9 MB after 100,000 distinct, the same with the listener off (29.7–29.9 MB), so the growth is the engine's reports, not Spar's counting.
- **The limits chosen from this:** listener interval 1 s, first batch after 100 ms, 100 groups per document, stop after 1,000 violations; host 100 groups per document, summaries every 10 s, `report-uri` 16 KiB a body and 20 at once then 5 a second, a queue of 256 for parsing.

### Start-up

Adding the injected script before the first navigation: from the controller to "page script added" 41–59 ms, to the first document 102–137 ms (12 Production runs); step 3's logs had 100–121 ms from the controller to the first document without it. `ui-ready` 330–428 ms after the host started.

### The Notebook

- `dotnet publish samples/Notebook -c Release`: 0 warnings, **4,940,288 B** (4,849,664 B before this step, +90,624 B). On the way it was 5,146,624 B, then 5,069,824 B, over the 5 MB budget: generic code for enum collections, a struct channel item and value-type log arguments (each replaced), and the host's interop, where declaring the interfaces it only calls as `ComObjectWrapper`, together with string-only log messages, cut the last 129,536 B, existing interop included.
- Its built page (`obj/…/frontend/index.html`): an import map with the lazy `Preview` chunk's sha384 first in `<head>`, and `integrity` on the entry script and the stylesheet.
- Run as a Production executable (`notebook.log`): `ui-ready` at 391 ms and **0 Content Security Policy violations** (no report from either channel) over 8 s, under the full policy with its import map hash.
- Its browser journeys (`samples/Notebook.EndToEndTests`, loopback Chromium, the same composed policy from the catalog): 7 of 7 passed.

## Not verified

- A user's click on an external link, and `window.open` within a click handler (no real input here); whether WebView2 reports such a request as user-initiated.
- `about:blank` and `about:srcdoc` frames, and frame navigations with `FrameSrc` declared.
- `LaunchingExternalUriScheme`, which the lock does not handle (cancelled navigations never reached it here).
- `AllowWebAssembly()`, `AllowEval()`, `StyleNonce()` beside `AllowInlineStyles()` and `WithoutTrustedTypes()` on a real page (unit-tested only).
- A Runtime without `ICoreWebView2PermissionRequestedEventArgs3`; the camera and microphone (never asked for here).
- The Vite development server's policy header (M1d); SRI on a large bundle; lazy CSS, which import maps do not cover.
- The heartbeat for an error page; the Linux host.

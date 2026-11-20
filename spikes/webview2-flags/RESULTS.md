# WebView2 flags spike: results

Recorded 2026-11-19 for plan section "M1b", step 9 ("Checks this plan deferred to M1"). Four questions:
1. Does `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` work through the environment options' `AdditionalBrowserArguments`, combined with Spar's other arguments, and what does it cost?
2. Does `IsReputationCheckingRequired = false` stop every SmartScreen request in a net log?
3. Does `--js-flags=--noexpose_wasm` remove `WebAssembly` in every context?
4. What is the find API's minimum Runtime, and can its default dialog be suppressed?

**Answers**
1. **Yes, and it costs nothing measurable at the median.** Through the options object, quoted inside the argument (`--host-resolver-rules="MAP app.localhost ~NOTFOUND"`), before or after `--disable-blink-features=FileSystemAccessLocal`, it gave **0 loopback connections in all 253 runs the listener watched**, against a median of 16 per run (3 navigations) and 6 per run (1 navigation) without it; every request the host answers still loaded. Unquoted, the browser keeps only `--host-resolver-rules=MAP` and it has no effect. First Navigate to page-ready: 53.5 ms median with the rule against 54.2 ms without (25 runs each), and 52.9–55.2 against 52.8–55.3 in three further pairs of 60. About 5 s first-navigation stalls occur with and without it at the same rate. A strictly alternated 400 + 400 run with fresh profiles gave 5 with the rule and 4 without (p = 1.0); see "Rare 5 s stalls".
2. **Yes.** With the flag off: **0 requests to any SmartScreen host in 7 of 7 net logs** (4 on fresh profiles). With it on: on a fresh profile, every time (4 of 4), 4 `POST nav-edge.smartscreen.microsoft.com/api/browser/edge/navigate/3` (one per document: 2 navigations and 2 frames), 1 `POST data-edge.smartscreen.microsoft.com/.../data/settings/3`, 1 `POST telem-edge.smartscreen.microsoft.com/.../telemetry/3`. The only other Microsoft endpoint contacted, both ways, is `config.edge.skype.com` (2 `GET /config/v1/Edge/157.0.4341.60?…` on a profile's first run only), plus 4 `wpad` lookups per run.
3. **No.** `--js-flags=--noexpose_wasm` (and `--no-expose-wasm`, and even `--jitless`) reaches the renderer's command line, yet `typeof WebAssembly` stays `"object"` in the page, a dedicated worker, a shared worker, a service worker and a same-origin frame, and with `'wasm-unsafe-eval'` in the CSP the module instantiates everywhere. The flag does nothing on Runtime 157. Without `'wasm-unsafe-eval'`, instantiation throws `CompileError` in all five contexts, with or without the flag.
4. **Runtime 139 (139.0.3405.78), from Microsoft's release notes; verified by running on 157 only.** The interfaces are `ICoreWebView2_28::get_Find`, `ICoreWebView2Environment15::CreateFindOptions`, `ICoreWebView2Find` and `ICoreWebView2FindOptions` (with `put_SuppressDefaultFindDialog`). On 157, with the dialog suppressed, the counts were exact (8, 6, 7 and 0 for the four cases), next and previous moved and wrapped, `Stop` worked, and no window appeared; with it not suppressed, the browser's find bar appeared as a popup window. Runtime 139 itself could not be run: Microsoft's download page offers Fixed Version Runtimes 153–157 only, so nothing was downloaded.

## Environment

| Item | Value |
|---|---|
| OS | Windows 11 Pro 10.0.26320 |
| WebView2 Runtime | 157.0.4341.60 (Evergreen, installed; `BrowserVersionString`) |
| SDK | Microsoft.Web.WebView2 1.0.4314.35 (repository version), its managed `Microsoft.Web.WebView2.Core` API on CoreCLR |
| .NET | SDK 11.0.100, `net11.0-windows`, win-x64, CoreCLR (not NativeAOT: the spike measures the Runtime, not Spar's declarations) |
| Desktop | Every WebView ran on a desktop of the driver's own (`CreateDesktop`, child process with `STARTUPINFO.lpDesktop = WinSta0\webview2-flags-<pid>`). No window on the user's desktop, no input sent |
| Profiles | `artifacts/webview2-profiles/webview2-flags/<suite>-<variant>[-r<round>]`, passed explicitly |
| Other load | None of this repository; about 20 other `msedgewebview2.exe` processes of other apps ran on the machine. The listener attributes connections by process id, and saw 0 from other processes |
| Ports | 80 and 443 free on both loopback addresses before the runs |

## What was built

`Flags.Win/` is one executable with three modes:
- `drive <plan.json>`: creates a desktop, starts the loopback listener if the plan asks, and runs rounds × variants, each as a child process on that desktop. Variants rotate each round, so drift spreads over all of them. Writes `results/<suite>/runs.jsonl` (each run, with command lines cut to the relevant switches), `listener.json` and `summary.txt` (medians and ranges, round 0 left out as warm-up where the plan says so). Jobs, full results, net logs and captures go to `artifacts/webview2-flags/<suite>/` (disposable).
- `host <job.json> <result.json>`: one run. A plain window, an STA with its own message loop and synchronization context, `CoreWebView2Environment.CreateAsync(browserFolder, userDataFolder, new CoreWebView2EnvironmentOptions(additionalBrowserArguments: …))`, a controller in the window, `IsReputationCheckingRequired` set from the job, the source-kinds filter for `https://app.localhost/*` (all contexts, all source kinds), and every request answered from `www/` (404 for unknown paths) with `Content-Security-Policy: default-src 'none'; script-src 'self'[ 'wasm-unsafe-eval']; style-src 'self'; img-src 'self'; connect-src https://app.localhost; worker-src 'self'; frame-src 'self'; media-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'`, `nosniff`, `Cache-Control: no-store`. It records the browser's and every process's command line (`NtQueryInformationProcess`, class 60), the process list (`GetProcessInfos`), closes the controller and waits for `BrowserProcessExited`.
- `netlog <out.txt> <file>…`: summarizes Chromium net logs (below).

The managed wrapper passes the arguments the same way Spar's host does: the loader reads them through `ICoreWebView2EnvironmentOptions::get_AdditionalBrowserArguments` of the options object (Spar implements that interface itself in `EnvironmentOptions`). `WEBVIEW2_*` variables were removed in the child before the loader's first call.

Pages (`Flags.Win/www/`): `load.html` (stylesheet, `boot.js` in the head, a deferred `app.js` that posts `ready` with `typeof WebAssembly`, `typeof showOpenFilePicker`, `typeof gc`, an image); `smart.html` (stylesheet, image, a same-origin `<iframe>`, a `fetch`, a 404 `fetch`, a second image); `wasm.html` (the probe in the page, `/w/probe.js` as dedicated, shared and service worker, and `frame.html`); `find.html` (known text). Every page reports through `chrome.webview.postMessage`.

Run (Git Bash, from `spikes/webview2-flags`):

```sh
dotnet build Flags.Win -c Release
E=Flags.Win/bin/Release/net11.0-windows/win-x64/Flags.Win.exe
$E drive plans/resolver-load.json             # check 1 and the load cost of checks 1 and 3
$E drive plans/resolver-stalls.json           # base against the rule, 60 + 60 runs, listener on
$E drive plans/resolver-stalls-2.json         # the same again
$E drive plans/resolver-stalls-nolistener.json
$E drive plans/resolver-stall-netlog.json     # the rule with net logs, to catch a stall
$E drive plans/resolver-stalls-large.json     # 400 + 400 alternated, fresh profile per run, no listener
$E drive plans/wasm.json                      # check 3
$E drive plans/smartscreen.json               # check 2, one profile per variant
$E drive plans/smartscreen-fresh.json         # check 2, a fresh profile per run
$E netlog results/smartscreen-fresh/netlog-summary.txt ../../artifacts/webview2-flags/smartscreen-fresh/netlog-*.json
$E drive plans/find.json                      # check 4
```

### How loopback connections were counted

A listener of the spike's own accepts on `127.0.0.1` and `[::1]`, ports 443 and 80, for the whole plan. For each connection it records the time, the owner of the client end (`GetExtendedTcpTable`, `TCP_TABLE_OWNER_PID_ALL`, looked up while the connection is still open), and the TLS SNI or first request line; then it closes it without answering. A connection counts for a run when it falls inside that run's time window and its process is one of the run's WebView2 processes (`GetProcessInfos` plus the browser). This counts at the OS level what reached the loopback port, whatever the browser's own logs say, and it attributes reliably even with other WebView2 apps running. Its drawback: an accepted-then-closed connection may make Chromium behave differently from a refused one (nothing listening), which is what a user's machine usually has. The no-listener plan checks the timing side of that; the connection counts themselves need the listener.

## 1. `--host-resolver-rules` through the options object (observed)

`results/resolver-load/` (25 runs per variant after one warm-up, 3 navigations each), `results/resolver-stalls*/` (60 per variant, 1 navigation each).

| Variant (`AdditionalBrowserArguments`) | Browser's command line | Loopback connections per run, median (range) | Total | Navigate → first script, ms | Navigate → ready, first nav, ms | Later navs → ready, ms |
|---|---|---:|---:|---:|---:|---:|
| `--disable-blink-features=FileSystemAccessLocal` (Spar today) | as passed | 16 (12–18) | 392, all `[::1]:443`, SNI `app.localhost` | 53.8 (43.6–61.4) | 54.2 (44.3–62.1) | 14.9 (10.6–21.2) |
| `… --host-resolver-rules="MAP app.localhost ~NOTFOUND"` | as passed, quotes kept | **0 (0–0)** | **0** | 52.9 (41.7–4,976) | 53.5 (42.3–4,976) | 16.0 (8.7–21.6) |
| rule first, then `--disable-blink-features=…` | as passed | **0** | **0** | 54.0 (42.6–63.8) | 54.7 (43.0–64.6) | 15.3 (9.9–20.9) |
| rule unquoted (`--host-resolver-rules=MAP app.localhost ~NOTFOUND`) | **`--host-resolver-rules=MAP`** only | 16 (12–20) | 404 | 53.2 | 53.7 | 14.8 |
| `… --js-flags=--noexpose_wasm` | as passed | 16 (12–18) | 384 | 53.6 | 54.4 (43.5–66.6) | 14.0 |
| `… rule … --js-flags=--noexpose_wasm` | as passed | **0** | **0** | 54.6 | 55.3 (37.8–4,980) | 14.8 |

- Environment creation (12.4–12.9 ms median) and controller creation (209–216 ms median) did not change with any argument.
- The rule never broke anything the host serves: every page, stylesheet, script, image and frame loaded in all 304 runs with it, and `showOpenFilePicker` stayed `undefined` (the other argument still worked) in both orders.
- In the net log the rule shows as host resolutions of `https://~notfound` failing with `ERR_NAME_NOT_RESOLVED` (−105) and no `TCP_CONNECT` to `[::1]:443`; without it, `TCP_CONNECT [::1]:443` 12–16 times per smart-page run (`results/smartscreen/netlog-summary.txt`).
- One navigation of the load page without the rule produced 6 connections, and each later one about 5, more than the 2 per navigation `spikes/desktop-origin` counted for the React bundle. The count depends on the page; it was 0 with the rule in every case.
- The renderer's command line does not carry the rule (it is a network-service setting), only `--disable-blink-features` and `--js-flags`.

### Rare 5 s stalls (observed, cause not found)

Across all runs after warm-up:

| | Runs | First Navigate → ready about 5 s (4,917–5,060 ms) | Controller about 5.2 s | Browser exit after close about 4.85 s |
|---|---:|---:|---:|---:|
| With the rule | 304 | **8** (2.6 %) | 0 | 8 |
| Without the rule | 255 | **2** (0.8 %) | 3 | 10 |

Per plan, with the rule: `resolver-load` 2 of 75, `resolver-stalls` 4 of 60, `resolver-stalls-nolistener` 0 of 60, `resolver-stalls-2` 1 of 60, `resolver-stall-netlog` 1 of 49; without: 0, 0, 0, 2 of 60. The difference is not conclusive (Fisher's exact test, two-sided, p ≈ 0.12), and the same signature occurs without the rule (it is the existing pitfall "Rare 5 s stalls", there 1 in about 400). In every stalled navigation the host's first `WebResourceRequested` (the document) came about 4.9 s after `Navigate`; the subresources followed within 12 ms. The net log of the stalled run (`resolver-stall-netlog`, round 26) has no gap: its first event is the navigation's preconnect, and the whole log spans 159 ms. So the wait happens before the network service logs anything for the navigation, not in DNS or in the resolver rule's lookup. Medians are unaffected.

### Stall rate with and without the rule, 400 + 400 runs (observed)

`plans/resolver-stalls-large.json`, `results/resolver-stalls-large/`. Added to settle the 8/304 against 2/255 above. The two variants (Spar's argument alone, and with the rule) ran strictly alternated, base then rule, 400 times each (19:48–20:01, 12.6 min), with no listener. Every run had a new profile folder, deleted after the browser exited, and one navigation of the load page. The earlier `resolver-stalls` plans reused one profile per variant, so this plan also covers the cold-profile case; its medians are higher for that reason (first Navigate → ready 73.4 ms without the rule, 73.1 with it; controller 220.6 against 222.1 ms). A stall is a value over 1 s; every one was 4.95–5.06 s.

| | Runs | First-navigation stalls | Rate | Controller stalls | Browser-exit stalls (about 4.85 s) |
|---|---:|---:|---:|---:|---:|
| Without the rule | 400 | 4 | 1.00 % | 2 | 17 |
| With the rule | 400 | 5 | 1.25 % | 4 | 5 |
| Fisher's exact p, two-sided | | **1.00** | | 0.69 | 0.015 |

- **No difference in navigation stalls.** Pooled with every earlier plan: 13 of 704 with the rule (1.8 %) against 6 of 655 without (0.9 %), p = 0.17. The earlier excess was chance.
- **Signature:** in 8 of the 9 stalled navigations, the host's first `WebResourceRequested` (the document) arrived 4,954–5,039 ms after `Navigate`, and the page was ready 14–19 ms later. That matches the earlier stalls: the wait comes before the document request. The ninth (with the rule, round 303) had its document request 46 ms after `Navigate` and became ready 5,017 ms after it: the stall was between the document request and the page's first script. Both forms occur on fresh profiles, with or without the rule.
- Browser-exit stalls were fewer with the rule (5 against 17, p = 0.015). With several kinds of stall compared at once, this is a single observation, not a finding. The rule cannot plausibly lengthen anything there, and the difference does not argue against it.

**Recommendation:** add `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` to Spar's browser arguments always, with the value quoted exactly so, in any order with the other arguments. The larger sample shows it does not raise the about 5 s first-navigation stall rate. It is a network-service setting, so the debug proxy (Vite on `127.0.0.1`) and the loopback host are unaffected. The stalls themselves (about 1 % of cold first navigations, either way) remain unexplained; an ETW trace of a stalled run would be the next step.

## 2. SmartScreen against a net log (observed)

`results/smartscreen/` (one profile per variant, 3 runs each, the third variant with the resolver rule) and `results/smartscreen-fresh/` (a new profile every run, 4 per variant). Each run: navigate to `smart.html`, wait for its subresources, 1 s, navigate to `smart.html?second`, stay 20 s, close. Net logs `--log-net-log="<path>" --net-log-capture-mode=Everything` through the same `AdditionalBrowserArguments` (110–1,760 KB each, kept under `artifacts/webview2-flags/smartscreen*/`; the summaries are committed).

The summarizer does not assume host names: it lists every `URL_REQUEST_START_JOB` (method and URL without the query), every `HOST_RESOLVER_MANAGER_REQUEST`, every connect job, and every host name found in any string of any event's parameters, with the event types it appeared in.

| Requests in the net log (per run) | Flag off, fresh profile (4 runs) | Flag off, same profile, runs 2 and 3 | Flag on, fresh profile (4 runs) | Flag on, same profile, run 2 / run 3 |
|---|---:|---:|---:|---:|
| `POST nav-edge.smartscreen.microsoft.com/api/browser/edge/navigate/3` | **0** | **0** | 4 | 1 / 0 |
| `POST data-edge.smartscreen.microsoft.com/api/browser/edge/data/settings/3` | **0** | **0** | 1 | 0 / 0 |
| `POST telem-edge.smartscreen.microsoft.com/api/browser/edge/telemetry/3` | **0** | **0** | 1 | 0 / 0 |
| `GET config.edge.skype.com/config/v1/Edge/157.0.4341.60?…` | 2 | 0 | 2 | 0 / 0 |
| `wpad` host lookups | 4 | 4 | 4 | 4 |
| Any other host named anywhere | `ecs.nel.measure.office.net`, only inside response headers (a NEL policy from `config.edge.skype.com`); never resolved or connected | none | the same | none |

- No SmartScreen host name appears anywhere in the flag-off logs, not even as a resolution.
- The flag-on counts show what the flag stops: one reputation check per document (the two navigations and the two frame documents), a settings fetch and a telemetry upload. A profile caches the verdicts: the second run sent one check and the third none, which is why only fresh-profile runs make a fair comparison.
- `config.edge.skype.com` (the Runtime's configuration service) is contacted on a profile's first run whatever the flag; the flag does not cover it. Not checked: a switch that stops it.
- Browser exit after close, 20 s after the last navigation: 194 ms median with the flag off, 242 ms with it on.
- Seen once: the controller of the run right after a flag-on run (a different profile, the flag off) took **6,472 ms**, the SmartScreen hold-over figure of decision "Profiles and shutdown" (6.47–6.56 s), although that flag-on browser had already reported its exit 242 ms after close. Not reproduced or explained.

**Recommendation:** keep `IsReputationCheckingRequired = false`; it stops every SmartScreen request in the net log. Decision "SmartScreen default in WebView2"'s "Not verified" item can be closed.

## 3. `--js-flags=--noexpose_wasm` (observed)

`results/wasm/` (3 runs per variant; Spar's arguments plus the resolver rule in every variant). "refused" is `CompileError: WebAssembly.instantiate(): Compiling or instantiating WebAssembly module violates the following Content Security policy directive because 'unsafe-eval' is not an allowed source of script …`.

| `--js-flags` | CSP | `typeof WebAssembly` in page, dedicated, shared, service worker, frame | Instantiate the empty module | `typeof gc` |
|---|---|---|---|---|
| none | strict | object ×5 | refused ×5 | undefined |
| none | + `'wasm-unsafe-eval'` | object ×5 | ok ×5 | undefined |
| `--noexpose_wasm` | strict | **object ×5** | refused ×5 | undefined |
| `--noexpose_wasm` | + `'wasm-unsafe-eval'` | **object ×5** | **ok ×5** | undefined |
| `--no-expose-wasm` | strict | object ×5 | refused ×5 | undefined |
| `--jitless` | strict | object ×5 | refused ×5 | undefined |
| `--expose-gc` (control) | strict | object ×5 | refused ×5 | **function ×5** |

The control proves the switch reaches V8 in every context (the browser forwards `--js-flags` to the renderer's command line, which the spike read). V8's `expose_wasm` simply does not remove the global in this Runtime.

How `--js-flags` values combine (same table, `typeof gc` as the witness):

| Arguments | Browser keeps | `gc` |
|---|---|---|
| `--js-flags="--expose-gc"` | `--js-flags=--expose-gc` | function |
| `--js-flags="--expose-gc --noexpose_wasm"` | as passed | function |
| `--js-flags="--noexpose_wasm --expose-gc"` | as passed | **undefined** (only the first space-separated flag took effect) |
| `--js-flags=--noexpose_wasm,--expose-gc` | as passed | function |
| `--js-flags=--expose-gc,--noexpose_wasm` | as passed | function |
| `--js-flags=--noexpose_wasm --js-flags=--expose-gc` | `--js-flags=--expose-gc` | function (the last switch wins) |
| `--js-flags=--expose-gc --js-flags=--noexpose_wasm` | `--js-flags=--noexpose_wasm` | undefined |

So several V8 flags go in one `--js-flags`, separated by commas. A second `--js-flags` replaces the first; since WebView2 appends `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` after the options' arguments, an allowed variable with `--js-flags` would replace Spar's.

Cost (from `resolver-load`, 25 runs each): first Navigate → ready 54.4 ms (43.5–66.6) with the flag against 54.2 (44.3–62.1) without; first script 53.6 against 53.8; environment 12.9 against 12.9; controller 210.6 against 214.2. No measurable cost, and no effect.

**Recommendation:** do not add `--js-flags=--noexpose_wasm`; it does nothing. On WebView2 the difference from Linux stays: the global exists and the CSP refuses instantiation unless the app allows `'wasm-unsafe-eval'`. Not tested: deleting `globalThis.WebAssembly` from the host-injected script (it would cover documents, not workers).

## 4. The find API (observed on 157; Runtime 139 from the documentation)

**The interfaces**, from `WebView2.h` in Microsoft.Web.WebView2 1.0.4314.35:
- `ICoreWebView2_28 : ICoreWebView2_27` (`62e50381-5bf5-51a8-aae0-f20a3a9c8a90`), one method: `get_Find(ICoreWebView2Find**)`.
- `ICoreWebView2Environment15 : ICoreWebView2Environment14` (`2ac5ebfb-e654-5961-a667-7971885c7b27`): `CreateFindOptions(ICoreWebView2FindOptions**)`.
- `ICoreWebView2Find` (`a3ec0f5f-ddbc-54ed-8546-af75a785b9a6`): `get_ActiveMatchIndex`, `get_MatchCount`, `add_/remove_ActiveMatchIndexChanged`, `add_/remove_MatchCountChanged`, `Start(options, ICoreWebView2FindStartCompletedHandler*)`, `FindNext`, `FindPrevious`, `Stop`.
- `ICoreWebView2FindOptions` (`e82e3b2b-a4af-5bc6-94c6-18b44157a16c`): `FindTerm`, `IsCaseSensitive`, `ShouldHighlightAllMatches`, `ShouldMatchWord`, `SuppressDefaultFindDialog` (get and put each).

**The minimum Runtime** (Microsoft's release notes): the Find API was promoted to "Phase 3: Stable in Release" in [Release SDK 1.0.3405.78, for Runtime 139](https://learn.microsoft.com/microsoft-edge/webview2/release-notes/sdk/1-0-3405-78), whose notes say it requires Runtime 139.0.3405.78 or later for full API compatibility; the Win32 members listed there are the four interfaces above. It was experimental from [Prerelease SDK 1.0.3079-prerelease, for Runtime 134](https://learn.microsoft.com/microsoft-edge/webview2/release-notes/sdk/1-0-3079-prerelease) (`ICoreWebView2ExperimentalFind`) and stable in prerelease in [1.0.3415-prerelease, for Runtime 140](https://learn.microsoft.com/microsoft-edge/webview2/release-notes/sdk/1-0-3415-prerelease). The same notes list a known issue: in a PDF, find gives only the first index and the count, and next and previous do not work. Spar's minimum is already `139.0.3405.78` (`Prerequisites.MinimumRuntime`), so the find API needs no higher minimum.

**Runtime 139 not run.** The [official download page](https://developer.microsoft.com/microsoft-edge/webview2/) lists Fixed Version Runtimes 153.0.4234.48 to 157.0.4341.60 only (x86, x64, arm64), and the Edge update service lists no WebView2 builds; no official source for 139 was found, so nothing was downloaded and no Runtime was installed or extracted.

**On 157** (`results/find/`, 3 runs, same results each): `QueryInterface` for `ICoreWebView2_28` on the raw `ICoreWebView2` returned `S_OK`. The page holds 5 × "needle", 2 × "Needle" and 1 × "needles".

| Case | Count (expected) | After start | next, next, previous, previous, previous | After `Stop` | Windows that appeared |
|---|---:|---|---|---|---|
| insensitive, suppressed | 8 (8) | index 1 | 2, 3, 2, 1, **8** (wraps) | count and index kept | **none** |
| case-sensitive, suppressed | 6 (6) | 1 | 2, 3, 2, 1, 6 | kept | none |
| whole word, suppressed | 7 (7) | 1 | 2, 3, 2, 1, 7 | kept | none |
| no match, suppressed | 0 (0) | 0 | 0 ×5 | kept | none |
| insensitive, **not suppressed** | 8 | 1 | as above | kept | **a `Chrome_WidgetWin_1` popup of the browser process at the window's top right (342 × 84 px), with an `Intermediate D3D Window` child; gone after `Stop`** |
| insensitive again, suppressed | 8 | 1 | as above | kept | none |

- `StartAsync` completed within 0–12 ms, with the count already final (`MatchCountChanged` and `ActiveMatchIndexChanged` fired at once); each next or previous raised both events.
- Indices are 1-based; 0 means no match.
- "Windows that appeared" compares every window on the run's desktop (`EnumDesktopWindows`, then children) before and after; `results/find/default-find-bar.png` is a capture of that popup ("needle 1/8" with up, down, options and close buttons), `suppressed-window.png` the window with the dialog suppressed (matches highlighted, no bar).
- Not tested: Ctrl+F and the browser's own accelerator (Spar turns accelerators off), find in a PDF, find across frames, a page that changes while finding.

**Recommendation:** the minimum Runtime for find is 139 (139.0.3405.78), Spar's existing minimum; feature-detect `ICoreWebView2_28` and `ICoreWebView2Environment15` like every newer interface, set `SuppressDefaultFindDialog = TRUE` always, and draw the bar in the SDK as decided.

## Not tested

- Runtime 139 itself (not offered for download); any Runtime but 157; Windows 10.
- The resolver rule combined with an allowed `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` that also sets `--host-resolver-rules` (the last switch would win, as `--js-flags` showed).
- The cause of the about 5 s stalls (navigation, controller, browser exit); an ETW trace would be the next step.
- Whether SmartScreen requests carry the app's URLs (bodies were not decoded).
- A NativeAOT host: the arguments go through the same options interface, so no difference is expected.

## Process hygiene

Every child was started by the driver on its own desktop and waited for; each run waited for `BrowserProcessExited` (all exited, 160–4,870 ms after close). After the last plan, no `msedgewebview2.exe` or `Flags.Win.exe` with a command line under this repository was running.

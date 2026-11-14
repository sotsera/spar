# D8 automation spike — results

**Question.** Can a NativeAOT .NET 11 Win32 + WebView2 app drive and observe its own page **in-process** through
`ICoreWebView2::CallDevToolsProtocolMethod`, with no remote debugging port?

**Answer: yes.** Everything ran from inside the published AOT executable: the accessibility tree, screenshots, trusted
fill and click, script fill and click, and waiting for text. The window must be shown, either on screen or positioned
offscreen. With the window hidden or minimized, the page is not reliable (see the table).

Environment: Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 157.0.4341.46, Microsoft.Web.WebView2
1.0.4314.35 (loader only), CsWin32 0.3.369, Microsoft.Playwright 1.64.1, win-x64, display at 100 % scale. Every number
below was observed on 2026-11-13 in this folder. Raw data is in `artifacts/results/`.

## What was built

| File | Role |
|---|---|
| `D8.App/` | `net11.0-windows` Exe, `PublishAot=true`, `IsAotCompatible=true`, `TreatWarningsAsErrors` inherited from the repository. CsWin32 (`allowMarshaling: false`) is used for the window and message loop. WebView2 COM is hand-declared from `WebView2.h` with `[GeneratedComInterface]`. Handlers are `[GeneratedComClass]`, and the loader is a `[LibraryImport]` that takes a generated COM interface parameter. |
| `D8.App/Devtools/Automation.cs` | In-process CDP: tree, fill and click (CDP `Input.*` or `Runtime.evaluate`), waitFor (MutationObserver promise, or polling the AX tree), screenshot. |
| `D8.App/Devtools/SelfTest.cs` | `--mode selftest`: times every capability from inside the app and writes `artifacts/results/selftest-<window>-aot.json`. |
| `D8.App/Devtools/Server.cs` | `--mode serve`: loopback HTTP/1.1 JSON on `127.0.0.1:0`, with a bearer token, a Host-header check and Origin rejection, plus a lockfile. |
| `client.cs` | File-based client. It discovers the lockfile, checks liveness (pid plus an authenticated probe), then calls `tree`, `fill`, `click`, `waitFor`, `screenshot`, `bench`, `negative` and `shutdown`. |
| `playwright-attach.cs` | File-based Playwright check. Scenarios: `attach`, `stale`, `shared`, `fixed`. |

Build knobs: `-p:SparDevtools=Enabled|Switched|Excluded`, `-p:SparDevtoolsServer=false` and `-p:WebView2Loader=Static|Dll`.
The window mode is chosen with `--window normal|offscreen|hidden|controller-hidden|minimized`.

## Capabilities (in-process, AOT exe, offscreen window, 20 iterations, milliseconds)

The timings are taken inside the app around the awaited CDP calls, so they include the COM round trip and the
UI-thread continuation. In the `waitFor` rows, the page itself spends 50 ms before updating the status, standing in
for a backend call.

| Capability | Works | Median | p95 | Notes |
|---|---|---|---|---|
| CDP round trip (`Runtime.evaluate "1"`) | yes | 0.16 | 0.25 | Floor cost of one in-process CDP call. |
| `Accessibility.getFullAXTree` → flat `{role,name,value,backendDOMNodeId}` | **yes** | 0.51 | 0.85 | The CDP call itself is 0.48 ms and parsing 0.02 ms. The first call takes 2.4 ms. The raw JSON is 7.5 KB, reduced to 10 nodes. Ignored, `InlineTextBox` and unnamed `generic` nodes are dropped. |
| Fill via `DOM.focus` + `callFunctionOn(select)` + `Input.insertText` | **yes** | 1.43 | 2.02 | The page sees **trusted** input (`isTrusted=true`). |
| Click via `scrollIntoViewIfNeeded` + `getBoxModel` + 3× `Input.dispatchMouseEvent` | **yes** | 10.3 | 16.9 | **Trusted** click. Five CDP calls; each mouse event waits for the renderer's input acknowledgement. |
| Fill via `Runtime.evaluate` (set value, dispatch `input`) | yes | 0.32 | 0.58 | **Synthetic** (`isTrusted=false`). |
| Click via `Runtime.evaluate` (`el.click()`) | yes | 0.20 | 0.24 | **Synthetic**. It does not count as a user activation. |
| waitFor text: in-page MutationObserver promise (`awaitPromise`) | **yes** | 57.2 | 64.8 | About 7 ms over the page's 50 ms delay. |
| waitFor text: host polls the AX tree (no page script) | yes | 63.1 | 79.0 | 4–5 polls at a 10 ms interval. |
| `Page.captureScreenshot` → PNG on disk | **yes** | 48.2 | 63.2 | A 9 KB valid PNG of the real render (`screenshot-Offscreen.png`). |
| Readiness: double `requestAnimationFrame` after NavigationCompleted | yes | 99–127 | — | See "first input" below. |

The normal on-screen window gives the same figures within noise (`selftest-Normal-aot.json`). The figures are also
unchanged with `AreDevToolsEnabled=false` (`selftest-Offscreen-aot-devtoolsui-off.json`), so **the DevTools UI
setting does not gate in-process CDP**. Only a compile-time or trim-time gate keeps it out of a build.

### Window modes

| Mode | Tree | Trusted fill/click | Script fill/click | Page timers | Screenshot |
|---|---|---|---|---|---|
| Normal (on screen) | yes | yes | yes | normal | yes, 47 ms |
| **Offscreen** (shown at −32000,−32000, not activated) | yes | yes | yes | normal | yes, 48 ms |
| Minimized (`SW_SHOWMINNOACTIVE`) | yes | the click takes **~940 ms** | yes | normal | **never completes** (10 s timeout) |
| Hidden (`SW_HIDE`, controller visible) | yes | **hangs** (>10 s), or the click takes 5 s | yes | **throttled to ~1 s** | inconsistent: 0.5 s in one run, never in another |
| Controller `IsVisible=false` | yes | **hangs** (>10 s) | yes | throttled to ~1 s | ~530 ms |

With `--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows`
(the `*-flags.json` files), timers in hidden mode are back to normal and screenshots return in ~530 ms. Trusted
`Input.dispatchMouseEvent` still never completes, and `requestAnimationFrame` never fires. **Conclusion:** headless and
agent sessions (`spar snap`, hidden test runs) should use a shown, non-activated window placed offscreen, not
`SW_HIDE` or a hidden controller.

### First trusted input after navigation

Without a readiness wait, the first trusted click came 66–82 ms after the page loaded in 4 of 5 runs (1 normal,
3 offscreen). The fifth run, offscreen, stalled for **5013 ms**, which matches Chromium's input-acknowledgement
timeout. With a double-`rAF` wait first
(about 100–125 ms after NavigationCompleted), the first click took 12–17 ms in all 6 runs, with no stall. The UI tools
should wait for "first frame produced" before any input; this fits D8's rule that "UI tools wait until the page has been
observed".

## Loopback endpoint and lockfile (AOT exe, offscreen, `client.cs bench 20`)

- **Bind.** `TcpListener` on `127.0.0.1:0`. The OS picks the port when the socket binds, so there is no
  choose-then-bind race.
- **Lockfile.** `%TEMP%\spar-devtools-spike\<sha256(exe path)[..16]>.json` holds `{endpoint, pid, token, executable,
  startedAt}`. It is written to a temporary file and then renamed, and deleted on exit only if its pid is ours.
  Verified: it was gone after `shutdown`.
- **Discovery.** Reading the file, checking the pid and an authenticated `/version` probe took about 50 ms. Most of that
  is HttpClient's first-use cost.
- **Negative checks.** A wrong token returned 401. A foreign `Host` header (DNS rebinding) returned 403. Any `Origin`
  header (a browser page) returned 403.

| Tool | Client round trip, median | p95 | Server-side, median | HTTP and discovery overhead |
|---|---|---|---|---|
| version | 0.48 | 0.89 | 0.00 | 0.47 |
| tree | 1.24 | 1.54 | 0.90 | 0.35 |
| fill (fresh tree, then trusted fill) | 2.45 | 3.01 | 2.08 | 0.38 |
| click (fresh tree, then trusted click) | 12.29 | 18.82 | 11.81 | 0.46 |
| waitFor (includes the 50 ms app delay) | 58.03 | 65.55 | 57.37 | 0.63 |
| screenshot (base64 PNG in JSON) | 65.61 | 68.01 | 64.93 | 0.67 |

The transport adds under 1 ms per call. The plan's MCP framing was not built: the spike speaks plain JSON per tool.

## WebView2 loader: static link

**It works with no extra configuration.** The project declares `<DirectPInvoke Include="WebView2Loader" />` and
`<NativeLibrary Include="…\build\native\x64\WebView2LoaderStatic.lib" />`. The `.lib` appears in `link.rsp`. The link
needed no extra libraries, because NativeAOT's default set already includes `version.lib`, `advapi32.lib`, `ole32.lib`
and the others. `dumpbin /dependents` lists no `WebView2Loader.dll`, and the publish folder has only `D8.App.exe` and
its `.pdb`. All variants passed the smoke test (`--mode smoke`). The sidecar fallback (`-p:WebView2Loader=Dll`) also
works, but was not needed.

## Sizes (Release, win-x64, NativeAOT, `InvariantGlobalization=true`, bytes)

| Variant | `D8.App.exe` | Delta from `excluded-static` |
|---|---|---|
| `excluded-static`: devtools removed by `#if` | 2,139,136 | 0 |
| `switched-static`: devtools compiled, feature switch `Spar.Devtools.IsSupported=false` (`Trim="true"`) | 2,139,136 | **0** |
| `core-static`: in-process CDP automation and self-test, no HTTP server or lockfile | 2,694,144 | +554,008 |
| `enabled-static`: everything (CDP, self-test, socket HTTP, lockfile, token) | 3,320,320 | **+1,181,184** |
| `excluded-dll` | 2,115,584, plus 194,912 for `WebView2Loader.dll` | |
| `enabled-dll` | 3,297,280, plus 194,912 for `WebView2Loader.dll` | |

- **Static loader cost.** It adds about 23 KB to the exe (23,040 / 23,552), against a 195 KB sidecar DLL.
- **Trimming proof.** The exes were scanned for UTF-8 and UTF-16 strings. `Accessibility.getFullAXTree`,
  `Input.dispatchMouseEvent`, `spar-devtools-spike`, `DevToolsCallHandler`, `TcpListener`, `Utf8JsonWriter` and
  `JsonDocument` appear in `enabled-static` and are absent from both `switched-static` and `excluded-static`. The only
  thing the switched build keeps is the switch name. A `[FeatureSwitchDefinition]` plus `RuntimeHostConfigurationOption
  Trim="true"` therefore removes the code exactly as completely as `#if`. Both builds refuse `--mode selftest` with
  exit code 3. That string scan is a cheap basis for the `Spar.BuildTests` "no devtools in Production" check.

## Playwright attach (`--remote-debugging-port`, set through `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`)

This was run against `excluded-static`, a build with **no devtools code at all**.

- **Attach works.**
  - With `--remote-debugging-port=0`, the browser writes `<userDataFolder>\EBWebView\DevToolsActivePort`: line 1 is the
    port, line 2 is the browser WebSocket path. It appeared 182 ms after launch.
  - `ConnectOverCDPAsync` took 30.7 ms, and the page was reachable 559 ms after launch, including the Playwright driver
    start.
  - Medians: fill 2.8, click 29.3, waitFor 94.7, screenshot 67.0, `AriaSnapshotAsync` 2.7 ms. The clicks were trusted.
- **Race 1: stale port file.** The file is **not deleted** when the app exits.
  - A second launch with the same profile reads the old port right after starting.
  - On Windows, a probe to that closed loopback port did not fail fast: it hit the client's 2 s timeout.
  - The new port appeared only after the browser rewrote the file. Worse, another process could have taken the old
    port in the meantime.
- **Race 2: shared profile.**
  - Two app processes on one user-data folder share one browser process, so one port exposes both apps' pages (2 pages
    seen).
  - According to the WebView2 docs, a second app with *different* arguments fails with `ERROR_INVALID_STATE`.
- **Race 3: fixed port** (the earlier experiment's shape).
  - With `--remote-debugging-port=9333`, the browser listens but writes **no** `DevToolsActivePort`; the file exists
    only for port 0. The client can only probe the port it picked.
  - A second app given the same port starts normally, but its page has no endpoint. Neither the app nor the client is
    told that the bind failed.
  - Port 9333 kept serving app A's single page, and it went dead when A exited, although B was still running.
  - Choosing a free port and then passing it is the same race with a time-of-check gap added.
- **Security.**
  - `/json/version` and the WebSocket need **no authentication**: any local process can drive the page.
  - According to `WebView2.idl`, `additionalBrowserArguments` from `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` (or its
    registry equivalent) is *appended* to the app's own arguments. A Production build with all devtools code removed
    can still be opened for remote debugging from outside the app. That is a separate production-hardening question
    (untested idea: clear the variable in-process before creating the environment; the registry policy would still
    apply).
- **AOT.** `Microsoft.Playwright` 1.64.1 throws inside `Playwright.CreateAsync()`, with "Reflection-based serialization
  has been disabled", in any project where `PublishAot=true` turns off reflection JSON. File-based apps do this by
  default, so the client needed `#:property PublishAot=false`. Playwright can therefore never run inside a Spar app
  process; it can run only in a JIT test project.

## AOT and trimming issues hit

Publication had **zero** ILC or trimming warnings, with `TreatWarningsAsErrors=true`, `IlcTreatWarningsAsErrors=true`,
`IsAotCompatible=true`, `TrimmerSingleWarn=false` and no suppressions. These features all produced no warnings:

- `GeneratedComInterface` and `GeneratedComClass`;
- `LibraryImport` with a generated COM interface parameter;
- CsWin32 with `allowMarshaling: false`, and an `[UnmanagedCallersOnly]` window procedure;
- `[STAThread]`, which is honoured: the apartment is STA;
- `JsonDocument` and `Utf8JsonWriter`, without the reflection serializer;
- `TcpListener`, `SHA256` and `RandomNumberGenerator`;
- `[FeatureSwitchDefinition]`.

Compile-time issues met along the way:

1. **CS8652: "updated memory safety rules" is a preview feature in C# 15.** An `await` inside a type marked `unsafe`
   fails to compile under `LangVersion` 15. The fix is to mark only the members that need pointers as `unsafe`, not the
   type. This is relevant to Spar's conventions for the host and STA code.
2. CS4007: a `ReadOnlySpan<string>` collection expression used as a `foreach` source cannot cross an `await`. A static
   array fixed it.
3. With `PublishAot=true`, `RuntimeFeature.IsDynamicCodeSupported` is **false even in JIT runs**, because the SDK sets
   the switch so analysis matches AOT. Use `RuntimeFeature.IsDynamicCodeCompiled` to detect a real AOT process.
4. Playwright needs reflection JSON (see above).

## Recommendation for the D8 automation transport

**Use in-process CDP (`CallDevToolsProtocolMethod`) as the only automation transport, with no remote debugging port.**

- It covers every D8 UI tool (tree, screenshot, click, fill, waitFor) in an AOT exe. Calls cost sub-millisecond to
  ~10 ms, there is no port, and nothing needs discovering at the browser level.
- It cannot race, because nothing is allocated. It cannot be reached by other local processes except through Spar's own
  authenticated loopback endpoint.
- Agent access goes through Spar's own endpoint: a `127.0.0.1:0` bind, a per-launch bearer token, a Host-header check
  and Origin rejection, and an atomically written lockfile with pid and probe liveness. It adds under 1 ms per call.
- **Default to trusted CDP `Input.*`** for click and fill, so pages see real user activation. Keep `Runtime.evaluate`
  as an explicit `via: script` fallback; it is about 10× faster but synthetic.
- Wait for a first frame (double `rAF`) before the first input. Run sessions without a visible window as **shown,
  offscreen and non-activated** windows; never `SW_HIDE` or a hidden controller.
- Gate devtools with `#if` or a trimmed feature switch: both remove it completely. Enforce it with a string or type scan
  in `Spar.BuildTests`. The cost of D8 devtools is roughly **+0.55 MB** for the core and **+0.63 MB** for the transport.
- Keep the WebView2 static loader. It worked first time, and saves a 195 KB sidecar for a 23 KB increase in exe size.
- **Do not adopt Playwright over a debugging port** for devtools. If the test factory ever needs it, use port 0 only.
  Delete `DevToolsActivePort` before launch, use an exclusive profile per run, and accept that the port is
  unauthenticated. The shared automation core should be the in-process one (D8 "shared with the test factory").

## Not tested

- Keyboard `press` (`Input.dispatchKeyEvent`), iframes, shadow DOM, and CDP events through
  `GetDevToolsProtocolEventReceiver`. All waits here use a promise or polling.
- A display scale other than 100 %: box-model coordinates are in CSS pixels, and the screenshot was 884×601 for a
  900×640 window.
- An offscreen window with `WS_EX_TOOLWINDOW`/`WS_EX_NOACTIVATE`, to keep it off the taskbar. Multiple monitors.
- MCP framing, concurrent HTTP requests, and long-running or stress sessions.
- Hung CDP calls after a timeout: they were left running, and only process exit cleaned them up. Cancelling an
  in-flight CDP call was not examined.
- Mitigating `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` in Production.
- arm64 and x86. Windows Sandbox. Any JIT-mode measurement: all reported numbers come from the AOT exe.
- The lockfile is keyed by the exe path, not by the csproj as the plan says. The key choice was not evaluated.

## Reproduce

```bash
dotnet publish D8.App -c Release -o D8.App/bin/publish/enabled-static
```

```bash
D8.App/bin/publish/enabled-static/D8.App.exe --mode selftest --window offscreen --iterations 20 --profile aot
```

```bash
D8.App/bin/publish/enabled-static/D8.App.exe --mode serve --window offscreen --profile serve
```

```bash
dotnet run client.cs -- bench 20
```

```bash
dotnet run playwright-attach.cs -- D8.App/bin/publish/excluded-static/D8.App.exe attach
```

Other variants: `-p:SparDevtools=Switched|Excluded`, `-p:SparDevtoolsServer=false`, `-p:WebView2Loader=Dll`. Set
`D8_READY=none` to skip the readiness wait. `artifacts/` (profiles and results, about 200 MB) is disposable.

# Page security and security defaults

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 3b" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 3b. Page security: navigation lock, Content Security Policy and response headers (verified by `spikes/csp-navigation`; choices decided 2026-11-14)

- **Navigation lock, always on in every host** (not configurable):
  - cancel top-level and frame navigations outside the app origin (`NavigationStarting` and frame navigation on WebView2, `decide-policy` on WebKitGTK);
  - the page never opens another WebView window: the host cancels `NewWindowRequested`, `window.open` and `target="_blank"`;
  - external links never load in the WebView. A `target="_blank"` link to an allowed scheme opens in the system browser by itself, since the click is the user action, and `shell.openExternal(url)` does the same for links built in code. The allowed schemes are `http(s)`, `mailto` and `tel`, only after a user action and never with credentials in the URL (decision "Opening links and files outside the app"; section 3, "opening things outside the app"). Every other new-window attempt stays blocked;
  - **WebView2 also needs `AddWebResourceRequestedFilter("*", DOCUMENT)`,** answering foreign documents with 403. Without it, every cancelled GET navigation still reached the network, because the cancel races a request that has already started;
  - hosts never answer with redirects: WebKitGTK does not follow a redirect from a scheme handler.

  The lock held on both hosts for every case tested: `location` changes, meta refresh, unknown schemes, form posts, iframes, `window.open`, `target=_blank`, and middle, Ctrl and Shift clicks.
  - This sits under the bridge's origin and document checks, which stay; CSP does not replace either.
- **A strict CSP by default.** Everything an app needs comes from its own origin, so the base policy allows only `'self'`:

  ```
  default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self';
  connect-src https://app.localhost; media-src 'self'; worker-src 'self'; manifest-src 'self';
  base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none';
  require-trusted-types-for 'script';
  report-uri /_spar/csp-report
  ```

  `media-src 'self'` was added when the policy was built (M1b step 4): without it the app could not play its own audio or video, which is no more a risk than its own images, and `MediaSrc(…)` had no base to add to (decision "Page security (implementation record; choices for review)", choice 3).

  Anything else (a font CDN, CSS-in-JS needing a nonce or `'unsafe-inline'`) is a conscious addition.
  - **Styles added at run time** (`spikes/csp-style-injection`; decision "Styles added at run time: a nonce and constructed stylesheets both work"): under the base policy a `<style>` element and a `style` attribute are blocked, while constructed stylesheets, `insertRule` and `style.setProperty` apply, identically on Chromium, WebView2 and WebKitGTK 6.0. A per-document style nonce also works, delivered as `<meta property="csp-nonce">`. Decided (user, 2026-11-17; decision "M1b's remaining questions without a spike"): the app-author docs recommend build-time CSS and constructed stylesheets, and `csp.StyleNonce()` in C# gives each document a fresh nonce, written into the served HTML as `<meta property="csp-nonce">` and into `style-src`.
  - **What the build adds to this policy by itself:** the hash of the theme style (section 3d), the hash of the SRI import map (below), and in Debug the nonce and the HMR WebSocket (below).
  - **`require-trusted-types-for 'script'`** is part of the base policy, with a C# opt-out (decision "Trusted Types on by default"; see "Trusted Types" below).
  - **`connect-src` names the origin with its scheme** (`https://app.localhost` on Windows), because `'self'` also allows `wss://` to the same host, and WebSockets bypass the host (`spikes/desktop-origin`). On Linux it is `spar://app` (decision "M1c's first questions", checked by `spikes/linux-gpu-csp`; served by the host since M1c step 1, and read back from every response kind in step 4).
  - **Verified, with one difference:** the React production bundle ran with 0 violations on WebView2, Chromium (loopback) and WebKitGTK 4.1 and 6.0, honoured as a response header on every host; `<meta>` gave the same results. That run used `connect-src 'self'` on the origin `https://app.spar.invalid` (`spikes/csp-navigation`). The policy above, with `connect-src https://app.localhost` on `app.localhost`, ran in M1b step 4 with the Notebook's React bundle, its import map hash and Subresource Integrity, as a Production NativeAOT executable on WebView2 157: 0 violations; its browser journeys under the same policy on loopback Chromium passed (`docs/evidence/m1b-page-security.md`).
  - **It also blocks** same-origin iframes, and WebAssembly. Both become C# opt-ins. Correction from `spikes/webkit-wasm-off`: WebKitGTK 2.52 does enforce the block, but only when a module is instantiated (`compile`, `Module` and streaming compile succeed; every instantiate path fails). 2.54 also blocks compiling. `validate` always succeeds.
- **Declared in C#,** where the Spar application is configured:

  ```csharp
  var app = SparApplication.Create(args, spar =>
  {
      spar.ContentSecurity(csp => csp.FontSrc("https://fonts.gstatic.com"));
  });
  ```

  - Values must be constants, and the call must sit directly in the `Create` configuration, so the build can read it without running code. Anything else is an analyzer error at its location.
  - The generator puts the policy in the generated catalog for the host. The client step writes it to `obj/spar/client/csp.json` inside `@spar/app`, where the Vite plugin reads it relative to itself on every page load.
  - In Debug the host checks that the policy it ran matches the generated one.
  - **Verified by prototype:** one analysis drives the generator, the analyzer and an extractor that writes `csp.json` without building, in 109–574 ms. The generated policy equalled the runtime one. Five misuses gave the same located errors from the build and from the extractor: a non-constant value, a call inside `if`, a helper method, an invalid source, a delegate variable. The extractor needs the project's implicit global usings. IDE squiggles were not checked.
- **Sent as a response header** on the HTML, by the host in Release and test variants and by the Vite plugin in Debug.
  - In Debug the plugin adds a per-request nonce (`html.cspNonce`) for Vite's inline preamble and styles, and, with the page kept on `https://app.localhost` (section 4), the HMR WebSocket `ws://127.0.0.1:<port>` in `connect-src`. Served directly from Vite, `'self'` already allowed it.
  - The dev HTML is sent `no-store` without an ETag, because Vite's ETag let a cached page keep an old nonce.
  - Measured in Chromium: Fast Refresh in 37–47 ms with state kept, CSS updates in 12–15 ms, the error overlay in 26–31 ms, 0 violations. A component update took about 90 ms in WebView2 and about 80 ms in WebKitGTK.
  - Violations should appear in development first, never only in Release.
- **The CSP goes on every host-served response, worker scripts included** (`spikes/webkit-wasm-off`). A worker takes its policy from its own response. On WebKitGTK a worker script served without the header ran `eval` and every WebAssembly path under a page with the base CSP, and it instantiated a module the page had compiled and posted to it. Frames inherit the page's policy. WebView2 checked in M1b step 4: a module worker served with the header had `eval`, `new Function` and WebAssembly blocked. The Vite plugin's development server arrives with the development loop (M1d).
- **Other headers on every host-served response** (and from the Vite plugin in Debug), next to `Cache-Control: no-store` (section 1):
  - `X-Content-Type-Options: nosniff`, with an exact `Content-Type` for every file type (`text/javascript`, `text/css`, `application/wasm`, `image/svg+xml`, `application/json`, fonts). Honoured on every engine.
    - **The Linux host must repeat `Content-Type` in the header set** passed to `webkit_uri_scheme_response_set_http_headers`. Otherwise WebKitGTK silently blocks the app's own CSS, classic scripts and workers.
  - `Referrer-Policy: no-referrer`. On the desktop no `Referer` was sent even without it; it matters on loopback.
  - `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Resource-Policy: same-origin`. These, and `frame-ancestors`, took effect only on loopback; they are cheap, so keep them everywhere.
  - `Permissions-Policy` denying powerful features (camera, microphone, geolocation, USB, serial, HID, Bluetooth, payment, display capture), with opt-ins declared in C# next to the CSP.
    - Honoured on WebView2 and Chromium. WebKitGTK ignores it, so on Linux the host's `permission-request` handler is the only gate.
    - The host's permission event (WebView2 `PermissionRequested`, WebKitGTK `permission-request`) is denied by default with the same opt-ins. Geolocation was denied in 1 ms with no prompt on both hosts.
    - **On WebView2 the camera never raised `PermissionRequested`.** A `getUserMedia` call without `Permissions-Policy` hung, and a prompt reached the user's desktop, most likely the Windows camera privacy prompt for unpackaged apps. With `camera=()` the call failed in about 250 ms with no host event. So the header is required on Windows, not optional.
    - **Followed up in Windows Sandbox** with real devices disabled (`spikes/windows-sandbox-checks`):
      - **Without a device:** `NotFoundError` in about 1 ms, with no host event.
      - **With Chromium's fake devices:** `PermissionRequested` fires per kind (camera 2, microphone 1). Allow gives a live track and deny gives `NotAllowedError`. Default shows WebView2's own in-window prompt and waits.
      - **`SavesInProfile` defaults to true.** The answer is remembered across processes, and a remembered allow starts the camera with no host event.
      - **`--use-fake-ui-for-media-stream` bypasses the handler entirely.** Only `Permissions-Policy` held under every condition.
      - **`ICoreWebView2Profile4`:** `GetNonDefaultPermissionSettings` lists the remembered answers. `SetPermissionState` to default forgets one, and to deny stops a running track at once.
      - No Windows privacy prompt appeared, because fake devices never open a real camera. The earlier incident with a real device stays untested.
    - **Host rules from this:**
      1. always send `Permissions-Policy`, denying everything not declared;
      2. answer `PermissionRequested` explicitly, and leave it at default only when the author chose the WebView's prompt;
      3. set `SavesInProfile = false` when the app stores the answer. Otherwise "turn the webcam off" calls `SetPermissionState`, which also stops a running camera;
      4. keep ignoring `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` in Production.
  - **Permissions, two layers** (decided, user, 2026-11-14):
    - The author declares which features the app may use at all, for example `spar.Permissions(p => p.Allow(Permission.Camera))`. This sets both the header (`camera=(self)`) and the host gate. Anything not declared is denied with no prompt.
    - **Declaring a feature grants it** when the page asks: the page is the app's own code, so the declaration is the consent. The OS privacy settings still apply.
    - The browser never remembers answers (`SavesInProfile = false`). Remembering is the app's choice.
    - For a declared feature, the author picks how a request is answered: grant it, let the WebView show its own prompt, or call a C# handler. The handler can ask through the app's UI and remember the answer.
    - The app can list and reset remembered answers (WebView2 profile permission state; the WebKitGTK equivalent to check), so an end user can turn the webcam off again from the app's settings.
    - The OS privacy settings (Windows camera and microphone access for desktop apps, the Linux camera portal) still apply on top and cannot be bypassed.
    - `spikes/csp-navigation` checked the deny path for geolocation; the camera result is described above. The grant path (a real camera on both hosts) is an M3 native-services check.
  - `Cross-Origin-Embedder-Policy: require-corp` only as an opt-in, for apps that need cross-origin isolation (`SharedArrayBuffer`, WebAssembly threads). It gave isolation on WebView2 and Chromium, not on WebKitGTK.
  - Not sent: `Strict-Transport-Security` (no network origin), `X-XSS-Protection` (obsolete). `X-Frame-Options: DENY` only if the CSP has to fall back to `<meta>`, which ignores `frame-ancestors`.
- **Requests checked on loopback and by the Vite dev server:** the `Host` header must be the loopback address or `localhost` with the right port (DNS rebinding), besides the token and the `Origin` check. Vite's `server.allowedHosts` stays at its default.
  - **The loopback bridge spike has no `Host` check:** `spikes/loopback` checks only the token and the `Origin`, and the `Origin` only when one is sent. It is not a reference for this rule. Two other spikes implement it, with different answers: `spikes/csp-navigation/Csp.Shared/LoopbackHost.cs` (421 for a wrong host or port) and `spikes/d8-automation/D8.App/Devtools/Server.cs` (403). **Decided (user, 2026-11-14): 403** with the code `forbidden-host` in the body, as Vite answers for a host it does not allow. 421 is avoided because browsers may retry it on a new connection.
  - Vite with the default `allowedHosts` answered 403 for `evil.example` and also for `host.docker.internal`, which the Linux container runs had to add (test only).
- **Trusted Types** (`require-trusted-types-for 'script'`) against DOM injection.
  - Supported by all four engines. The React production bundle works under it, and in Chromium so do the Vite dev client, Fast Refresh and the error overlay.
  - Cost: creating workers or script URLs from strings needs a policy.
  - **Decided (user, 2026-11-14): on by default**, with an explicit C# opt-out (level 2, section 3c). Angular, the dev loop on WebView2 and WebKitGTK, and other edge cases are tested when they come up.
- **Subresource Integrity:** the build adds `integrity` (sha384) to the entry script, CSS and `modulepreload` links, computed by the Spar Vite plugin from the final files, in 73–79 ms per build.
  - Enforced everywhere: a tampered entry stops the page, and tampered CSS is not applied.
  - Lazy chunks are covered through an import map with `integrity`, which works on WebView2, Chromium and WebKitGTK 2.52. The import map is an inline script, so its sha256 goes into `script-src`.
  - **Decided (user, 2026-11-14): on by default**, with a C# opt-out. Every external script or stylesheet an app allows must come with its hash (fonts cannot carry SRI).
  - For self-served files this mostly catches serving or packing faults: whoever can change the executable can change the HTML too.
  - Its real value is on external scripts and stylesheets the user allows. Spar should ask for a hash there (fonts cannot carry SRI).
- **Violations become diagnostics.** Each one goes to the backend log and the devtools `logs`, with the exact directive to add. `spar doctor` shows the effective policy.
  - **Where to listen:** in the host-injected script, not in an SDK module. A module listener missed the violations raised while the HTML was parsed (it saw 24 of 28 on WebView2, 20 of 25 on WebKitGTK).
  - The injected listener and `report-uri` received every violation on every engine. `report-to` delivered only on WebKitGTK.
  - **Decided (user, 2026-11-14): both channels.** The injected listener is the main one. `report-uri` is the backstop for contexts the listener may not see (workers, untested) and for a failed injected script. The host removes duplicates.
  - **On in every build, can be turned off** (user, 2026-11-14): both channels report by default in Development and Production. A C# option turns each channel off, or limits reporting to Development builds. Production logs the grouped summaries; Development also shows each new group with the directive to add.
  - **Flood limits** (user requirement: a badly made page can raise very many violations). Reports must never flood the log, the bridge or the UI thread:
    - **Grouping:** reports are grouped by directive, blocked URI and source location. The first report of a group is logged in full. Later ones only raise its count, written as a summary at most every few seconds.
    - **The page batches:** the injected listener groups and batches in the page too, and posts at most one message per interval. It never posts one message per violation.
    - **Bounds:** the number of distinct groups is bounded per document. Beyond it the host logs one "too many distinct violations" line and drops the rest.
    - **Size:** `/_spar/csp-report` caps the body size and the request rate, and answers 204 to anything over either limit without parsing it.
    - **Off the UI thread:** reports are parsed and logged away from the UI thread.
    - **Numbers:** the intervals and limits are set in M1 from a flood test, for example a page raising 100,000 violations.
- **A build diagnostic** when a Spar-added hash or nonce shares a directive with the author's `'unsafe-inline'`, which browsers then ignore (`docs/research-review.md`). Built (M1b step 4) as two parts: with `AllowInlineStyles()`, Spar puts no nonce or hash in `style-src` at all, so the author's `'unsafe-inline'` always means what it says; and `StyleNonce()` beside it is the warning SPAR105. `'unsafe-inline'` exists for styles only, so no other directive can meet the case.
- **Defaults that avoid surprises:** `build.assetsInlineLimit = 0`, so Vite never turns small assets into `data:` URLs. Verified: with Vite's default the 137-byte logo became a `data:` URL and was blocked.
- **Host-injected scripts** (the bridge, the telemetry settings) run before every page script on both hosts, also under Vite, and the page reads `window.__spar` synchronously. WebView2 exempts them from the page's `eval` ban; WebKitGTK does not.
- **`isSecureContext` is true on `spar://`** even without registering it as a secure scheme.
- **Built on Windows** (M1b step 4; decisions "Page security's public shape" and "Page security (implementation record; choices for review)"; `docs/evidence/m1b-page-security.md`):
  - **The C# declaration:** `spar.ContentSecurity(csp => …)` (`FontSrc`, `ImgSrc`, `ConnectSrc`, `MediaSrc`, `ScriptSrc`, `StyleSrc`, `FrameSrc`, `AllowInlineStyles`, `AllowEval`, `AllowWebAssembly`, `StyleNonce`, `WithoutTrustedTypes`, `WithoutIntegrity`, `Reporting`) and `spar.Permissions(p => p.Allow(Permission.X))`. One source rule (`CspSourceRule`) serves the builder at run time and the analysis at build time; the analyzer reports SPAR101–SPAR104 and the SPAR105 warning at their places, the generator writes the declaration into the catalog, and the client step writes `csp.json` into `@spar/app`. A Development build logs an error when the run's configuration differs from the build's.
  - **One valid policy:** `PagePolicy` composes each directive once, in a fixed order, with its values grouped (base value, opt-in keywords, the app's sources in the order declared, then the build's hashes and the document's nonce) and duplicates dropped.
  - **Every response carries it,** worker scripts included: a module worker served by the host had `eval`, `new Function` and WebAssembly blocked on WebView2 157 (the plan's step-9 check).
  - **The import map's hash** is read by the host from the served HTML at start (only Spar's own `<script type="importmap">`), not passed from the build; `script-src` names it.
  - **The navigation lock, new windows and the permission gate** on the WebView's events (`PageGuard`): top-level and frame navigations off the origin cancelled (frames may load `about:blank` and `about:srcdoc`), every new window handled, a user's click to `http(s)`, `mailto` or `tel` without credentials opened by `ShellExecuteW` on a thread of its own, and `PermissionRequested` answered allow or deny with `SavesInProfile = false`. A geolocation request denied by `Permissions-Policy` never reached the host's event.
  - **The host-injected script** (`PageScript.js`, added before the first navigation) answers the heartbeat and runs the violation listener: grouped, one message per second at most, 100 groups per document, and it stops listening after 1,000 violations in a document, since the engine creates an event per violation only while a listener exists.
  - **Both channels into one log** (`CspViolationLog`): a group's count is the larger of the two channels', so a violation reported twice is counted once; 100 groups per document; summaries at most every 10 s; `report-uri` bodies over 16 KiB or past 20 at once and 5 a second are answered 204 unread.
  - **Found while building:** Chromium raises one event and one report for identical violations in a document (100,000 identical style attributes gave one of each), so a flood is a flood of distinct violations; Trusted Types make `new Worker(url)` a script sink, so a worker's address must come from a policy (Vite's `new Worker(new URL(…))` included).
- **Built on Linux** (M1c step 4; decision "Linux page security (implementation record; choices for review)"; `docs/evidence/m1c-page-security.md`):
  - **The same policy and headers** on every `spar://app` response, the worker script and 404 included, with `connect-src spar://app` and `Content-Type` repeated in the header set; a module worker served by the host had `eval` and `new Function` blocked and its cross-origin `fetch` refused.
  - **The lock in `decide-policy`, with what WebKitGTK cannot tell apart:** a navigation decision has no main-frame flag, so navigations go ahead only to the app's origin and to addresses declared with `FrameSrc`; `about:blank` and `about:srcdoc`, which a top-level `location` change commits in the window, are refused for frames too (a frame keeps its first, blank document; a `srcdoc` frame stays empty, a difference from Windows). A declared frame address loaded by the window itself is stopped as its load starts and again at its response. New windows are always refused; an allowed external address opens through the desktop's handler (`g_app_info_launch_default_for_uri`) after a person's input on the view within 5 s, since WebKit's own gesture flag is also set for script right after a load. A redirect from a declared frame to another origin was refused by the page's `frame-src` before the lock.
  - **`permission-request` is the only gate,** decided by the request's type: declared geolocation, camera, microphone and clipboard reading granted from the app's page, everything else denied with no prompt; WebKitGTK remembers nothing. `query-permission-state` answers "denied" for what is undeclared, which WebKit shows the page as "prompt".
  - **Reports:** the relay runs the Windows listener's grouping and limits in its isolated world; `report-uri` is read asynchronously within the same size and rate limits; both reach one log, and a violation without a source file is one group although the two channels disagree on its position. **Violations inside workers raise no event and send no report on WebKitGTK,** so neither channel sees them.
  - **Autoplay is not gated on Linux yet:** WebKitGTK ignores `Permissions-Policy: autoplay=()`, and its website policies (`WebKitWebsitePolicies`) were not set in this step.

- **The spike's questions, all decided on 2026-11-14** (`spikes/csp-navigation/RESULTS.md`):
  - Trusted Types default (decided: on, with a C# opt-out);
  - same-origin iframes in the base policy (decided: blocked, with a C# opt-in);
  - WebAssembly (decided: blocked by default, with a C# opt-in adding `'wasm-unsafe-eval'`). The Linux host also sets `JSC_useWasm=false` through libc `setenv` before GTK and WebKit start, so the `WebAssembly` global is absent in the page, workers and frames (`spikes/webkit-wasm-off`). The opt-in sets it to `true` explicitly. On Windows the global stays (user decision 2026-11-19): no browser switch removes it (`--js-flags=--noexpose_wasm`, `--no-expose-wasm` and `--jitless` left it in the page, frames and every kind of worker, `spikes/webview2-flags`), and the policy refuses every module there; so `typeof WebAssembly` is `"object"` on Windows and `"undefined"` on Linux, and a window test checks the refusal in the page and in dedicated and shared workers. Built on Linux in M1c step 5 (decision "Linux WebAssembly off by default (implementation record; choices for review)"): the host resolves the page security before GTK and writes `JSC_useWasm` with `setenv`'s overwrite, so an inherited value never decides it (with or without `AllowBrowserEnvironmentOverrides()`, which covers exactly the variables listed in 3c); every other inherited `JSC_*` passed and was named in a warning at start-up (superseded in M1c step 6 by a user decision: a Production build removes them, 3c). Real-host tests check the page, a dedicated and a shared worker both ways, and again after a lost web process and after "Reload page"; WebKitGTK registers no service worker from `spar://app` at all;
  - the reporting channel (decided: both, deduplicated, with flood limits);
  - SRI default (decided: on, with a C# opt-out; external scripts and stylesheets need a hash);
  - the camera and microphone on WebView2 (decided: the two-layer permission design and the four host rules);
- **Not tested:**
  - the camera path on either host;
  - `frame-ancestors` and COOP on the desktop;
  - redirects of subresources on WebKitGTK;
  - CSS and overlay hot updates inside the WebViews;
  - Vite `?worker` under Trusted Types (found in M1b: `new Worker(url)` needs a Trusted Types policy);
  - SRI on a large bundle;
  - injected scripts in child frames;
  - a real Linux desktop.

## 3c. Security defaults and overrides (user decision)

The app must be secure by default and follow current best practice, and an author who really needs to can go past a default, consciously and visibly. **Rule (user, 2026-11-14): anything that is a security risk is blocked by default.** An app unblocks it only through an explicit option, and that option's documentation explains the risk it brings, when it is reasonable, and the safer alternative if one exists.

**What the controls defend** (adopted from the research review, user, 2026-11-15):
- **Page content is untrusted,** even the app's own bundled frontend, which a dependency or an injection can compromise. So the bridge checks origin and document, entry limits and admission run before any work, and the page gets grants and write-only secrets, never paths or values.
- **A nonce or a hash proves that the host issued or built something, not that its content is safe.**
- **Native code running as the same user is not sandboxed from the app.** Neither OS isolates apps run by one user, so the per-user data folder, the credential store and the devtools lockfile protect against other users and other sites, not against that user's own programs.

Constraints fall into three levels:

1. **Always on.** These are controls that protect against other sites and other processes; turning them off serves no legitimate app:
   - the bridge's origin and document checks;
   - the loopback token, `Host` check and `Origin` rejection (development and tests only);
   - the devtools endpoint's token and lockfile;
   - no remote debugging port opened by Spar.

   **How a loopback page gets its token: decided (user, 2026-11-14), the URL fragment.** The host publishes `http://127.0.0.1:<port>/#token=…` on its ready line and in the devtools lockfile and writes the token nowhere else. The SDK moves the token to `sessionStorage` and removes it with `history.replaceState`. A fragment never reaches the server, a `Referer` or a request log. Rejected: an `HttpOnly` cookie (cookies are not separated by port, so parallel hosts would clash and other local servers would receive it) and injecting the token into the served HTML (anyone reaching the port would get it). Earlier state: in `spikes/loopback` the token (24 random bytes per run) arrives in the URL fragment (`#token=`, printed in the ready line), the SDK moves it to `sessionStorage` and removes it with `history.replaceState`. Through Vite, the dev server's proxy adds it (`spikes/react`). `spikes/browser-telemetry` added `hostHeaders()` so the page's exporter can call its own host. Assets are served without the token. Through Vite the proxy still adds it.
2. **Strict defaults with an explicit override in C#,** declared in the `SparApplication.Create` configuration like the CSP (section 3b), so overrides show in diffs. Spar logs every active override at startup and `spar doctor` lists them. This level covers:
   - the CSP and other headers;
   - permissions;
   - browser settings;
   - browser environment overrides;
   - browser devtools in Production.
3. **Limits on the page only.** File grants hide paths, and page-facing secrets are write-only. The backend can do anything, so an author goes past these by writing their own command. Ready-made options (visible paths, persistent grants for recent files) can come later.

**Production defaults, each with a C# opt-in:**

Built on Windows in M1b step 5 (decisions "Security defaults' public shape" and "Security defaults (implementation record; choices for review)"; `docs/evidence/m1b-security-defaults.md`): the opt-ins are one group, `spar.Browser(b => b.AllowDevTools().AllowAcceleratorKeys().AllowPasswordSaving().AllowDownloads().AllowHistoryNavigation().AllowContextMenu().AllowPagePicker().AllowBrowserEnvironmentOverrides())`, for the whole app, each logged at start-up. A Development build keeps every default except devtools (F12, Inspect) and the reload keys (F5, Ctrl+R).
- **Browser settings:**
  - On Windows:
    - devtools (F12) and the context menu's "Inspect" are off. **This does not disable automation:** with `AreDevToolsEnabled=false`, in-process CDP still worked unchanged (`spikes/d8-automation`). Only a compile-time or trim-time gate keeps the devtools endpoint out of a build, and the production boundary test proves it;
    - browser accelerator keys are off (reload, print, devtools); `AreBrowserAcceleratorKeysEnabled` is one switch for every key, so where devtools are on (Development, or `AllowDevTools()`) the setting is on and `AcceleratorKeyPressed` leaves the browser only the devtools keys, and in Development the reload keys (`IsBrowserAcceleratorKeyEnabled = false` for the rest; the page still gets every key);
    - the default context menu is cut down in `ContextMenuRequested`: editable fields keep Cut, Copy, Paste and Select all, Inspect stays where devtools are on, and anywhere else no menu shows;
    - password saving and general autofill are off, so the SQL tool's password fields are never offered for saving;
    - downloads are denied (`DownloadStarting`);
    - swipe navigation is off, and so is history navigation (mouse back and forward buttons, Alt+Left); `NavigationStarting` with `NavigationKind` back-or-forward is cancelled, which covers the mouse's buttons, Alt+Left and `history.back()` to another document; moves within one document (a router's `pushState` entries) raise no navigation and stay;
    - no host objects (`AddHostObjectToScript`); `AreHostObjectsAllowed = false`, with no opt-in;
    - SmartScreen's reputation check is off (`IsReputationCheckingRequired = false`, decision "SmartScreen default in WebView2"); no app can allow other origins yet, so the automatic switch and its overrides are not built;
    - `<input type="file">` never opens the engine's picker (decided, user, 2026-11-17; decision "M1b's remaining questions without a spike"): it would hand the page a file outside the grant model. `AllowPagePicker()` in C# brings it back. Linux cancels it from `run-file-chooser`. WebView2 has no API: the host calls `Page.enable`, then `Page.setInterceptFileChooserDialog`, in-process for each WebView, and passes `--disable-blink-features=FileSystemAccessLocal`, so the File System Access pickers are absent as on Linux (decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner"; `spikes/window-tests`, 46 of 46 clicks opened no dialog).

    The history-navigation and file-input items are adopted from the research review (user, 2026-11-15) and built in M1b.
  - On Linux: no `enable-developer-extras`, no windows opened by JavaScript, no `file://` access, downloads denied. Built in M1c step 6 (decision "Linux security defaults (implementation record; choices for review)"; `docs/evidence/m1c-security-defaults.md`), with the same opt-ins as Windows:
    - the view's settings before the first load: `enable-developer-extras` only in a Development build or with `AllowDevTools()`, and `javascript-can-access-clipboard`, `javascript-can-open-windows-automatically`, `allow-file-access-from-file-urls`, `allow-universal-access-from-file-urls` and `allow-top-navigation-to-data-urls` off (WebKit's own defaults, set explicitly);
    - downloads cancelled in the session's `download-started`; a `download` link to a `blob:` or `data:` address never gets there, since the navigation lock refuses it first;
    - the page's file picker cancelled in `run-file-chooser` (the page gets `cancel`);
    - the default context menu cut down in `context-menu` exactly as on Windows (Cut, Copy, Paste and Select all in editable fields, Inspect Element where devtools are on, elsewhere no menu);
    - a back or forward navigation to another document ignored in `decide-policy`, except the host's own reload after a lost or ended web process, which WebKit decides as one too;
    - WebKitGTK binds none of the browser keys itself (F5, Ctrl+R, Ctrl+P, Ctrl+F, the zoom keys, Alt+Left and Alt+Right, F12; its inspector is reached only through Inspect Element), and the mouse's back button did not navigate; it has no password manager or autofill. So `AllowAcceleratorKeys()` and `AllowPasswordSaving()` change nothing on Linux, and a Development build has no reload keys there (the development loop, M1d);
    - GTK 4 itself opens its own inspector, which can change any object of the process, on Ctrl+Shift+I and Ctrl+Shift+D in every app by default: outside a Development build the host stops those two keys at the window after the page has had them (`AllowDevTools()` does not bring it back: it is not the page's devtools). `GTK_DEBUG=interactive` in the environment opens it at start: a Production build removes `GTK_DEBUG` (below).
  - Permission requests are denied without a prompt (section 3b).
- **Browser environment overrides are ignored in Production:**
  - `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER`, `WEBVIEW2_USER_DATA_FOLDER` and `WEBVIEW2_RELEASE_CHANNEL_PREFERENCE`;
  - `WEBKIT_INSPECTOR_SERVER`, `WEBKIT_INSPECTOR_HTTP_SERVER` and `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS`.

  The filter is a block-list of exactly these names: everything else passes. In particular the Linux GPU workarounds (`__NV_DISABLE_EXPLICIT_SYNC`, `WEBKIT_DISABLE_DMABUF_RENDERER`, `WEBKIT_DISABLE_COMPOSITING_MODE`) keep working, and `spar doctor` suggests them for blank or white windows.

  **JavaScriptCore's options** (user decision, 2026-11-20, superseding M1c step 5's choice 3): a Production build also removes every inherited `JSC_*` variable before WebKit starts, unless `AllowBrowserEnvironmentOverrides()`; a Development build keeps them for debugging; `JSC_useWasm` always follows the C# declaration (`AllowWebAssembly()`), in every build and with every option. Each variable removed or kept is named in the start-up log, never its value. Checked in M1c step 6: a release WebKitGTK 2.52.6 refuses `JSC_useDollarVM` ("invalid option", a restricted option), so no `$vm` debugging object reaches the page even where the option passes; the options that do work change how the page's code runs (step 5's spike: `JSC_useJIT=false` about 35 times slower, an incoherent pair aborted the app).

  **`GTK_DEBUG`** (user decision, 2026-11-20): treated as the `JSC_*` options are. Its `interactive` flag opens GTK's inspector at start, which can change any object of the process, so a Production build removes it before GTK starts unless `AllowBrowserEnvironmentOverrides()`, and a Development build keeps it. GTK's rendering choices (`GSK_RENDERER`, `GDK_DEBUG`) pass, as the GPU workarounds do.

  The opt-in is `AllowBrowserEnvironmentOverrides()`. Administrator policies set in the registry stay in force. Built in M1b step 5, the Windows list also has the names Microsoft's documentation now uses for the channel (`WEBVIEW2_CHANNEL_SEARCH_KIND`, `WEBVIEW2_RELEASE_CHANNELS`) and the script-debugger variables (`WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER`, `WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER`), for the user's review. **Clearing a variable in-process is enough:** the host removes each one with `SetEnvironmentVariableW` before the loader's first call; on the published NativeAOT executable `--remote-debugging-port` opened no port, and the profile and Runtime folders did not move, while with the opt-in the port opened (and closed at exit) and both folders moved. **On Linux too** (M1c step 6): the host removes the three with libc `unsetenv`, and in Production every other `JSC_*`, after single instance and before any WebKit library loads, and from .NET's copy of the environment; on the Production NativeAOT probe neither inspector server listened, the web process stayed under `bwrap`, and neither the web nor the network process had any of them, while with the opt-in each was in force. A variable that cannot be removed stops the start (exit 70).
- **App identity and folders** (adopted from the research review, user, 2026-11-15; M1b and M1c): an app has a validated app id, from which the per-user data, profile, log and crash folders derive, and Spar exposes them to app code. Development and installed builds never share data or a profile: the folder names differ by channel. Decided (user, 2026-11-17; decision "M1b's remaining questions without a spike"): the id is reverse-DNS, `<SparAppId>com.example.SqlTool</SparAppId>`, required and checked by the build with GTK's application-id rules; the folders are `%LOCALAPPDATA%\<app id>` on Windows and `<app id>` under the XDG data, cache and state folders on Linux; Development appends `.Development` to the id everywhere. Production takes no data-root switch: tests move the folders through the platform's own per-user variables (the security review is in that decision). The host checks the profile folder actually in use, because policy can redirect it, and no file grant or helper may ever reach the profile folder. Built on Windows in M1b step 5: a missing or invalid `SparAppId` fails the build with SPAR130; the id reaches the runtime through the generated catalog (`SparCatalog.AppId`); the folders are `%LOCALAPPDATA%<app id>` or `<app id>.Development`, and so is the process's explicit AppUserModelID; app code reads the folders from the `SparFolders` service; the host records the profile folder in use (`ICoreWebView2Environment7.UserDataFolder`) beside the requested one in the internal `BrowserProfile` service, which M3's file grants read.
- **The browser profile of a shipped app** lives in a per-user, per-app folder, and its location is configurable:
  - Windows: `%LOCALAPPDATA%\<app id>\WebView2`;
  - Linux: the app's XDG data and cache folders.

  WebView2's own default, next to the executable, fails under `Program Files`. The `artifacts/webview2-profiles/` rule applies only to Spar's repository, its tests and local development. Built in M1b step 5 (decision "Security defaults' public shape"): `spar.Folders(f => f.Profile(path))` moves it; a path that is not absolute or is on a network drive (UNC, or a drive letter mapped to a share) stops the start with a message and exit code 70, the app's own start failure.
- **No loopback in Production** (user decision). The desktop host is the only shipped surface.
  - The loopback host is compiled only into development and test variants, like the devtools, and the production boundary test proves it is absent.
  - No real production use was found. Remote use from another machine would be a different product: a web server with TLS and authentication. Revisit only if a real consumer needs it.
- **Publishing a non-Production variant** is a build error. An explicit property (`SparAllowNonProductionPublish`) turns it into a prominent warning, for example for a support build.
- **The devtools lockfile** is readable only by its user: `0600` in `XDG_RUNTIME_DIR` on Linux, the user's temp folder on Windows.
- **Supply chain:**
  - The build runs `npm ci --ignore-scripts --no-audit`, with a property to allow scripts.
    - **Verified** (`spikes/npm-ignore-scripts`): the installed tree was file-for-file identical to a normal `npm ci`, and the native packages (Rolldown, Lightning CSS, TypeScript 7) work. No package in either frontend needs scripts.
    - The flag goes on the `npm ci` command line only: in `.npmrc` it would also skip the app's own `pre`/`post` script hooks.
    - After install, the build warns about lockfile entries with `hasInstallScript` that match the current OS.
  - NuGet audit runs as an error in Spar's repository, and as warnings by default in apps: `Spar.Sdk` forces nothing. An app created from the template starts with `MSBuildTreatWarningsAsErrors` on (section 7), so an audit finding fails its build until the app lists the advisory in `MSBuildWarningsNotAsErrors` or turns the setting off.
  - The packing step rejects source maps, version-control and secret files, links, and names that differ only by case (adopted from the research review, user, 2026-11-15; built in M1a as SPAR110, which also refuses hidden files and types the hosts do not serve; decision "Frontend packing built").
  - The executable embeds third-party notices: the frontend's licences (Vite's `build.license`), plus WebView2's and the static loader's (adopted the same way; M1a). The frontend's are built, as `Spar.ThirdPartyNotices`; the other two arrive with the Windows host. The build reports them in one informational line, `SparThirdPartyNotices` replaces them with the team's own file or turns them off, and `spar doctor` flags packages with no licence or a copyleft one (decision "Source maps stay out of the executable; third-party notices are reported and copyleft is flagged").
  - Source maps never ship, devtools in Production or not: Debug gets them from the Vite dev server (same decision).

**Later (M4):**
- signing the executable and the Linux packages;
- privacy defaults for telemetry (no SQL text or parameters in spans unless opted in);
- redaction of connection strings in logs.

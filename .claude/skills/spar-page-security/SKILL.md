---
name: spar-page-security
description: "Change Spar's page security: the policy declared in C# (`spar.ContentSecurity`, `spar.Permissions`) and its analyzer (SPAR101–SPAR105), the composed Content Security Policy and Permissions-Policy headers, the navigation lock and new windows, the permission gate, the host-injected script (heartbeat answer, violation listener and page error reports), the `report-uri` endpoint and the violation log with its flood limits, and Subresource Integrity and Trusted Types on the bundle."
---

# Work on page security

The design is plan section 3b, with the defaults and overrides of 3c, in `docs/design/security.md`; the public shape is decision "Page security's public shape", the build record "Page security (implementation record; choices for review)", and the evidence `docs/evidence/m1b-page-security.md`. Host work also needs `spar-webview2-hosting` or `spar-webkitgtk-hosting`, analyzer work `spar-contract-generation`, the plugin and the injected script `spar-typescript-conventions`. `docs/pitfalls.md` ("Page security") holds what engines do.

## The rule

- **Anything that is a security risk is blocked by default** (plan 3c); an app widens it only through a named C# option whose documentation says the risk, when it is reasonable and the safer alternative. Never add a default exception, a string keyword, or a host-side relaxation without one.
- **Page content is untrusted,** the app's own bundle included. Every message, batch and report from the page is untrusted input with a bound before any work.

## The declaration

- **C# only, read by the build:** `spar.ContentSecurity(csp => …)` and `spar.Permissions(p => …)` are statements of their own directly in the `SparApplication.Create` configuration, with constant values. `PageSecurityAnalysis` (in `Spar.Generator`) reads them for the generator (the catalog's `PageSecurity`), the analyzer (`PageSecurityAnalyzer`, located diagnostics as you type) and the client step (`csp.json` in `@spar/app`). Change all three together; one analysis, one model.
- **Diagnostics:** SPAR101 a call not directly in `Create` (apps only: top-level statements), SPAR102 a value that is not a constant, SPAR103 a shape the build cannot read, SPAR104 a source the rule refuses, SPAR105 (warning) `StyleNonce()` with `AllowInlineStyles()`. Messages name the method to use.
- **One source rule:** `CspSourceRule` (`src/Spar/Hosting`, linked into the generator, so netstandard2.0 APIs only) accepts hosts and schemes (and `'self'` in `FrameSrc`) and refuses keywords, nonces, hashes, `*`, `javascript:`, credentials, and `data:`/`blob:` in `ScriptSrc`, each with the named method. The builder throws its message at run time; the build reports it.
- **Normalized the same way at build and run time:** sources lower-case scheme and host, each once, in the order first declared; permissions in the enum's order. A Development build compares the run's declaration with the build's (`PageSecurityMismatch`, behind `SparDevelopment.IsSupported`) and logs an error when they differ; the host uses the build's.
- **`Permission`** covers the features both `Permissions-Policy` and WebView2's permission kinds know; adding one needs the header feature(s), the WebView2 kind (and the WebKitGTK request type) and the stand-ins in `tests/AppModel`.

## The policy

- **One valid policy:** `PagePolicy` writes each directive once, in a fixed order, values grouped (base, opt-in keywords, the app's sources, the build's hashes, the document's nonce), no duplicates, no empty directive. Browsers keep only the first copy of a repeated directive. Test every new value with "each directive once".
- **`'unsafe-inline'` beside a nonce or hash is ignored by browsers,** so with `AllowInlineStyles()` no nonce or hash goes into `style-src`.
- **Hashes the build needs** are read by the host from the served HTML at start, off the UI thread: only Spar's own `<script type="importmap">`. Never hash other inline scripts.
- **The headers go on every response,** worker scripts included (a worker takes its policy from its own response), and an HTML document gets its own policy with a fresh nonce and `<meta property="csp-nonce">` when the app asked. Compose once; only the nonce changes per document. Hosts never compose on the UI thread beyond concatenating the prepared parts.
- **Loopback serves the same policy** from the catalog (development and test variants only), so browser tests see what the window sees.

## The host

- **Navigation lock:** cancel top-level and frame navigations off the app origin (frames may load `about:blank` and `about:srcdoc`); answer foreign documents 403; handle every new-window request so no WebView window opens; open a user-initiated `http(s)`, `mailto` or `tel` address without credentials with `ShellExecuteW` on an STA thread of its own, never on the UI thread; block and log the rest, the app's own origin included.
- **History and pickers** (plan 3c, built in M1b step 5): `PageGuard` also cancels back-or-forward navigations to another document unless `AllowHistoryNavigation()`; the page's file pickers are closed by `PagePicker` and `BrowserEnvironment` unless `AllowPagePicker()` (`spar-webview2-hosting`, "Production defaults").
- **Permission gate:** allow a declared feature from the app's origin, deny everything else, never prompt, `SavesInProfile = false`; log each kind once. Never ask for the camera or microphone in an unattended run.
- **Logs:** state what was blocked and why; never put a page value in a log line uncut (`CspReportReader.MaxText`).

## The injected script and reports

- **The host-injected script** (`src/Spar.Windows/PageScript.js`, embedded; settings written in by `PageScript.cs`) runs at document creation, before any page script, in the app origin's top document only. It answers the heartbeat's `ping` from its message handler and runs the violation listener. Since M1b step 7 it also reports the page's errors (below). It defines nothing on `window`, keeps bound references taken before page code runs, and uses no `eval`, string timers or DOM sinks. Page code can post any message itself, fake reports included: never give it more than that.
- **Flood limits, both sides:** the listener groups by directive, blocked address and source location, posts at most one `csp` message a second, reports at most 100 groups per document and stops listening after 1,000 violations (the engine's per-event cost); the host bounds groups per document again, summarizes at most every 10 s, reads `report-uri` bodies up to 16 KiB within 20 at once and 5 a second, and parses and logs on `CspViolationLog`'s task from a bounded queue. Set new numbers from a flood test (100,000 distinct violations; identical ones are one event).
- **The page's errors** (M1b step 7; decision "Logging (implementation record; choices for review)"): the script wraps `console.error` in Production and Test builds (no event reports it, and Production refuses the DevTools Protocol's console events; a Development build leaves it alone and the host reads `Runtime.consoleAPICalled`, so devtools' links stay the page's) with a method named `error` that calls the page's original with the same `this` and arguments and returns its result, and listens to `error` (filtered to the window as target: resource failures reach a capturing listener too) and `unhandledrejection`, never cancelling either. It reads thrown values without running page `toString` or `toJSON`, guards every read with `try` and a re-entrancy flag, and gets `console.error`'s caller from `Error.captureStackTrace(holder, wrapper)`. The same `reporter` as the violation listener batches `errors` messages: one a second, 100 groups and 1,000 errors per document (each costs the page 3–18 µs more while reported), then it stops, removes its listeners and puts the original `console.error` back if the page did not wrap it meanwhile. `PageErrorLog` (`Spar.Logging`) reads them untrusted and logs under `Spar.Page` with `CspViolationLog`'s rules.
- **One log for two channels:** a group's count is the larger of the listener's and the endpoint's, so a violation both report is counted once. Production logs each new group and the summaries; Development adds the C# that would allow it (`CspAdvice`, passed in behind the feature switch so Production carries none).

## The bundle

- **The Vite plugin** (`src/Spar.Client/src/vite/`) adds sha384 `integrity` to the entry script, stylesheets and module preloads, and an import map with the lazy chunks' hashes first in `<head>`, from the files as written (`writeBundle`). An external script or stylesheet without `integrity` fails the build and the development server; fonts are exempt. `WithoutIntegrity()` turns both off through `csp.json`.
- **Trusted Types** are on unless `WithoutTrustedTypes()`. `new Worker(url)` is a script sink under them: app code passes the address through a policy.

## Verify

- Unit tests: `PagePolicyTests`, `ContentSecurityTests`, `CspViolationLogTests`, `PageErrorLogTests`, `PageGuardTests` (Spar.Tests); `PageSecurityTests` (generator, every diagnostic at its place); `CspFileTests` (client step); `test/integrity.test.ts` and `test/pageScript.test.ts` (SDK).
- Real runs: `tests/apps/SecurityProbe` with `docs/evidence/m1b-page-security/scenarios.sh` (Production NativeAOT and Development), windows shown without activation and offscreen, `LOCALAPPDATA` under `artifacts/`. A Production page busy for 15 s shows the hung-page dialog: run long floods in Development with `SPAR_AUTOMATED=1`.
- The Notebook's Production executable with 0 violations, its browser journeys, `tests/AppModel` (the documented `Program.cs` through real Spar), and the production boundary (`PageSecurityMismatch`, `CspAdvice` and their texts absent).

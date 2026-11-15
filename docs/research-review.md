# Review of earlier Spar versions and other frameworks

This is a design record, written 2026-11-14, like `plan.md` and `decisions.md`, so it names earlier versions and other frameworks. Items without a source tag are Spar requirements stated in Spar's own words.

It collects topics that matter to v7 from:
- the earlier Spar repositories: `spar`, `spar_old`, `spar-experiments`, `old/spar_spikes`, `spar.v2` to `spar.v6` (including `experiments/application-factory`), and `old/spar.v7`;
- the Tauri v2 documentation.

The reviews only read documents and code; nothing was re-run. Every finding is research input. Before it changes the plan, it becomes a Spar requirement or a spike.

**State on 2026-11-15:** the items marked "Adopt (M1)" below, the two-stage close and the threat-model note are adopted (user decision) and listed in the plan's M1 slices, marked "(RR)" there. Their numbers are still set by measurement in the slice. The "Discuss" and "Later" items, and the "Adopt" items for M2 to M4, stay here until their milestone starts (decision "Plan and guidance review; M1 split").

Sources are abbreviated:
- `v1` stands for `spar`, `spar_old`, `spar-experiments` and `old/spar_spikes`;
- `v2` to `v6` are the numbered repositories;
- `AF` is v6's application-factory experiment;
- `T` is Tauri.

Each item ends with a proposal:
- **Adopt** means put it in the plan at the stated milestone.
- **Discuss** needs a user decision.
- **Later** waits for a consumer.
- **Ignore** means v7 already chose better.

## 1. What every source agrees on

- **No version shipped any app-shell feature.** Tray, menus, notifications, deep links, file associations, updates, installers, printing, localisation and crash reporting were all deferred everywhere. The few desktop features built were in v5: single instance, tray, clipboard and window placement. v7 needs an explicit list driven by the SQL tool (section 6).
- **Process weight sank earlier versions.** v1 stalled at SP-22 before setting any budgets. v4's 29 immutable candidates and hash tables consumed its schedule, and its agent trials never ran. v3 ended with 506 of 637 questions open. v7's light decision log is the right correction; keep it.
- **Bridge limits existed in every implemented version** (v1, v2, AF, v4), and v7 has none. See section 2.
- **Close and shutdown need user-facing policy,** not only teardown order (v1, v3). See section 3.

## 2. The bridge: limits, admission and robustness

| Topic | Evidence | Proposal |
|---|---|---|
| **Entry limits:** maximum message size, calls in flight per document, a bounded send queue, open streams per document. A rejected call never reaches the handler. | v1: 64 KiB, 32 ops, 8 execution slots. v2: 64 KiB, 8 in flight, a queue of 256. AF: 16 concurrent, 128 pending, 64 KiB per stream item. v4: 4 active, 64 queued. T documents none, and large JSON is slow. | **Adopt (M1).** Pick the numbers by measurement, not from old versions, and document and test them. |
| **Admission before any work:** reserve capacity before parsing, check the document and command before converters, DI or the handler run, and release a slot only when the work has really ended. | v1 (spar_old `docs/06`), v3 `docs/28` | **Adopt (M1).** |
| **A "not started" outcome:** `Busy` or `NotStarted` when admission refuses or the call was never sent, so the page knows a retry is safe. Still never retried automatically. | v3, AF (`ExecutionState`, `host_busy`) | **Covered (2026-11-14):** `CallError` carries `execution: "notStarted"` with the kinds `unavailable` and `rejected` (decision "CallError kinds"). Earlier note: it extends the three outcomes cleanly. |
| **Increasing call IDs**, with duplicate or lower IDs rejected, so a duplicate never runs twice. | v1, v2 | **Adopt (M1).** |
| **Separate capacity for control messages** (cancel, close, readiness), plus reserved essential slots and visible drop counts. | v1, v3 | **Adopt (M1).** |
| **Parse off the UI thread:** the native callback only copies bytes and enqueues without blocking. Application code, including its synchronous prefix, runs off the STA. | v3 | **Adopt (M1).** |
| **Per-call deadlines**, plus idle timeouts for streams. | AF: 15 s by default. The friction there: calls that wait on a person (pickers) needed `timeoutMs: 0`. | **Decided (2026-11-14):** no default deadline. The page passes `AbortSignal.timeout(ms)` as the call's `signal`; C# may set an optional backend limit (`[SparCommand(Timeout = …)]`) folded into the command's token. Streams get no default idle timeout. Earlier note: a deadline needs a rule for calls that wait on the user. |
| **Serial groups:** commands in a named group never overlap, for example recipe saves. | v4 | **Discuss (M3).** |
| **Exception redaction in Release:** unhandled exceptions reach the page as a correlation id, with details only in development. An oversized result fails only its own call. | — | **Adopt (M1).** |
| **A bytes wire type** for blobs and exports, without base64 in JSON. | T (raw `Response`/`ArrayBuffer`); alternatively opaque resource handles | **Discuss (M3).** `spikes/wire-details` covers `byte[]` for now. |
| **Per-command or per-window permissions:** an allow-list of the commands a window may call. | v1, v2, v4, T (app commands are open to every window by default) | **Decided (2026-11-14):** every window may call every command by default; a window can declare an allow-list in C# (for example `w.Commands(c => c.Allow<IConnections>())`), and other calls are refused before running with `rejected` / `forbidden-command` / `notStarted`. Earlier note: it has little value with one origin and the navigation lock; it matters with multi-window. |
| **Protecting the bridge from the page:** an isolated script world on WebKitGTK with a relay in the top document only, frozen bridge entry points, and an optional `Object.prototype` freeze. | T (`freezePrototype` opt-in) | **Adopt** the Linux design (M1). **Discuss** the freeze as a 3c opt-in. |

## 3. Application lifetime

| Topic | Evidence | Proposal |
|---|---|---|
| **Two-stage close.** First a close *request*: async guards (page first, then backend) may save, ask or refuse. Repeated clicks join the request already running, and approvals from an old document are ignored. Only then does teardown start. | v3 `docs/59`. Requirements: a deadline, coalesced requests, child windows closed before owners, a page `onCloseRequested` | **Adopt (M1 or M3).** The SQL tool needs "a copy is running, close anyway?". |
| **An automated mode** that skips close prompts when agents or tests run the app. | — | **Adopt** with two-stage close. |
| **Hang policy:** after about 10 s of incomplete shutdown, a native "Keep waiting / Exit now" prompt; the library never kills the process on its own. One automatic renderer reload, only after a document had loaded. | v1 (C17–C19), v3 | **Adopt, simplified (M1).** |
| **Idle renderer hangs raise no event:** `ProcessFailed(RenderProcessUnresponsive)` needs input that goes unacknowledged; an idle hung page gave nothing in 60 s. | v4, v5 | **Pitfall now.** **Heartbeat adopted pending a spike (user, 2026-11-14):** see plan section 3, "Hung pages". |
| **OS session end:** the window ignored a confirmed `WM_ENDSESSION`. Treat `WM_QUERYENDSESSION` as a non-cancellable, best-effort phase. | v1 (fixed, with 4 regression tests) | **Adopt** as a required host test on both OSes. |
| **The young-WebView shutdown:** closing within about 0.5 s of ready took about 6.4 s; later, about 210 ms. The cause was SmartScreen's shutdown work (101 sleeps of about 60 ms). `IsReputationCheckingRequired=false` removed it in 18 of 18 runs, and waiting 1.5 s after ready also helped. | v3 (ETW), v4; T always disables `msSmartScreenProtection` | **Pitfall now.** **Decided (2026-11-14):** reputation checking is off while the page stays on the app origin and on when the app allows other origins, with a C# override both ways (decision "SmartScreen default"). |
| **Startup failure that users can see:** a native "could not start" dialog (replaceable through `OnError`, exit code 1) when the WebView runtime is missing, too old or broken. | v1, AF, v4 (a Release failure was invisible) | **Adopt (M1).** Include a minimum-version check inside the exe, since a single exe has no installer (T). |
| **Single instance:** opt-in, with a user- and session-scoped mutex plus a current-user pipe (Windows) or D-Bus (Linux) that forwards arguments, acknowledged and never replayed. It is the base for deep links and file associations. | v1, v2 (profile lock), v4 (instance policies; a shared profile was qualified), v5, T | **Decided (2026-11-14):** single instance is enforced before the WebView environment, and a relaunch forwards its arguments to the first instance (Windows mechanism to settle in M1; Linux lock file plus Unix socket). An app that wants several instances says so in C#; their data folders are settled when one needs it. |
| **Last window, Quit and background work:** whether work outlives its window. | v3 | **Later.** |

## 4. Host details for M1

Small items, each found as a real bug or verified behaviour:

- **Focus:**
  - forward `WM_SETFOCUS` to `MoveFocus(Programmatic)`, or Tab never reaches the page;
  - move focus into the WebView when the window is activated (v5).
- **Window state:**
  - keep the controller's `IsVisible` in step with minimized, hidden and tray states;
  - call `NotifyParentWindowPositionChanged` when the window moves (v5).
- **Production defaults to add to 3c:**
  - script dialogs off (`alert`/`confirm`/`prompt`, v2);
  - history navigation off (mouse back, Alt+Left, Runtime 115+);
  - a policy for `<input type=file>`.
- **Navigation lock detail:** WebView2 sends a navigation's request before the host cancels it, which uses up one-time links. Answer it with an empty response. Plan 3b instead filters every document request (`AddWebResourceRequestedFilter("*", DOCUMENT)`) and answers foreign documents with 403. With that filter, `spikes/csp-navigation` saw no foreign request reach the network, and the handler was never called for the cancelled navigations, so neither response was exercised.
- **Check the profile folder actually in use** (`ICoreWebView2Environment7.UserDataFolder`), because policy can redirect it (v2).
- **Profiles:**
  - environments sharing a profile must use identical options (v1, v5, T);
  - WebKitGTK without an explicit data manager falls back to a shared `webkitgtk` folder;
  - no file grant or helper may ever reach the profile folder (T).
- **Background colour:** set it through `ICoreWebView2ControllerOptions3.DefaultBackgroundColor`, feature-detected (T). Tauri also uses the environment variable as a fallback; v7 does not: `spikes/theme-startup` did not need it, and `WEBVIEW2_DEFAULT_BACKGROUND_COLOR` overrides the creation options (plan 3d).
- **Linux GPU workarounds must keep working in Production:** `__NV_DISABLE_EXPLICIT_SYNC`, `WEBKIT_DISABLE_DMABUF_RENDERER` and `WEBKIT_DISABLE_COMPOSITING_MODE` fix blank or white windows and crashes with NVIDIA. The 3c environment filter must block only the variables it names, and `spar doctor` should suggest these (T).
- **Teardown in a visible window:** a handle leak in WebView2's network service appeared only with visible windows. The mitigation was to hide the controller before `Close`, navigate to a blank page, and post the final close outside the callback. Recheck it in M1 (v1).
- **Long paths:** lengthening the data root from 114 to 169 characters broke PDF staging and `localStorage` persistence. Add a long-install-path test (v6).
- **Memory while minimized:** a LowMemory policy saved about 10 MiB (13%) and restored in 8.8 ms; Suspend saved nothing and broke subscriptions (AF). **Later (M3 or M4).**

## 5. App data, settings and windows

| Topic | Evidence | Proposal |
|---|---|---|
| **App identity and a data-folder API:** a validated app id, plus `Paths.DataDirectory` and `ProfileDirectory`. Section 3d already needs a place to save the theme. | AF, v4 | **Adopt (M1).** |
| **Folders separated by channel:** development and installed builds never share data or profiles. | v3 | **Adopt (M1).** Today a Debug build could share a profile with an installed release. |
| **The backend owns durable data;** browser storage holds only disposable UI state. A file lock guards app data when several instances run. | v4 | **Adopt** as guidance. **Discuss** the lock with single instance. |
| **Settings:** versioned JSON outside the WebView profile; staged write, then replace; an invalid file kept with a recovery message; reset makes a backup. | v5 (a "recipe, not a framework") | **Discuss (M3).** The SQL tool's saved recipes need this. |
| **Window placement:** normal bounds, DPI and maximized state; the nearest monitor, clamped to the work area; writes coalesced; restored *before* the first show, so it doesn't flash. | v1, v5, T | **Adopt (M3)**, together with 3d. |

## 6. Native services and desktop features: a list for the SQL tool

**Proposed scope** (to decide):

| Feature | Notes from the sources | Proposal |
|---|---|---|
| **File access** | AF: single-use grants, at most 16, five-minute expiry, revoked with the document; atomic writes through a temporary file; error mapping to `FileAccessError`. v5: picker options (title, multi-select, filters) were missing. v4: staged create-new writes and streamed reads for exports. AF moved the pickers to the backend, so the page never held IDs. | **Adopt** the limits, atomic writes, error mapping and options. **Discuss** page-held grants against backend-owned pickers. |
| **Clipboard** | v5: Unicode text, bounded, handles a busy clipboard. T: `navigator.clipboard` is enough, with clipboard-read behind the permission gate. | **Adopt (M3).** |
| **Drag and drop** | Dropped files become opaque grants, and a drop grants nothing on the parent folder (v3, v5). Keep HTML5 drag and drop working; on Windows the native drop target replaces it by default (T). | **Adopt (M3).** |
| **Tray and close-to-tray** | v5: recreated on `TaskbarCreated`, and Quit goes through the normal drain. On Linux, T gets no click events (menu only), and a StatusNotifierItem tray needs a StatusNotifierWatcher, which GNOME lacks. | **Discuss**, for SQL monitoring. |
| **Menus and accelerators** | v1: dark title bars don't theme popup menus. | **Discuss.** |
| **Notifications** | Unpackaged Windows apps get balloons only. Windows notifications work only for installed apps, and in development they show PowerShell's name (T). | **Later**, after the installer decision. |
| **Deep links, file associations** | The URL arrives in a new process's arguments, so it needs single instance. Validate it: it can be faked (T). | **Later.** |
| **Printing** | `window.print()`, or an SDK `print()`. 3c turns off the print accelerator. | **Discuss.** |
| **Custom title bar** | WebView2 non-client regions (`app-region: drag`). Hit-testing must use host-local state. Snap Layouts need native buttons (v3, v5). | **Later.** |
| **Global shortcuts, autostart, positioner** | On Linux, global shortcuts in practice work only on X11 (T). | **Ignore.** |
| **Relaunch** | Needed by an updater and some settings. | **Later (M4).** |
| **External links** | An allow-list of schemes for the open-in-system-browser call: `http(s)`, `mailto` and `tel` by default (T). Only after a user gesture, and without credentials in the URL. | **Adopt** in 3b. |

## 7. Streams and subscriptions

- **A publisher helper, `EventSource<T>`:** per-observer bounded channels. A snapshot-plus-registration overload ensures no change is lost between the snapshot and the registration (AF; +48 KiB). **Adopt (M3)**, for SQL monitoring.
- **Overflow modes per subscription:** drop-oldest with a `gap` (v7 today), latest value only (v5, suited to dashboards), or detach with `SlowConsumer` (AF). **Covered (2026-11-14):** drop-oldest with `gap` stays the default, and latest-value-only is the coalesced subscription (decision "Coalesced subscriptions"). Detaching a slow consumer is not adopted.
- **The handle:** `ready` (admission) separate from the first item, plus `closed`, an idempotent `dispose`, and a stream that can be iterated only once (AF). **Adopt (M3).**
- **Trace context on streams and subscriptions,** which the telemetry spike did not cover. **Adopt (M3).**

## 8. Testing

| Topic | Evidence | Proposal |
|---|---|---|
| **Typed C# test clients** generated per contract, keeping `Result` and `IAsyncEnumerable`, used at the managed and loopback levels. | AF (124 cases) | **Adopt (M1).** Without them, tests write envelopes by hand. |
| **A `SparTestedApplication` item** that becomes a project reference before restore; the artifacts to build inferred from the testing packages; a generated `Projects.X` marker; relocatable outputs. | AF (qualified in VS and VS Code) | **Adopt (M1)** as the test-SDK shape. |
| **Typed page-side mocks** generated from the contracts, for Vitest and for browser-mode tests. | T (`mockIPC`) | **Adopt (M1 or M2).** |
| **Generality fixtures for the package-only consumer test:** a folder name with a space, an assembly name different from the project, a custom app id, a non-default frontend folder, paths containing `caffè`. | AF Measure, v4 | **Adopt (M1).** |
| **A UI Automation smoke test of the Release executable,** which has no devtools. | v4, v6 Sandbox | **Adopt (M1 or M4).** |
| **Fixture rules:** assertion, capture and teardown failures reported together; unresolved cleanup is not a pass; an empty test selection fails; explicit SKIP lines. | v4 | **Adopt (M1).** |
| **Native pixel checks:** a CDP capture does not prove the native window painted. | v2 | Covered by `spikes/theme-startup`. |
| **Sandbox lessons, recorded now:** the elevated runner makes WebView2 ignore environment overrides, so the profile has to be set by policy in the guest; the guest's WebView2 registration disagreed with the files on disk; no CDP in the guest (use UI Automation or winapp); give the guest an ID first; read results from the `wsb exec` JSON exit code; the colour mode cannot be set from `.wsb`. | v5, v6 | **Pitfalls now.** Write a Sandbox skill in M4. |
| **Antivirus delays:** Defender trust checks made up 99% of loader waits (1.28 s to 13.7 s), because randomized fixture paths defeated its caching. A Release-only `PathMap` fixed it. | v2 | **Pitfall now.** |

## 9. Development loop and agent tooling

- **Contract edits must always restart, never patch.** In a single project, Hot Reload accepted a DTO change that a fresh compile rejected; `Watch=false` was not enough. v2 needed a separate contract assembly (v2, v3). **Adopt** as an M1 Hot Reload check: DTO and contract edits force a restart.
- **A Hot Reload signal to the page:** an update revision lets the page refetch, and a compile error shows "Edit not applied". The callback can arrive after the patched code has run (v2). **Decided (2026-11-14):** a Debug-only `backend-updated` event with a default toast (plan section 4, "Hot Reload signal"); "Edit not applied" stays in `dotnet watch`.
- **Child processes:**
  - a job object for everything `spar dev` and `spar snap` start, assigned at creation (v2, v4);
  - children get a closed stdin (v4: an inherited stdin stalled `npm install`).

  **Adopt (M2).**
- **npm:**
  - use `npm ci --no-audit` (v2: auditing hung a CI job for 30 minutes), with vulnerability checks as a separate gate;
  - find Node installed through version managers (fnm, Volta, mise, asdf) in `doctor` and the build.

  **Adopt.**
- **Devtools and MCP rules:**
  - every command carries its target identity, and a mismatch returns `stale_target`;
  - a reused ID returns `duplicate_id` and is never replayed;
  - a timeout means unfinished, never failed;
  - password fields are redacted;
  - results are bounded;
  - images come back as content, not paths;
  - each UI action links to the calls it caused;
  - errors give file:line;
  - idle status is short;
  - outputs have schemas and read-only annotations.

  Sources: v2, v3, v4. **Adopt (M2).**
- **Linux devtools:** the Chrome DevTools MCP tool names (`take_snapshot`, `click`, `fill`) can be implemented with an injected DOM script, so they work on every engine without CDP. Its synthetic input is untrusted, and it cannot see CSP violations. **Adopt for Linux; discuss for Windows**, where CDP keeps trusted input.
- **A development relay the app connects out to**, encrypted, instead of a listening port. **Discuss (M2).**
- **Startup error format for the tools:** stable codes, exit 1, one JSON line on stdout and one human line on stderr. A failure names the process role, the exit code, the log paths and the next action (v2). **Adopt (M2).**
- **Build identity:** a publish receipt or an embedded build identity (mode, revision, dirty tree, tool versions, contract identity, payload hashes; v2). Devtools `version` reports when sources are newer than the build (v4). **Adopt (M1 or M2).**
- **F5 debugging** of the Desktop profile in Visual Studio and VS Code as an M1 exit criterion. Earlier lessons: debugger Stop kills the app rather than closing it; a drive-letter case mismatch broke Hot Reload, fixed by `PathMap` (v1, v3). **Adopt.**

## 10. Delivery and supply chain (M4 unless noted)

- **Third-party notices:** the frontend's licences (Vite `build.license`), plus WebView2's and the static loader's notices, embedded in the exe (v2). **Adopt (M1),** cheap.
- **Asset checks while packing:** reject source maps, VCS or secret files, links and case collisions. **Adopt (M1)** in Brotli packing.
- **WebView2 runtime delivery modes:** download bootstrapper (0 MB), embedded bootstrapper (+1.8 MB), offline installer (+127 MB) or fixed runtime (+180 MB, and you own the patching) (T). A fixed runtime was never tested (v2).
- **Signing:** since 2024, EV certificates no longer give instant SmartScreen reputation (T).
- **Updates:** a signature is mandatory and independent of Authenticode, plus `relaunch` (T).
- **SBOM, `SHA256SUMS`, and whether symbols ship** (v4 put a 16.5 MB `.pdb` next to a 3.8 MB exe).
- **AppImage:** 2–6 MB becomes 70 MB or more; it must be built on the oldest supported glibc (T).
- **Asset codec control:** on a 1.4 MB synthetic corpus, the whole exe was 1.60 MB with Deflate, 2.27 MB with Brotli and 2.56 MB raw (v1). v7 measured Brotli as 44% smaller than raw on a 2.8 MB bundle. **Adopt** a Deflate control on the real bundle before M1 packing. Status: `spikes/asset-packing` measured Deflate only inside the zip container, where its payloads were about 15% larger than Brotli's and the zip reader cost 0.5–1.0 MB. Per-file Deflate inside Spar's own index, which is what v1's result suggests, is still unmeasured.

## 11. Measurement (M4, method from now)

| Measure | Values found |
|---|---|
| **Host private memory (AOT)** | 5.6–8.6 MiB across v1, v2, v4, v5, AF; CoreCLR about 13 MiB (v4) |
| **Browser processes** | 152–256 MiB private, about 187 MiB with the group counted once (v4); 7 processes, about 390 MiB summed working set (v3) |
| **First command or ready** | v4: 261–315 ms (AOT), 328–453 ms (CoreCLR); AF: 277 ms fresh, 226 ms reused; v2: 557 ms fresh, 317 ms reused |
| **Profile growth** | about 1.25 MiB per launch, from browser metrics files (v3) |
| **Microsoft DI** | +176 KiB on Measure; in a benchmark 1.99 MB against 1.08 MB for a custom container, and 81 against 52 µs to first resolve (AF). This feeds the open DI question. |

**Rules:**
- declare budgets before measuring (v5);
- never force a GC to make a cleanup check pass;
- an unavailable counter is not zero;
- count a shared browser group once;
- observers distort timings (v4: a CIM query before close changed shutdown);
- keep outliers, and investigate them with ETW (v3: 18–22 s first launches);
- run same-process soak cycles (v1: +2.7–6.8 MiB over 20 cycles, unattributed);
- consider an automatic size ledger per commit (v5).

## 12. Smaller decisions to revisit

- **`InvariantGlobalization=true` by default** has unwritten localisation consequences, and SqlClient already needs it off (v1). Decide it together with localisation.
- **Null against missing on the wire:** v1 kept them distinct. Decide with the wire details (`spikes/wire-details`).
- **A short threat model section** naming what the controls defend. Frontend content is untrusted, even a compromised bundled SPA; a nonce proves freshness, not trust; same-user native code is not sandboxed (v1). **Adopt** in the plan.
- **A CSP diagnostic:** once Spar adds a hash or nonce to a directive (for example the 3d theme style), browsers ignore that directive's `'unsafe-inline'`, which silently breaks CSS-in-JS. Warn at build time (T). **Adopt** in 3b.
- **Static checks on the generated client:** a contract hash in the generated TypeScript, plus a gzip budget and licence checks in the frontend's `check` script. **Decided (2026-11-14):** the contract fingerprint (already planned) yes; an optional bundle budget (`SparBundleBudget`, off by default); no built-in licence check, the docs point to existing tools.

# Window-test automation: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/window-automation/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/window-automation) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-18. Host: Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 157.0.4341.60 (Evergreen), Microsoft.Web.WebView2 1.0.4314.35 (loader only), Microsoft.Playwright 1.64.1. Every window was shown offscreen at (-32000, -32000) with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and `SW_SHOWNOACTIVATE`, `IsReputationCheckingRequired = false`; all page input went through CDP, never through the OS. Every user-data folder was under `artifacts/webview2-profiles/window-automation/` and was deleted at the end; no `msedgewebview2`, `node` or harness process of the spike was left.

**Question** (M1b step 8, the `Window` surface): how do window tests drive the page?
- **Option A:** Microsoft.Playwright attached with `ConnectOverCDPAsync` to the WebView2 browser started with `--remote-debugging-port=0` (Test variant only), port read from `EBWebView\DevToolsActivePort`. JIT test projects only.
- **Option B:** Spar's own small API over the in-process `CallDevToolsProtocolMethod` and DevTools event receivers. No port; NativeAOT.

Both drive the same page and the same 28-step journey. The harness mirrors the host as M1b builds it: one environment per test process, one InPrivate controller per test, the page served on `https://app.localhost` through `WebResourceRequested`, and the host's own `Page.enable` + `Page.setInterceptFileChooserDialog` on every controller before its first navigation.

## Summary

| # | Question | Result |
|---|---|---|
| 1 | Does the library work on WebView2 over the attach? | **Yes, for page-level work.** The journey passed 60 of 60 (plus 30 in two parallel processes). Locators, auto-waiting, `Expect`, fill, click, check, select, keyboard, screenshots, `AriaSnapshotAsync`, evaluate, `WaitForURLAsync`, dialogs, console and page errors, network events, its file chooser and tracing all worked. **Not working:** route interception (the handler was never called for requests the host serves), video, new pages or contexts (one opened a **real visible window on the desktop**), `page.CloseAsync` (never returns), and the context-level APIs (they act on the wrong browser context) |
| 2 | Identity: can a test find its page? | **All pages land in one default context** (1 context, 4 pages: the anchor and three tests). Finding the page by a unique token in the URL took 0.0 ms (the library had already attached when `NavigationCompleted` arrived); the target id from the host's `Target.getTargetInfo` matched the library's in 3 of 3 |
| 3 | Isolation | **An InPrivate anchor leaks state between tests**: 8 of 8 consecutive tests, over two runs, saw the previous test's `localStorage` and cookie. A default-profile anchor, or none, isolates: 0 leaks in 16 checks. **InPrivate controllers alive at the same time share storage** unless they have different `ProfileName`s. This applies to both options |
| 4 | Interference with the host's CDP calls and the picker block | **None seen.** With the library attached, a file-input click was still intercepted by the host's session and opened no native dialog: with no library listener, with the library's own file chooser (`RunAndWaitForFileChooserAsync` + `SetFilesAsync` worked, and the host got its event too), and after the library turned its interception off. The host's in-process calls kept working |
| 5 | Exposure (A) | While a test runs, **any local process can list and drive the page** with no credentials: `curl /json/list` and a WebSocket from a separate `node` process ran script in the page, took a screenshot and listed the browser's targets. After exit, the port file stays behind (every run) and the port is closed (connection refused at once) |
| 6 | Cost per test (p50 / p95, 60 runs) | **A:** 1,799 / 1,843 ms. **B (NativeAOT):** 1,564 / 1,660 ms. Controller creation, navigation and close are the same (≈58 + 30 + 13 ms); the difference is in the journey (below) |
| 7 | Cost per process | **A:** about 0.6 s to ready (driver start 311–417 ms, connect 29–36 ms; **one connect took 4,460 ms** when two test processes started together), plus a `node` driver of 128–171 MB private bytes. **B:** 0.22–0.27 s to ready, a 12 MB test process, a 3.79 MB executable |
| 8 | Per action | **A is faster per action:** a click 40.6 ms against 57.1 ms, an assertion that already holds 0.9 ms against 13.5–17.5 ms (B's role lookup through `Accessibility.queryAXTree` often waits for the renderer's next frame). **B waits for asynchronous content sooner:** it polls every 20–250 ms, while the library's `Expect` polls at 100, 250, 500, 1,000 ms. The journey's async list took 819 ms (A) against 265 ms (B) |
| 9 | Flakiness | **0 failures** in every run: A 60 + 30 parallel + 1; Browser surface 60; B NativeAOT 60 + 30 parallel; B JIT 20 |
| 10 | NativeAOT (B) | **0 warnings**: build with the trim and AOT analyzers (`IsAotCompatible`) and the ILC publish, no suppressions |
| 11 | One journey on both surfaces (A) | **Yes.** `OptionA/Journey.cs` ran unchanged on headless Chromium (library-launched, page over loopback), 60 of 60. Only the setup that hands it the page differs |
| 12 | B to a usable 1.0 | About **30–40 developer-days** on Windows for a usable API; **55–80** for breadth close to the library's page-level API; a WebKitGTK backend another **15–25**. Never matched: the trace viewer, codegen, the inspector, the breadth of the library's waiting rules |

**Recommendation: Option B**, an API with the library's names. Reasons and caveats are under [Recommendation](#recommendation).

## Feature coverage

"A" is the library attached to WebView2. "B prototype" is what `OptionB/Automation/` does today. "B 1.0" is the estimate below.

| Feature | A on WebView2 | B prototype | B 1.0 (estimate) |
|---|---|---|---|
| `GetByRole` (role + accessible name) | yes, its own role engine in the page | yes, through `Accessibility.queryAXTree` (Chromium's role and name computation): substring or exact names, no `checked`/`pressed`/`level`/`expanded` filters, no regex | full options; a role engine in page script for WebKitGTK |
| `GetByLabel`, `GetByText`, `GetByTestId` | yes | yes (script in the page): label text, `aria-label`, `aria-labelledby`; smallest element containing or equal to the text; `data-testid` | plus placeholder, alt text, title, regex, shadow DOM for text |
| Chaining, `Nth`, `First`, strict mode | yes, plus `Filter`, `And`, `Or` | chaining, `Nth`, `First`, strict mode with the matches listed | `Filter(HasText/Has/HasNot)`, `Last`, `And`, `Or` |
| Auto-waiting, actionability | yes (attached, visible, stable, enabled, editable, receives events) | the same checks in one page call: stable over one frame, hit test at the click point (shadow roots included), scroll into view | `force`, `trial`, position, nested scroll containers, the element detaching mid-action, a check that the event landed |
| `Expect` with retry | yes, 30+ matchers, call log on failure | visible, hidden, text (string, regex), contains text, value, count, enabled, checked; the last reason on failure | `Not`, attribute, class, CSS, focused, accessible name/role, in viewport, soft assertions, a call log |
| Click, hover, fill, press, check, select option | yes | yes (trusted `Input.*`; select option fires `input`/`change` as the library does) | double-click, modifiers, buttons, drag, wheel, uncheck, multiple selection, date/range inputs |
| Keyboard | yes (all keys, layouts, shortcuts) | about 12 named keys and single characters | the US key table, modifiers, `ControlOrMeta` shortcuts, `Type` |
| Screenshots (page, element) | yes | yes (`Page.captureScreenshot`, element clip) | full page, masks, animations off; visual comparison is a separate item |
| `AriaSnapshotAsync` | yes (YAML of the accessible tree, 3.0 ms) | no | a snapshot from `Accessibility.getFullAXTree`; template matching later |
| Evaluate | yes | by value | arguments, handles |
| Navigation wait | `WaitForURLAsync` | `WaitForUrlAsync` (glob) | load states, reload/back through the host |
| Script dialogs | `Dialog` event | accepted automatically (`Page.javascriptDialogOpening` on the host's session) | accept/dismiss/prompt text, a handler API |
| Console and page errors | events | collected (`Runtime.consoleAPICalled`, `Runtime.exceptionThrown`) | the same, with failure reports |
| Network events | yes (all 4 requests of a reload seen) | no | request/response waits from the `Network` domain |
| Route interception | **no**: 0 handler calls for `app.localhost`, fulfil or abort | no | not planned: tests replace the backend through the test host |
| File upload | `SetInputFilesAsync`, file chooser | `SetInputFilesAsync` (`DOM.setFileInputFiles`) | a file-chooser wait on the host's own `Page.fileChooserOpened`; directories |
| Tracing | yes (screencast, DOM snapshots, the trace viewer), but on the default context: one trace held frames under 4 page ids | no | an action log with screenshots on failure; no viewer |
| Video | **no** (only for new contexts; a 1,457-byte file without frames) | no | no |
| New pages or contexts | **no**: a new page opened a real browser window on the desktop; a new context's page could not load `app.localhost` | n/a | n/a |
| Frames | yes | main frame only | same-origin frames; cross-site frames through auto-attach |
| NativeAOT | **no** (JIT only) | **yes**, 0 warnings | yes |
| Linux (WebKitGTK) | **no** (no CDP in WebKitGTK) | no | a second backend: page script + XTEST |

## Costs

### Per process

| | A (JIT + driver) | B NativeAOT | B JIT |
|---|---|---|---|
| Environment + anchor controller | 201–277 ms | 197–247 ms | 228–233 ms |
| Port file appears | 5–9 ms after | — | — |
| Driver start (`Playwright.CreateAsync`) | 311–417 ms (1,606 ms in the first, cold run of the day; not kept) | — | — |
| `ConnectOverCDPAsync` | 29–36 ms; **4,460 ms** once (two processes starting together) | — | — |
| Process start → ready | 613–785 ms (5,220 ms with the slow connect) | 219–270 ms | 272–277 ms |
| Test process, private bytes | 18–27 MB | 12 MB | 17 MB |
| Extra process | `node` driver, 128 MB at start, 170 MB after one test | none | none |
| Browser (all processes of the folder) | 182–194 MB | 182–191 MB | 186 MB |
| On disk | the driver folder `.playwright/`, 103 MB per output folder | one 3,792,896 B executable | — |

### Per test (milliseconds, 60 tests; B JIT 20)

| | A p50 / p95 | B NativeAOT p50 / p95 | B JIT p50 / p95 | Browser surface (A's journey) p50 / p95 |
|---|---|---|---|---|
| New controller (or context + page) | 58.5 / 75.5 | 57.9 / 77.9 | 60.7 / 74.5 | 1.3 / 1.6 |
| Attach (B: event receivers, `Runtime.enable`) | — | 0.6 / 0.9 | 0.7 / 1.4 | — |
| Navigate to `/` | 31.2 / 41.4 | 29.7 / 37.5 | 31.7 / 35.0 | 24.8 / 27.6 |
| Find the page in the library | 0.0 / 0.0 | — | — | — |
| Journey (28 steps) | 1,694 / 1,742 | 1,451 / 1,545 | 1,528 / 1,543 | 1,698 / 1,728 |
| Close | 13.6 / 14.6 | 13.2 / 14.7 | 13.6 / 14.6 | 1.8 / 2.2 |
| **Per test** | **1,799 / 1,843** | **1,564 / 1,660** | **1,638 / 1,658** | **1,767 / 1,796** |

Two test processes at once (15 tests each, per test p50): A 1,796 and 1,814 ms; B 1,585 and 1,641 ms. Each had its own root and port (A: 63468 and 63469), each saw only its own browser, all 60 tests passed.

### Per step of the journey (p50, milliseconds, 60 runs)

| Step | A | B NativeAOT | Note |
|---|---|---|---|
| 01 two frames | 47.3 | 31.3 | |
| 03 fill (label) | 6.7 | 4.3 | |
| 04 check native checkbox | 34.8 | 13.8 | |
| 05 check Radix checkbox (role) | 35.6 | 71.4 | B: two role lookups (state read, then click) and the click |
| 07 open Radix select | 39.3 | 58.1 | |
| 10 open Radix menu | 35.4 | 57.5 | |
| 12 expect status text | 1.1 | 14.3 | B's role lookup waits for a frame |
| 13 wait for the list that loads after 600 ms | 819.4 | 264.8 | the library's `Expect` polls at 100, 250, 500, 1,000 ms |
| 16 press Enter | 3.5 | 18.0 | B: role lookup first |
| 21 cancel the animated dialog | 228.0 | 232.6 | both wait for the 150 ms entry animation |
| 27 wait for URL | 0.2 | 1.7 | |
| 29 screenshot | 64.0 | 50.1 | one sample each |

B's retries over 60 journeys: 360 "not stable" (6 per run; the dialog clicks during its 150 ms entry animation), 265 "count is not yet" (the async list and the filter).

### Per action (100 each, p50 / p95, milliseconds)

| Action | A | B NativeAOT |
|---|---|---|
| Resolve by role | 0.9 / 1.3 (`CountAsync`) | 6.1 / 7.3 idle; 17.6 / 18.0 right after a DOM change; 1.6 / 2.0 in the JIT run |
| Resolve by label | 0.7 / 1.2 | 0.5 / 0.9 (0.6 right after a DOM change) |
| Mouse move | 16.3 / 18.5 | 17.6 / 17.9 |
| Click (open the menu, then Escape) | 40.6 / 43.7 | 57.1 / 58.3 |
| Key press | 1.0 / 1.2 | 0.5 / 0.7 |
| Evaluate `1+1` | 0.6 / 0.9 | 0.2 / 0.3 |
| Assertion that already holds | 0.9 / 1.7 | 17.5 / 18.2 (lookup 13.5, the state read 0.3) |
| Viewport screenshot | 54.5 / 68.8 | 59.1 / 60.7 |
| Aria snapshot of the form | 3.0 / 3.9 | — |

A mouse move costs a frame in both (the browser acknowledges it at the next frame). B's role lookups cost 1.6 ms when the page has been idle and up to a frame (about 17 ms) after any change: `Accessibility.queryAXTree` appears to wait for the renderer's pending accessibility update. That is an inference from the timings; the cause was not isolated. The library computes roles in page script and pays nothing like it. A role engine in page script would remove this cost from B, and B needs one for WebKitGTK anyway.

## Identity and isolation

- **One context for everything (A).** `browser.Contexts` had 1 context holding all pages: `about:blank` (the anchor) and the three test pages. No `ProfileName` or InPrivate state is visible to the library. Context-level APIs therefore aim at the wrong browser context:
  - `CookiesAsync` returned 0 cookies while the InPrivate test page had one.
  - `StorageStateAsync` showed the page's `localStorage`, which it reads through the pages, but no cookies.
  - A trace started on the context recorded screencast frames under 4 different page ids.
- **Finding the test's page (A).** The host navigated to `/?test=<guid>`, and the test looked for that token in `page.Url`: 0.0 ms at p50 and p95 in 60 tests, because the library had attached before `NavigationCompleted` arrived. As a check, the target id the host got from the in-process `Target.getTargetInfo` was compared with the one the library's own CDP session reported: they matched in 3 of 3 (3–18 ms per session). B needs no lookup: its session is the page's own.
- **Isolation (`b-aot-isolation.log`; host-level, so the same for A).**
  - **InPrivate anchor: state leaks.** Test *n* found test *n−1*'s `localStorage` value and cookie in 4 of 4 tests, in each of two runs: the NativeAOT run in `b-aot-isolation.log`, and an earlier JIT run with the same results whose output was not kept. InPrivate data lives as long as any InPrivate controller of that profile name, and the anchor keeps it alive.
  - **Default-profile anchor:** tests 2–5 found nothing, in both runs (0 of 8). **No anchor:** 0 of 8. The 5.7 s stall of pitfall "The last controller's close ends the browser" did not occur in 8 no-anchor controller creations; the first took 308–355 ms and the later ones 90–131 ms.
  - **Simultaneous InPrivate controllers share storage:** with the same (empty) profile name, B saw A's value, with every anchor kind. With profile names `t1` and `t2`, nothing was shared.
  - **Consequence for the decision "InPrivate on one shared environment per test process":** it holds only while no other InPrivate controller of the same profile name outlives a test. Keep any anchor a default-profile controller, and give tests that run at the same time in one process distinct `ProfileName`s. The spike's harness uses a default-profile anchor from here on.

## Interference with the host's own DevTools calls and the picker block

- **The setup:** the host's in-process session called `Page.enable`, then `Page.setInterceptFileChooserDialog {enabled: true}`, before the first navigation. The library attached its own session to the same page and called its usual setup (`Page.enable`, its own interception setting, `Runtime.enable` and others).
- **A file-input click with no library listener:** the host's session got `Page.fileChooserOpened`; no native dialog appeared.
- **The library's file chooser** (`RunAndWaitForFileChooserAsync`, then `SetFilesAsync`): it worked, and the page got the file (`change`, "Attached 1 file(s)"). The host's session also got its event; no native dialog appeared.
- **After the library removed its listener**, which turns its own session's interception off, the next click was still intercepted by the host's session; no native dialog appeared. Interception is per session, and any session that has it on keeps the dialog closed.
- **The host's in-process calls while the library is attached:** `Runtime.evaluate` and `Page.captureScreenshot` worked.
- **Script dialogs:**
  - **With the library attached:** its `Dialog` handler accepted the `alert`; no native dialog appeared.
  - **With B:** the host's `Page.enable` means its session also gets `Page.javascriptDialogOpening`, and B answers it.
  - **With nobody answering**, with or without the host's `Page.enable`, the `alert` blocked the page (the click did not return within 3 s, and evaluate did not answer within 2 s), and no new top-level window appeared. So the host's `Page.enable` does not change how dialogs behave.
- **B uses the host's own session.** It must never call `Page.disable` or turn the interception off. Its uploads use `DOM.setFileInputFiles`, so no chooser opens. A future "wait for the file chooser" can reuse the `Page.fileChooserOpened` events the host already receives.

## Exposure of the port (A; `exposure.log`)

- **While a test held its page open:**
  - From a separate process, `curl http://127.0.0.1:<port>/json/list` listed the page (`https://app.localhost/settings`, title "Orders") and the anchor, with their WebSocket URLs.
  - A separate `node` process opened the page's WebSocket with no credentials. It ran `Runtime.evaluate` in the page (it read the title and path), took a screenshot, and on the browser's WebSocket listed every target.
  - Anything the page can do, including calling the app's backend through the bridge, is open to every local process for as long as the tests run.
- **After the test process exited:**
  - `DevToolsActivePort` was left in the profile every time: 6 of 6 processes that checked it, including the 60-test run.
  - The port no longer listened: `curl` failed at once, and `netstat` showed no listener.
  - Each run starts on a new root, so a stale file cannot be misread there. Another process could still take the old port number.
- **Two test processes at once:** separate ports and separate browsers; neither saw the other's pages. One of the two `ConnectOverCDPAsync` calls took 4,460 ms.
- **Left in `%TEMP%`:** the library left an empty `playwright-artifacts-*` folder per attached session, 11 from this spike, removed. When the coverage run was killed, its folder kept the trace's temporary files (9 files, 177 KB).

## Option B: the prototype

`OptionB/Automation/`: 928 lines (`Cdp.cs` 110, `Locator.cs` 378, `Scripts.cs` 139, `WindowPage.cs` 242, `Expect.cs` 59). It is written with the library's names, so the two journeys read alike (`OptionA/Journey.cs` against `OptionB/Journey.cs`). The whole prototype took about 1.5 hours of agent time, including three fixes found by running it:
- the heading name "Orders" also matched "Recent orders": exact matching, which the library needs too;
- exact `GetByText` stopped descending at the root;
- the stability check went from two frames to one: a click dropped from 75 to 57 ms.

The times below are the agent's authoring times, not a developer's.

| Primitive | Lines | Time | How complete, against the library |
|---|---|---|---|
| CDP plumbing: calls, `callFunctionOn`, evaluate, object groups, JSON by hand | 110 | 10 min | enough; no session per frame |
| Retry engine, timeouts, messages | 60 | 10 min | the last reason only (for example `<button …> "Place order": not enabled`); no call log |
| `WaitForUiReadyAsync` (two frames) | 2 | — | complete |
| `GetByRole` through `queryAXTree` | 45 | 10 min | role and name; no state filters or regex; up to a frame per lookup |
| `GetByLabel` / `GetByText` / `GetByTestId` | 45 | 15 min | the common cases; no shadow DOM for text and label |
| Chaining, `Nth`, `First`, strict mode | 45 | 5 min | no `Filter`, `Last`, `And`/`Or` |
| Actionability (one page call) | 30 | 20 min | the library's checks except `force`/`trial`/position and nested scroll containers |
| Click, hover, mouse | 60 | 10 min | left single click only |
| Fill | 25 | 5 min | text fields and contenteditable |
| Press, keyboard | 55 | 10 min | about 12 named keys and single characters |
| Check, select option, set input files | 70 | 10 min | check only; one option; files |
| `Expect` (9 matchers) | 59 | 10 min | no `Not`, attribute, class or focus |
| Screenshots (page, element) | 35 | 5 min | PNG; no full page or masks |
| Console, page errors, dialogs, URL wait, evaluate | 40 | 10 min | collected; dialogs always accepted |

Timeout messages from the coverage run:
- `expect role=status to have text "Shipped": timed out after 300 ms (6 attempts). Last attempt: text is "Draft"`
- `click role=button[name~="Place order"]: timed out after 300 ms (6 attempts). Last attempt: <button class="rounded bg-slate-900 px-4 py-2 text-sm f."> "Place order": not enabled`
- `Strict mode: role=button matches 6 elements: <button id="radix-_r_0_" …> "Actions"; <input class="mt-1 block text-sm">; …`

The library's messages include a call log of every attempt.

## Option B: the work to a usable 1.0

These are developer-days for someone who knows the codebase, Windows first.

| Item | Days | Notes |
|---|---|---|
| Locator breadth: role state filters, regex names, `Filter`/`And`/`Or`/`Last`, placeholder/alt/title, include-hidden | 4–6 | |
| A role and name engine in page script (instead of `queryAXTree`) | 5–8 | removes the per-frame lookup cost; **required for WebKitGTK**; must follow the accessible-name spec closely |
| Actionability: `force`, `trial`, position, nested scrolling, detaching mid-action, a check that the event landed on the target | 4–6 | **hard part**: edge cases are where flakiness lives |
| Input: US key table, modifiers and shortcuts, `Type`, double-click, buttons, wheel, drag and drop | 4–6 | drag and drop between elements through `Input.dispatchDragEvent` is the hard part |
| Fill and select for every control (date, range, color, number, multiple) | 2–3 | |
| Frames: same-origin frames, cross-site frames through `Target.setAutoAttach` (child sessions) | 3–5 | Spar's navigation lock keeps foreign frames out, so this can wait |
| Shadow DOM for text and label lookups | 1–2 | role lookups already pierce it |
| Dialogs API, file chooser wait on the host's events, directory upload | 2–3 | |
| `Expect` breadth: `Not`, attribute, class, CSS, focused, accessible name and role, in viewport, soft assertions | 3–5 | visual comparison is another 3–5 |
| Failure diagnostics: a call log per attempt, and on failure a screenshot, the accessibility tree and the console tail attached to the test result | 3–4 | **hard part** to get as good as the library's |
| Aria snapshot (from `getFullAXTree`) | 2 | template matching another 3–4 |
| Network waits (`Network` domain) | 1–2 | |
| Threading and lifecycle: the test thread against the UI thread, the test's cancellation, a page crash failing fast, object-group leaks | 2–3 | |
| Its own tests: a conformance page exercising every primitive, in both lanes | 4–6 | |
| Documentation and compiled examples in `docs/app-model.md` | 3–4 | |
| **Usable 1.0 on Windows** (the locators, actionability, input, fill/select, dialogs/files, core `Expect`, diagnostics, lifecycle, tests and docs) | **30–40** | |
| **Breadth close to the library's page API** (everything above) | **55–80** | |
| WebKitGTK backend: the same API over page script, XTEST input and WebKit snapshots | 15–25 | no CDP there; reuses the page-script role engine |

**Hardest parts:**
- waiting and actionability rules that stay flake-free across real component libraries;
- error messages that say what was found;
- an exact accessible-name computation in page script;
- keyboard tables;
- `Accessibility.queryAXTree`, which is an experimental DevTools method: if B keeps using it, Runtime updates can change it.

**What B would likely never match:**
- the trace viewer (DOM snapshots, time travel, network);
- codegen and the inspector (picking a locator);
- the VS Code integration;
- years of tuning of the library's waiting rules on many sites;
- its documentation and user base.

## One journey on both surfaces

`OptionA/Journey.cs` ran **unchanged** on the Browser surface's shape: headless Chromium 156.0.8078.4 launched by the library, a context per test, and the page from a small static server on `127.0.0.1` instead of Spar's loopback host. It passed 60 of 60 at 1,767 ms per test. What differed, all outside the journey:
- **How the test gets its page.** On WebView2, the host navigates and the test finds the page by its URL token. On the Browser surface, `NewContextAsync`, `NewPageAsync` and `GotoAsync`.
- **The viewport,** set to the WebView2 window's client size (1008 × 729).
- **The origin:** `https://app.localhost` through `WebResourceRequested`, against `http://127.0.0.1:<port>`.

The heading needed `Exact = true` on both, because "Orders" also matches "Recent orders".

With Option B, window journeys and browser journeys would use two APIs that read alike but are not the same code. B's `Cdp` layer is transport-neutral, so B could also drive a Chromium launched for the Browser surface over its DevTools WebSocket. That would give one journey for both levels again. This was not tried.

## Recommendation

**Option B**, built with the library's names and shapes, so a window journey reads like a browser journey.

1. **It stays in the NativeAOT lane.** Window tests run as the app runs, and the lane keeps no JIT-only exception. A needs a JIT test project, the 103 MB driver folder, a `node` process of 128–171 MB per test process and about 0.35 s of start-up, and once 4.5 s to connect.
2. **No port.**
   - **A's port is open to every local process** for the whole run, unauthenticated, and can drive the app's page and through it the backend.
   - It leaves a port file behind every time.
   - It needs `--remote-debugging-port` in the Test variant, the switch decision D8 rejected and production hardening has to keep out.
3. **The attach works, but it works around WebView2.**
   - The library sees all controllers as one context, so context-level APIs and tracing mix tests.
   - Route interception and video do not work.
   - `page.CloseAsync` hangs.
   - A careless `NewPageAsync` puts a real browser window on the developer's desktop, which the "offscreen, never activated" rule forbids.
4. **Linux.** The library cannot attach to WebKitGTK at all. B's API can get a second backend (page script plus XTEST), so one window-test API serves both platforms.
5. **The costs are close.**
   - A test cost 1,564 ms (B) against 1,799 ms (A) here.
   - The library is faster per action: clicks 41 against 57 ms, assertions 1 against 14–18 ms.
   - B is faster at waiting for asynchronous content.
   - A page-script role engine (needed for WebKitGTK anyway) should remove most of B's lookup cost.

**Caveats:**
- B is 30–40 developer-days to a usable 1.0, and its trace and diagnostics will stay well behind the library's.
- The Browser surface keeps the library, so app authors meet two APIs unless B also drives the Browser surface.
- An opt-in, JIT-only "attach the library for debugging" mode remains possible later for local trace recording. It would bring back the port only for that run.

## Problems met

- **A visible window on the desktop.** The library's `Contexts[0].NewPageAsync()` created a real browser window ("Chrome_WidgetWin_1", "Untitled", on screen at (10, 0)–(971, 1032)) for about 0.6 s. `NewContextAsync` + `NewPageAsync` also ran twice in earlier coverage runs. These probes are now behind `--new-targets`, which `run-all.sh` does not pass. No input was sent to that window.
- **`page.CloseAsync` on a WebView2 page never returned.** In one coverage run it returned only after 193 s, and the run then reached its 200 s limit. The first coverage run, stopped by hand, probably hung on the same call. The probe now gives up after 5 s.
- **The first coverage run was stopped by hand** (process 53524 and its children) after it hung. Its root and an earlier one were deleted once their lock files were gone.
- **Strict mode on the first B run:** "Orders" matched two headings. The library does the same with default options; both journeys now use exact matching for it.
- **`run-all.sh` was edited while it ran.** Bash reads scripts as it goes, so a garbled line overwrote `a-journey.log`. The A journey (60 runs) was rerun on its own, and the other logs were complete.
- **The NativeAOT publish replaces the apphost of the build output folder,** so the JIT run must come before the publish. The script now does that.
- **The trace archive held the test's source path** in `trace.stacks`. The copy in `results/` has it replaced with `<repo>`.
- **Redaction with `sed` in Git Bash** lost backslashes. `scripts/redact.mjs` does it instead.
- **A benchmark reused a released object group,** which failed with "Could not find object with given id". It now creates a new attempt; this is also a rule for B's API.

## Not tested

- **Windows and Runtimes:** Windows 10, other WebView2 Runtimes, an elevated test process.
- **Real OS input,** focus between windows, the clipboard (out of scope, Windows Sandbox).
- **Library features not tried on WebView2:** codegen or the inspector attached to WebView2; `page.Clock`; permissions and geolocation emulation; `page.Video` for an existing page; `WaitForLoadStateAsync` after a full navigation; frames and popups (`window.open` is handled by the host).
- **The trace viewer** was not opened; only the archive's contents were checked.
- **B not built or measured:** frames, drag and drop, a page-script role engine, B on the Browser surface over a WebSocket, B's WebKitGTK backend.
- **Why B's `queryAXTree` lookups sometimes take a frame** (the timings suggest it waits for the renderer's accessibility update).
- **The 5.7 s stall** (pitfall "The last controller's close ends the browser") was not reproduced with no anchor, 8 of 8. What triggers it remains open.
- **Isolation for IndexedDB, service workers and permissions** under the anchor variants (only `localStorage` and a cookie); `spikes/window-tests` covered those without an anchor.
- **The cold first-run driver start** (1,606 ms) was seen once in a smoke run that was not kept.

## Reproduce

From the repository root, in Git Bash:

```bash
cd spikes/window-automation/page && npm ci --ignore-scripts && npx vite build && cd -
bash spikes/window-automation/scripts/run-all.sh      # builds, publishes B, runs every measurement, redacts into results/
```

- **Verbs:**
  - `OptionA.exe journey|actions|coverage [--new-targets]|browser`. `--new-targets` opens browser windows on the desktop; do not use it unattended.
  - `OptionB.exe journey|actions|coverage|isolation --anchor private|default|none`.
  - `scripts/exposure.sh` holds a test page open and probes the port from other processes.
- **The page** is `page/` (React 19, Radix Dialog, DropdownMenu, Select and Checkbox, Tailwind 4), built to `page/obj/dist`.
- **The Browser surface's Chromium (revision 1248)** was already in the library's default cache, `%LOCALAPPDATA%\ms-playwright` (headless shell 278 MB, Chromium 437 MB). It was used read-only and nothing was downloaded. That Chromium keeps its profile in a temporary folder it deletes on close, as the repository's browser tests do.
- **Shared code:**
  - `Shared/Interop/WebView2.cs` is adapted from `spikes/window-tests` by `scripts/adapt-interop.mjs`.
  - `Shared/` holds the host side used by both options: the UI thread, the environment with its anchor, the InPrivate controller per test, the page server and the picker block.

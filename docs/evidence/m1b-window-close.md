# M1b: closing from window tests, the session end within 4 s, light tests and keys for any character

Recorded 2026-11-19. What it shows: the follow-up of step 8a decided by the user on 2026-11-18 (decision "Window tests: closing, session-end budget, test theme and any-character keys"): the page's close guard and `app.Window.CloseAsync`, the session end's 4 s budget, window tests light by default, and keys for any character, on the real WebView2 window; and that the test-only parts stay out of a Production executable.

## Environment

As [`m1b-window-tests.md`](m1b-window-tests.md): Windows 11 Pro 10.0.26320 (system theme dark), WebView2 Runtime 157.0.4341.60, SDK 11.0.100, xUnit v3. Every window offscreen and never activated, all page input through the window's in-process DevTools session; no dialog was shown on the desktop (every question of a test's close was answered by the test). User-data folders under `artifacts/webview2-profiles/test-host/<run>-<pid>/`.

- **CoreCLR:** `dotnet test --project tests/Spar.WindowTests` and `dotnet test --project samples/Notebook.EndToEndTests` (`--filter-trait "Surface=Window"` for the flakiness runs).
- **NativeAOT:** `dotnet publish <project> -c Release -p:SparTestLane=Aot -o artifacts/aot/<name>`, then the executable.
- **Evidence written by the tests:** `artifacts/test-results/Spar.WindowTests/` (`session-end.txt`, `close.txt`, `theme.txt`, `keys/`, `budgets.txt`).

## Results

### The session end (observed)

`WM_ENDSESSION` (`TRUE`, `ENDSESSION_LOGOFF`) sent to the window on its UI thread, timed from the test's side and by the host itself (`HostTests`):

| Case | Held |
|---|---|
| A page guard that takes 5 s ignoring its signal, a backend guard that takes 5 s ignoring its token, a call in flight, and a service whose disposal takes 5 s | 4,001–4,015 ms (21 runs, both lanes: 10 CoreCLR, 10 NativeAOT and the first run; [`session-end.txt`](m1b-window-close/session-end.txt)) |
| Nothing slow (page guard keeps, backend guard closes at once) | 15–95 ms (21 runs) |

In the slow case the page's guard was given 2 s (then `close-ended`), the backend's the rest of 3 s, the teardown ran to the 4 s mark, and the disposal was still running when the message returned (it finished later, checked); in the fast case the services were disposed before it returned. The page's guard ran with `sessionEnd`, and its keep kept nothing.

### Closing from a test (observed)

| Path | Time from the request |
|---|---|
| A silent guard (answers pings, never the request), "Close anyway" | 5,014–5,030 ms to the window gone, the backend's guard included (20 runs; [`close.txt`](m1b-window-close/close.txt)) |
| A page hung for 12 s, "Close anyway" | 5,015–5,031 ms (20 runs) |
| The same, "Wait": the page comes back, reads the request, its guard keeps | 11,842–11,861 ms (20 runs) |
| A page showing its own `aria-modal` dialog for 6.5 s | nothing asked; the request ended with the dialog's answer |

`CloseTests` (15) covers close, keep (the backend not asked after it), ask automated (counts as close, the question reported) and not (the test's answer), the backend's question after the page agreed, a page guard that throws, a second request joining, a navigated page's late answer, the dialog of the page's own, the silent and hung paths, `SessionEnd` through `CloseAsync`, and the refusal of `Automated = false` without `Answer`.

### Keys (observed)

What the page's own listeners received for `PressAsync` of one character the US layout has no key for (`KeyTests`, files in `artifacts/test-results/Spar.WindowTests/keys/`): for each of `è`, `€`, `ñ`, `😀` and `中`, `keydown`, `keypress` (`key` the character, `code` empty), `beforeinput` and `input` (`data` the character), `keyup` (`key` the character, `code` empty), and the field's value the character. `Shift+A` kept `code` `KeyA`. Typing `Grüße € ñ 😀 𝄞 中文 e` + U+0301 + `!` sent one `keydown` per scalar value and left exactly that text, with no U+FFFD anywhere; `FillAsync` of the same text inserted it whole with no key events; typing `ok 😀𝄞` into editable content kept the emoji whole.

DevTools' `Input.dispatchKeyEvent` with text of 2 and 3 UTF-16 units was accepted, of 4 and 8 (a family emoji) refused with `-32602 Invalid 'text' parameter`: the reason keys go by scalar value, not by grapheme.

### The theme (observed)

| Window | `prefers-color-scheme: dark` | Title bar dark (DWM) | Window colour | Screenshot pixel at the page's blank header |
|---|---|---|---|---|
| Default (`Light`) on this dark machine | false | false | `#ffffff` | (252, 252, 252) |
| `Dark` | true | true | `#1f1f1f` | (44, 44, 44) |
| `System` | true (the machine's) | true | `#1f1f1f` | not asserted |

Before the emulation was added, a `Light` window's page still matched `prefers-color-scheme: dark` although the InPrivate profile's `PreferredColorScheme` was set to light; `Emulation.setEmulatedMedia` made it light and kept it across a navigation (`The_scheme_lasts_across_a_navigation`). A light and a dark window open at once each kept their own scheme. A DevTools screenshot does not hold the window's own colour behind the transparent page, so the window's colours are read from the window.

The evidence screenshots of step 8a were replaced by light ones: [`window-page.png`](m1b-window-tests/window-page.png), [`window-conflict.png`](m1b-window-tests/window-conflict.png), [`window-host.png`](m1b-window-tests/window-host.png), and the new [`window-close-kept.png`](m1b-window-tests/window-close-kept.png) (the Notebook kept open after it asked about a draft it could not save).

### The budget (observed)

A window test of open, an 8-step journey and close, 20 per series ([`budgets-coreclr-full-run.txt`](m1b-window-close/budgets-coreclr-full-run.txt), [`budgets-aot-full-run.txt`](m1b-window-close/budgets-aot-full-run.txt)):

| Series | Whole test, median (range), p95 | Open | Journey | Close | Controller |
|---|---|---|---|---|---|
| CoreCLR, Debug, in the full suite | 305 (297–344), 330 | 132 | 159 | 13.9 | 23.4 |
| NativeAOT, in the full suite | 299 (281–346), 344 | 125 | 157 | 13.7 | 22.5 |

Against the 700 ms budget, unchanged from step 8a (306 and 298 ms): the light scheme's `Emulation.setEmulatedMedia` before the first navigation costs no measurable time. Per action (NativeAOT, 100 each, median): role lookup 0.25 ms, an assertion that holds 0.27 ms, click 33.4 ms, fill 0.64 ms, key 0.44 ms, screenshot 62 ms.

### Tests (observed)

| Suite | Result |
|---|---|
| `tests/Spar.WindowTests`, CoreCLR | 79 of 79 (53 window tests: 15 `CloseTests`, 9 `KeyTests`, 5 `ThemeTests`, 10 `HostTests`, and step 8a's; 26 that need no window) |
| `tests/Spar.WindowTests`, NativeAOT | 79 of 79 (19,331,584 B executable) |
| `samples/Notebook.EndToEndTests`, CoreCLR | 17 of 17 (10 window journeys, 3 new; 7 browser journeys) |
| `samples/Notebook.EndToEndTests`, NativeAOT (window journeys) | 10 of 10 (18,907,136 B) |
| `tests/Spar.Tests`, CoreCLR / NativeAOT | 510 of 512, 2 skipped (Linux only) / 506 of 508, 2 skipped |
| `tests/Spar.Generator.Tests` | 94 of 94 |
| `tests/Spar.Sdk.Tasks.Tests` | 65 of 65 |
| `tests/AppModel.Tests` | 11 of 11 (35 blocks; the new `ClosingWindowTests.cs` against stand-ins and real Spar) |
| `tests/WireShapes.Tests`, CoreCLR / NativeAOT | 79 of 79 / 79 of 79 |
| `tests/WireShapes.EndToEndTests` | 17 of 17 |
| `samples/Notebook.Tests`, CoreCLR / NativeAOT | 12 of 12 / 12 of 12 |
| `src/Spar.Client` Vitest | 252 of 252, 14 files (`closing.test.ts` new); `npm run check` clean on TypeScript 6 and 7 |
| `samples/Notebook/frontend` page tests | 11 of 11 (4 new); `tsc` clean |
| `tests/Spar.BuildTests` (the production boundary with the new markers) | 6 of 6 |
| `tests/Spar.PackageTests` (after the SDK's `npm run build`) | 11 of 11 |
| `dotnet run eng/check-size.cs` | 401 files, 0 errors, 0 warnings |
| `dotnet build Spar.slnx`, the NativeAOT publishes | 0 warnings, 0 errors |

### Flakiness (observed)

Ten runs of each on each lane, back to back ([`flakiness.log`](m1b-window-close/flakiness.log)): the window suite (`tests/Spar.WindowTests`, 79 tests, 53 of them window tests) 10 times on CoreCLR and 10 on NativeAOT, 1,580 tests; the Notebook's 10 window journeys 10 times on each lane, 200 journeys. **0 failures.** A window-suite run took about 51 s on NativeAOT, most of it the close tests that wait out the 5 s check and the 12 s hang. After the runs no `msedgewebview2` process had a command line under the repository, and `artifacts/webview2-profiles/test-host/` held no root.

**After review (2026-11-19):** a reviewer's 9 back-to-back runs of `dotnet test --project tests/Spar.WindowTests` (CoreCLR Debug) failed 5 times: `CloseTests.A_page_guard_that_throws_lets_the_person_decide` 4 times (the console message, delivered by DevTools events, arrived after the close answer, delivered by the bridge) and `PageApiTests.Actions_wait_for_late_covered_animated_and_scrolled_away_elements` once. The runs above were made with the same command, configuration and parallelism (`dotnet test --project tests/Spar.WindowTests --no-build` after a Debug build, then the NativeAOT executable, the Notebook's window journeys between them); both races depend on timing, and here the second reproduced once in 9 instrumented runs. A click probe of that failure showed the Confirm button's press landing on it (`seen`, `onTarget`) and no click on it: the dialog's transition, which starts two animation frames after it appears, moved the button between the press and the release, after one frame of an unchanged box had passed the stability check. Fixes: the test waits, bounded, for the console message; the engine waits while a running animation moves the element or an ancestor, stops a first click that misses an element still in the page and tries again, and registers its watcher's listeners when it is installed, before any page script (a page's own capturing window listener saw the stray click otherwise). A new window test, `A_click_whose_element_moves_between_press_and_release_is_stopped_and_tried_again`, makes the move deterministic.

Then, after the fixes ([`flakiness-after-review.log`](m1b-window-close/flakiness-after-review.log)): `dotnet test --project tests/Spar.WindowTests` exactly so (with its build) **30 times on CoreCLR, 0 failures** (80 tests each, 2,400); the NativeAOT executable **10 times, 0 failures** (800); the Notebook's window journeys 10 times on each lane, **0 failures** (200). No `msedgewebview2` process under the repository and no profile root were left.

### The Production boundary and size (observed)

The Production Notebook (`dotnet publish samples/Notebook -c Release`, NativeAOT) is **5,234,688 B**, against 5,200,896 B after step 8a (+33,792 B; budget 6 MB). The page's close guard is production code: `PageWatch.Closing`, `AppWindow.Closing`, `PageCloseAnswer`, `BridgeSession.ShowsDialog`, the bridge's new messages, and the Notebook's own page guard in its packed page; the three new record structs were made plain structs (−512 B). The ILC map attributes to the names involved, heartbeat and window included: `AppWindow` 28.0 KB, `WebViewBridge` 34.8 KB, `PageWatch` 22.5 KB, `BridgeSession` 21.9 KB, `CloseGuard` 5.1 KB, `PageCloseAnswer` 3.6 KB, `PageCloseResult` 3.2 KB, `CloseReport` 2.8 KB; a build of the commit before was not made, so the growth is not split further. None of the test-only names (`SparTestClose`, `SparCloseOptions`, `SparCloseResult`, `TestAnswers`, `NativeThemeAsync`, `DwmGetWindowAttribute`, `Emulation.setEmulatedMedia`, `app.Window.CloseAsync`) is in the image; `tests/Spar.BuildTests` checks the new markers (below).

## Not verified

- A real sign-out or shutdown on Windows 11: only `WM_QUERYENDSESSION` and `WM_ENDSESSION` sent to the window (step 8c's Sandbox runner).
- The native confirmation and "Wait / Close anyway" with a person at the desktop; a guard that calls `confirm()`; the later asks of the Wait schedule for a silent guard (40, 80, 160 s), which are code only here.
- A session end while a user's close dialog is open; secondary windows (M3).
- Why the profile's `PreferredColorScheme` does not reach an InPrivate page.
- Dead keys, IME, other layouts; Linux (M1c).

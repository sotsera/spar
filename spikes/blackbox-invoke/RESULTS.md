# Spike: why Invoke takes 2 s on the hidden desktop

After M1b step 8c. [`spikes/blackbox-desktop`](../blackbox-desktop/RESULTS.md) found that UI Automation's Invoke on a
page button, until the dialog it opens is found, took 2,040–2,082 ms on the black-box fixture's hidden desktop against
0.5–1.1 s on the input desktop, and left it unexplained. The questions: how long `IUIAutomationInvokePattern::Invoke`
itself blocks, when the page receives the click, whether the time is a fixed timeout or a wait that only the input
desktop ends, whether the page's rendering differs there, and whether another way of clicking avoids it. Part 2 answers
a separate question: which commits added the Production Notebook's two 1,024 B steps.

Run on 2026-11-19 on the developer's Windows 11 machine (10.0.26320, WebView2 Runtime 157, SDK 11.0.100), every app on
a desktop of the driver's own (`CreateDesktop`), never on the input desktop. Another agent ran window tests on the same
machine at the same time, so only timings within one run of this spike are compared.

## Parts

- `InvokeProbe/`: a Spar app, published Production NativeAOT (`dotnet publish -c Release`). Its page timestamps every
  `pointerdown`, `mousedown`, `pointerup`, `mouseup`, `focusin`, `click` and `keydown` it sees, the window's `focus` and
  `blur` and `visibilitychange` (`performance.timeOrigin + performance.now()`, Unix milliseconds, with the target, whether
  the event is trusted, the active element, `document.visibilityState` and `document.hasFocus()`), and once a second how
  many animation frames and 100 ms timer ticks ran. Each line goes through a command to the backend, which writes it to
  standard error with its own clock. Buttons: "Count" (counts) and "Open dialog" (a `<dialog>` with `showModal()`, the
  Notebook's shape, with a "Close dialog" button).
- `Driver/`: a CoreCLR console app that compiles the fixture's process, desktop and UI Automation sources by link and
  launches the probe with the fixture's own `AppProcess` (a job object, the data root under
  `artifacts/blackbox-invoke/`, deleted after each launch). It clicks through UI Automation from a thread placed on the
  hidden desktop (`SetThreadDesktop`), timing the call on the system's precise clock, the clock the page and the backend
  use, and samples the input desktop's foreground window every 10 ms. Verb `run <variants> <clicks per launch>
  <launches>`; `SPIKE_TRACE=1` prints every page event of each click; `SPIKE_WATCHER=same` gives the thread that watches
  for the dialog the variant's settings; verb `settings` checks which `IUIAutomation2` settings belong to one object.
  Variants:

  | Variant | Thread | `AutoSetFocus` | Action |
  |---|---|---|---|
  | `mta` | MTA, no message pump (the fixture's) | default (true) | `Invoke` |
  | `mta-nofocus` | MTA | false | `Invoke` |
  | `mta-timeouts` | MTA | default | `Invoke`, connection timeout 250 ms, transaction timeout 1 s |
  | `mta-setfocus` | MTA | default | `IUIAutomationElement::SetFocus` only |
  | `mta-legacy`, `mta-legacy-nofocus` | MTA | default, false | `LegacyIAccessible.DoDefaultAction` |
  | `sta-pump`, `sta-pump-nofocus` | STA pumping window messages between and during calls | default, false | `Invoke` |

- `ab.sh`: the A/B of the fixture change on the Notebook's black-box tests and the fixture's own (the first attempt ran
  `dotnet test` in place, where the concurrent agent's test run held `Spar.WindowTests`' copy of the fixture; discarded).
- `pe-sections.cs`: part 2's section table (`dotnet run pe-sections.cs -- <exe>...`).

Raw evidence: `results/`. `matrix.txt` and `matrix-timeouts.txt` are the variant matrix (10 clicks on "Count" per launch,
2 launches per variant, then the dialog; the timeouts variant in its own process because those settings belong to the
process); `trace-*.txt` have every page event per click.

## Verdict

**The 2 s is UI Automation setting the focus before it invokes.** A client object's `AutoSetFocus` is true by default:
"By default, most UI Automation methods that perform an action on an element ... set focus to the element before
performing the action" ([IUIAutomation2::AutoSetFocus](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation2-get_autosetfocus)).
Setting the focus brings the element's window to the foreground, which on a desktop that is not the input desktop never
happens, and UI Automation waits about 2 s before it invokes. With `AutoSetFocus` off, Invoke returns in about 15 ms and
the page gets the same events (`focusin` on the button, then the pointer, mouse and click events), and the black-box
fixture now turns it off on every client object it creates.

## Numbers

From the matrix (`results/matrix.txt`, `matrix-timeouts.txt`), per variant over its 2 launches, times from the call's start:

| Variant | Call returns | Page's `click` | Dialog seen by another thread |
|---|---:|---:|---:|
| `mta` | 2,033 ms (2,026–2,068) | with the return | 2,042 and 2,102 ms |
| `mta-timeouts` | 2,030–2,032 ms (2,020–2,049) | with the return | 2,072 and 2,082 ms |
| `mta-setfocus` | 2,026–2,032 ms (2,019–2,039) | (no click) | — |
| `sta-pump` | 2,027 ms (2,019–2,039) | with the return | 2,084 and 2,226 ms |
| `mta-nofocus` | 14–15 ms (13–18) | with the return | 60 and 100 ms |
| `sta-pump-nofocus` | 14–15 ms (12–20) | with the return | 60 and 89 ms |
| `mta-legacy` | 17 ms (13–32) | with the return | 62 and 70 ms |
| `mta-legacy-nofocus` | 14 ms (12–17) | with the return | 60 ms |

`mta-nofocus` over more launches: 100 clicks, median 15 ms (12–29); the dialog seen after 60 ms (57–64, 20 of 20) with
the watching thread configured the same (`trace-nofocus-20-watcher-same.txt`).

- **Where the 2 s goes (`SPIKE_TRACE=1`, `trace-mta-then-nofocus.txt`):** with `AutoSetFocus` on, within about 20 ms the
  page's window gets `focus` (the button `focusin`) and loses it again (`blur`); nothing for 2 s; then the window's
  `focus` again and, about 1 ms later, the button's `pointerdown` … `click`; the call returns with the click.
  `SetFocus` alone takes the same 2.0 s, Invoke without it 15 ms: Invoke itself does not wait, the focus does.
- **Not a configurable timeout:** a connection timeout of 250 ms and a transaction timeout of 1 s (defaults 2 s and
  20 s) left it at 2,020–2,049 ms. **Not the message pump:** an STA thread that pumps window messages took the same
  2.0 s.
- **The foreground there:** from a thread on the hidden desktop, `GetForegroundWindow` returned null before and for
  2.5 s after `SetForegroundWindow` on the app's window, which returned false (`results/foreground-probe.txt`). A wait
  for the window to become the foreground therefore never ends early there. That UI Automation waits for exactly that
  is inferred; its internals were not traced.
- **The page renders normally there:** 60 animation frames and 10 ticks of a 100 ms timer per second in every report,
  `document.visibilityState` `visible` in every report, and `document.hasFocus()` true except during the focus churn
  above. Chromium does not throttle the page on the hidden desktop.
- **The page's events are the same without the focus step:** with `AutoSetFocus` off, the first click on a button gives
  `focusin` on it, then `pointerdown`, `mousedown`, `pointerup`, `mouseup` and `click`, all trusted; only the window's
  extra `focus`/`blur` pair is gone. `LegacyIAccessible.DoDefaultAction` gives the same events in 12–32 ms whatever
  `AutoSetFocus` says: it does not set the focus.
- **Nothing reached the input desktop:** 0 foreground samples owned by an app process in every launch of every variant.
- **The documentation:** "Calls to this method should return immediately without blocking. However, this behavior
  depends on the implementation" (`IUIAutomationInvokePattern::Invoke`); a provider's Invoke "must return immediately
  without blocking" (`IInvokeProvider::Invoke`). Chromium's does: the delay is in the client, before the provider is
  called.

## Settings: per object and per process

`Driver settings` (`results/settings-scope.txt`): `AutoSetFocus` set to false on one `CUIAutomation8` object stayed true
on a second one and on a third created afterwards; the connection and transaction timeouts set on the first changed on
all three. So `AutoSetFocus` belongs to the object, the timeouts to the process.

**A default object in the same process undoes it now and then:** with the clicking object's `AutoSetFocus` off and the
thread watching for the dialog left at the default, the click that opened the dialog (while the other thread searched)
still showed the focus churn and took 2,027–2,038 ms in 2 of 10 (`trace-nofocus-10.txt`) and 2 of 20
(`trace-nofocus-20-watcher-default.txt`) launches; with both objects off, 0 of 20. The fixture has one client object
per desktop, and a new one when a stuck call's thread is replaced; each gets `AutoSetFocus` off when it is created.

## The fixture change, measured

`AutomationThread` now sets `IUIAutomation2::put_AutoSetFocus(FALSE)` on its object (`IUIAutomation2` declared in
`BlackBox/Interop/UiAutomation.cs`, slots 3–58 as placeholders and slot 59, checked by the slot-order test). The A/B ran
the same test outputs twice, private copies under `artifacts/blackbox-invoke/suites/` (a concurrent build could not swap
the fixture under them), differing only in `Spar.Testing.dll` built without and with the change, interleaved round by
round (`ab.sh`, `results/ab-3.csv`); CoreCLR, Debug:

| Suite (trait `Surface=BlackBox`) | Before (3 runs) | After (3 runs) | After, 10 more runs |
|---|---:|---:|---:|
| `Notebook.EndToEndTests`, 6 tests | 15.8–16.6 s | 9.4–14.6 s | 9.4–11.5 s (median 9.9) |
| `Spar.WindowTests`, 13 tests | 21.2–26.4 s | 13.4–13.6 s | 12.6–13.5 s (median 12.9) |

Per test (before → after, medians of 3): the Notebook's Invoke and dialog test 2,757 → 723 ms; its failed relaunch
(read and answered, exit 8) 7,757 → 5,803 ms; its profile-folder failure (exit 70) 2,708 → 699 ms; the fixture's window
test 5,702 → 1,554 ms; the hung first instance's relaunch 5,535 → 2,813 ms; the network `LOCALAPPDATA` refusal
2,220 → 163 ms. The dialogs' `PressAsync` is an Invoke too, so every test that answers a native message lost 2 s.
`results/after-10.csv`: 190 tests, 0 failures. NativeAOT: `Notebook.EndToEndTests` published with
`-p:SparTestLane=Aot` with no warning, its six black-box tests passed 4 of 4 runs in 8.9–9.6 s (the recorded NativeAOT
median before was 15.1 s).

## Found

- **A close request that never arrived, in the spike only:** in 4 of the 11 kept launches of the variants with
  `AutoSetFocus` on and `SPIKE_WATCHER=same` (`mta-legacy` 3 of 5, `sta-pump` 1 of 2, `mta` and `mta-timeouts` 0 of 2 each;
  two more runs of `mta-legacy`, 3 of 3 and 0 of 1, were overwritten), the window's `WindowPattern.Close`, sent right after
  a focus-setting Invoke had closed the page's dialog, did not end the app within 15 s: the app's log had no close
  request, its window was still there and showed no dialog, and the page kept reporting (`results/legacy-close-failure.txt`).
  Not seen with `AutoSetFocus` off (0 of 57 kept launches), nor in the fixture's 190 test runs after the change. Not explained.
- **An STA thread cannot be moved to another desktop once .NET has initialized it:** `SetThreadDesktop` failed with
  `ERROR_BUSY` (170) on a thread started with `ApartmentState.STA`; a thread left unset was MTA already
  (`CoInitializeEx` STA gave `RPC_E_CHANGED_MODE`). The STA variants call `CoUninitialize`, `SetThreadDesktop`, then
  `CoInitializeEx` with `COINIT_APARTMENTTHREADED`.

## Not measured

- The input desktop: not run (the fallback took the user's foreground in the earlier spike). Why Invoke took 0.5–1.1 s
  there, and whether `AutoSetFocus` is also what took the foreground in that spike's fallback, are open.
- UI Automation's own code: the wait for the foreground is inferred from the events, the 2 s and `GetForegroundWindow`.
- Windows 10, other Runtimes, Windows Sandbox's `Activated` placement (its real-input checks do not call Invoke).

## Part 2: the Production Notebook's 1,024 B steps

Each commit checked out in a `git worktree` of its own under `artifacts/` (this checkout's branch untouched; the worktrees
removed afterwards), the TypeScript SDK built there (`npm ci --ignore-scripts`, `npm run build` in `src/Spar.Client`),
then `dotnet publish samples/Notebook -c Release` (Production, NativeAOT, win-x64). Section tables:
`results/notebook-sections.txt` (`pe-sections.cs`).

| Commit | `Notebook.exe` | Bytes used (sum of section virtual sizes) | `.text` raw / used | `.rdata` raw / used | `.data` raw / used |
|---|---:|---:|---:|---:|---:|
| 6c6252e (step 8b) | 5,234,688 B | 5,326,873 | 2,512,384 / 2,512,216 | 2,201,600 / 2,201,186 | 256,512 / 351,064 |
| e4d8885 (folder fixes) | 5,235,712 B (+1,024) | 5,328,113 (+1,240) | 2,512,896 / 2,512,632 | 2,201,600 / 2,201,474 | 257,024 / 351,512 |
| febdd1c (Sandbox runner, host fixes) | 5,236,736 B (+1,024) | 5,328,949 (+836) | 2,513,408 / 2,513,048 | 2,202,112 / 2,201,650 | 257,024 / 351,704 |

- **Each step is its commit's code, rounded to the file alignment.** The image's file alignment is 512 B: each section
  takes whole 512 B blocks on disk. e4d8885 (`SparFolders.DataRootProblem`, `WindowsApp.CreateProfileFolder`; its only
  runtime changes) added 1,240 B of code and data: `.text` +416 B and `.data` +448 B each crossed a block boundary
  (+512 B each), while `.rdata` (+288), `.pdata` (+60) and `.reloc` (+28) fitted in their sections' last blocks.
  febdd1c (the session end's log close and the recovery stop) added 836 B: `.text` +416 B and `.rdata` +176 B each
  crossed a boundary, the rest fitted. So both steps are real growth of 836–1,240 B, and the file shows it as two blocks
  each time; a smaller change can add 0 B, and one of a few bytes can add 512 B.
- The sizes match the records exactly (5,234,688, 5,235,712 and 5,236,736 B), so the earlier inference that the first
  1,024 B came from the folder fixes holds: it is e4d8885.
- **The checkout path is in the image:** the CodeView record names the PDB's absolute path (125 characters in these
  worktrees, 99 in this checkout), in `.rdata`; the 26 characters did not change any size here, but a longer path could
  move `.rdata` over a block boundary on another machine.

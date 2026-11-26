# M2c: spar snap and scenes

Recorded 2026-11-26. What it shows: plan M2c, build steps 1 to 4 (section 6, `docs/design/agents.md`): scenes written as `[SparScene("name")]` methods in an app's end-to-end test project, each also a window test, and `spar snap`, which captures them into `obj/spar-snap/<scene>/` (`page.png`, `tree.txt`, `tree.json`, `calls.json`, `problems.txt`), the same PNG every run on one machine. Decisions "M2's decide-first questions" (item 6), "M2c's decide-first questions" and "M2c: spar snap and scenes (implementation record; choices for review; 2026-11-26)". The user-facing description is `docs/guide/spar-snap.md`; the app model's is `docs/app-model.md` 6.15.

## Environment

- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 158.0.4370.33, one 1920 × 1080 monitor at 100 %. Each scene ran in the test host's window: offscreen, never activated, its profile under `artifacts/webview2-profiles/test-host/`.
- **Linux:** WSL Ubuntu 24.04 (kernel 6.18.58.1), GTK 4.14.5, WebKitGTK 2.52.6, the repository's copy in `/var/tmp/spar-m1c-blackbox/repo` (`docs/evidence/m1c-blackbox/wsl.sh`, steps `sync client build test window snapsmoke`): each scene's test process on its own Xvfb (abstract socket only), its home and XDG folders under the copy's `artifacts/linux-homes/test-host/`. Nothing opened on WSLg's display.

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Testing/Scenes/` | `SparSceneAttribute`, `SparScene` (its clock, size and options), `SceneClock` (the backend's `TimeProvider` and the page's `Date` script), `SceneCapture` (still, idle, PNG, tree with sorted keys, calls, problems), `SparScenes` (the registry, `--spar-scene-list`, the generated tests' runner) |
| `src/Spar.Testing/SparTestApp.cs`, `Window/WindowSurface.cs`; `src/Spar/Testing/SparTestSession.cs`, `SparTestWindow.cs` | `options.Scene` and `options.Built`; the window request's page script, viewport and reduced motion |
| `src/Spar/Runtime/SparDispatcher.cs` | Calls recorded in test builds when a recorder is set |
| `src/Spar.Windows/Testing/TestWindow.cs`, `BrowserSource.cs`, `BrowserView.cs` | The viewport (`Emulation.setDeviceMetricsOverride`), reduced motion with the colour scheme, the page script, `--disable-gpu` for a scene's window |
| `src/Spar.Linux/Testing/TestPage.cs`, `TestWindow.cs`, `AppWindow.cs` | The page script as a user script; the view's size and the window fitted to it |
| `src/Spar.Generator/SceneAnalysis.cs`, `SceneAnalyzer.cs`, `Emit/SceneEmitter.cs`, `SparGenerator.cs`, `Descriptors.cs` | `Spar.Scenes.g.cs` (the module initializer and one window test per scene); SPAR110 to SPAR112 |
| `src/Spar.Tool/Snap/` | `spar snap`: the project's finder, the build, the list, the captures, `tree.txt`, `--json` |
| `samples/Notebook.EndToEndTests/Window/NotebookScenes.cs`; `samples/Notebook/frontend/src/time.ts` | The Notebook's three scenes; "now" under a minute |
| `tests/Spar.WindowTests/SceneTests.cs`, `tests/Spar.Generator.Tests/SceneTests.cs`, `tests/Spar.Tool.Tests/SnapCommandTests.cs` | 3, 3 and 10 tests |
| `tests/AppModel/SqlTool.EndToEndTests/Scenes.cs`, `Spar.Stubs/Testing.cs`, `RealTests/` | The app model's scene example, its stand-ins, and its build with real Spar |
| `docs/evidence/m2c-spar-snap/smoke.sh`, `smoke-windows.sh`; `wsl.sh` step `snapsmoke` | The evidence runs below |

## Results (observed)

### Tests

- **Windows**, `dotnet test --solution Spar.slnx -c Debug` (window tests included): 1,514 (1,401 passed, 113 skipped, 0 failed); before M2c 1,495. A first run beside the WSL window tests failed six window and black-box tests on their time limits (a renderer still running 5 s after its window closed, and the like) and the static-state test (four generator fields not yet reviewed); the run on an idle machine, with the fields reviewed, passed.
- **WSL**, `wsl.sh sync client build test window snapsmoke`: the solution without window or black-box tests 1,310 (1,269 passed, 41 skipped, 0 failed, after the same static-state fix); `Spar.WindowTests` 223 (173 passed, 50 skipped, 0 failed), the scene tests included.
- **NativeAOT (Windows):** the AOT lane of `Spar.Tests` 805 (7 skipped); `Spar.WindowTests` published with NativeAOT, `-class Spar.WindowTests.SceneTests` 2 of 2 (the scene registered by the generated module initializer under NativeAOT).
- **The boundary and the documents:** `Spar.BuildTests` 7 of 7; `AppModel.Tests` 11 of 11, its 38 blocks the same as the document's, the scene example built with real Spar.
- **Packages (Windows):** `Spar.PackageTests` 41 of 42: the template app's black-box journey (`WorkflowTests`, its Windows black-box step) did not see "Type a name first." within 10 s in three of four runs today, idle or not; it failed once and passed once in M2b's runs, and CI passed it on every M2b commit. Not caused by a scene (the template has none); not found.
- **The parts:** `Spar.Generator.Tests` 136 (`SceneTests` 3: registration in name order and the generated tests, no file without the test host, SPAR110 to SPAR112 with the scenes left out); `Spar.Tool.Tests` 123 (`SnapCommandTests` 10: the project found from the app, its folder and its file, none or two, wrong command lines, a folder without scenes, a result's text); `Spar.WindowTests`' `SceneTests` (two captures the same bytes at 1280 × 800 with no problems; the backend's and the page's clocks at the scene's instant, `Date` with arguments unchanged, `instanceof Date`) and the generated `Scene_window_host`, on both hosts.


### The smoke runs

The same sequence (`docs/evidence/m2c-spar-snap/smoke.sh`) from the Notebook's folder: the scenes listed; all captured with the build; all captured again into another folder without it; the two captures of each compared; one tree and its calls; an unknown scene; `--json`.

- **Windows** ([`logs/windows-smoke.txt`](m2c-spar-snap/logs/windows-smoke.txt), [`windows-groceries.png`](m2c-spar-snap/windows-groceries.png)): `--list` 5.8 to 10.8 s (with the build), `--all` 11.7 to 11.9 s, `--all --no-build` 6.8 to 6.9 s, one scene with `--no-build` 2.5 s; `page.png`, `tree.json` and `problems.txt` the same bytes in both captures of each scene, and `calls.json` the same without its times; `no-such-scene` exit 64 naming the three scenes.
- **Linux** ([`logs/linux-smoke.txt`](m2c-spar-snap/logs/linux-smoke.txt), [`linux-groceries.png`](m2c-spar-snap/linux-groceries.png)): the same, every file the same; `--list` 4.0 to 4.6 s, `--all` 10.6 to 11.0 s, `--no-build` 6.2 to 6.4 s.
- **Repeats:** five captures of `groceries` on Windows and eight on Linux, each the same bytes.
- **The groceries scene's tree** (from the smoke): the sidebar's page and its relative time, the page view's heading, the editor's Markdown as its value, the preview's checkboxes with their states; its calls: `pages.list`, `pages.create`, `pages.list`, `pages.save` (one per pause in the typing), `pages.list`.

### Found and fixed during the runs

- **A scene in the module initializer hung:** the module initializer now only registers and lists; a capture runs through the executable's xUnit runner (choice 1).
- **Single anti-aliased pixels differed by one level between runs on Windows:** a scene's window renders in software (choice 3).
- **The last click's hover style** showed in one capture and not the next: the pointer moves to a corner first.
- **WebKitGTK's key order** in the engine's tree varied: the tree is written with sorted keys.
- **"Groceries now" or "Groceries 1 second ago"** in the Notebook's sidebar, depending on the run's timing: the Notebook says "now" under a minute.
- **`problems.txt` began with a byte order mark;** the capture's files are UTF-8 without one.
- **The project finder missed `[Spar.Testing.SparScene(`;** it reads the qualified form and the `Attribute` suffix too.
- **`--no-build` restored packages** (8.6 s); it skips the restore (2.5 s).

## Not verified

- Captures across two machines, or between a 100 % and a 150 % monitor (the viewport's scale is overridden; not observed at 150 %).
- WebKitGTK with a GPU (WSL's Xvfb renders in software).
- The generated scene tests in the NativeAOT lane (they are not discovered there, choice 2).
- CI.

## Addendum (2026-11-26): the window-host scene's intermittent mismatch

`SceneTests.Two_captures_of_a_scene_are_the_same_bytes_at_its_size` differed in 7 of 40 Windows runs. Each time it was the same 2,352 pixels of the test app's "Loading…" layer, which its page removes after 400 ms; the capture's idle wait did not see the pending timeout. The capture now waits for the page's timeouts due within its limit (decision "A scene's capture waits for the page's pending timeouts"). After the change: 30 consecutive Windows runs of the test, then 30 of `SceneTests` (now 3 tests, with `A_capture_waits_for_the_pages_pending_timeouts` and its scene `pending-timeout`), and 10 of `SceneTests` in WSL, all passed. The Notebook's three scenes were the same bytes again, with 0 problems, and `--all --no-build` took 6.7 s; the smoke logs above were not re-run. The test now saves both PNGs and trees under `artifacts/test-results/Spar.WindowTests/scenes/<time>/` when they differ.

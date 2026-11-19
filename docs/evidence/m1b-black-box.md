# M1b: the black-box fixture on the Production executable

Recorded 2026-11-19. What it shows: M1b build step 8b (decisions "Step 8b's shape: the black-box fixture (user decisions)" and "The black-box fixture (implementation record; choices for review)"): the app's exact Production NativeAOT executable launched by `SparBlackBox.LaunchAsync`, on a desktop of the test process's own, read and driven through UI Automation; the build that publishes it beside the tests; and that none of it reaches a Production executable. The question of the desktop was answered first by [`spikes/blackbox-desktop`](../../spikes/blackbox-desktop/RESULTS.md).

## Environment

Windows 11 Pro 10.0.26320, WebView2 Runtime 157, SDK 11.0.100, xUnit v3, the Windows SDK 10.0.26100.0 (for the slot-order test's header). Every launched app on the hidden desktop `spar-black-box-<pid>`; nothing was shown on the user's desktop by the fixture's tests, no input was sent, and no window there was touched. Data roots under `artifacts/test-host/<run>/black-box-NNN/`, each with `LOCALAPPDATA`, `APPDATA`, `TEMP` and `TMP` inside it.

- **CoreCLR:** `dotnet test --project samples/Notebook.EndToEndTests --filter-trait "Surface=BlackBox"` and `dotnet test --project tests/Spar.WindowTests --filter-trait "Surface=BlackBox"`.
- **NativeAOT:** `dotnet publish <project> -c Release -p:SparTestLane=Aot -o artifacts/aot/<name>`, then the executable with `-trait "Surface=BlackBox"`.
- **Raw results:** [`m1b-black-box/`](m1b-black-box/) (the flakiness runs line by line, the publish logs of the NativeAOT lane).

## Results

### The suites (observed)

| Suite | Tests | CoreCLR, one run | NativeAOT, one run |
|---|---:|---:|---:|
| `Notebook.EndToEndTests`, `Surface=BlackBox` | 6 | 16–18 s (median 17), `dotnet test` included | 14.9–15.4 s (median 15.1), xUnit's time |
| `Spar.WindowTests`, `Surface=BlackBox` | 11 | 19–25 s (median 20), `dotnet test` included | 18.5–23.6 s (median 18.6), xUnit's time |

**Flakiness, after the last change:** each suite 20 times on CoreCLR ([`coreclr-loop.txt`](m1b-black-box/coreclr-loop.txt), 340 tests) and 20 times on NativeAOT ([`aot-loop.txt`](m1b-black-box/aot-loop.txt), 340 tests): **0 failures**. Both NativeAOT publishes had no warning. The six-test Notebook suite takes about 2.5 s per test; the fixture's own suite includes a 5 s wait for a root kept open on purpose and a 1 s exit deadline.

**Failures seen before:** one in an earlier series of 12 runs per suite on CoreCLR ([`coreclr-loop-before-fix.txt`](m1b-black-box/coreclr-loop-before-fix.txt), [`coreclr-failure-before-fix.txt`](m1b-black-box/coreclr-failure-before-fix.txt)): after a suspended first instance was ended, the data root could not be deleted, because the browser's `CrashpadMetrics-active.pma` was still open although the job reported no process left; the build tests were running beside it. The deletion gave up after 0.5 s; it now retries for 5 s, and both series were run again from the start. Earlier runs during development (a first CoreCLR series stopped after 8 runs per suite with 0 failures, for a change to the code) are not counted.

### Every suite the step touches (observed, on the final state)

- `dotnet test --solution Spar.slnx -c Debug --no-build --filter-not-trait "Surface=Window" --filter-not-trait "Surface=BlackBox"`: 832 tests, 830 passed, 2 skipped (Linux-only cases of `SecurityDefaultsTests`), 0 failed: Spar.Tests 517 (the ready-line pin and the UI Automation slot test included), Spar.Generator.Tests 96 (the `Projects` class included), Spar.Sdk.Tasks.Tests 65, AppModel.Tests 11 (36 blocks), Notebook.Tests 12, Notebook.EndToEndTests 7 (browser), WireShapes.Tests 79, WireShapes.EndToEndTests 17, Spar.WindowTests 26 (no window).
- `dotnet test --project tests/Spar.WindowTests`: 91 of 91; the Notebook's window journeys: 10 of 10.
- NativeAOT lane: Spar.WindowTests 91 of 91, Notebook.EndToEndTests 16 of 16 (window and black-box), Spar.Tests 514 (2 skipped), WireShapes.Tests 79, Notebook.Tests 12.
- `src/Spar.Client`: `npm run build`, then Vitest 252 of 252.
- `dotnet test --project tests/Spar.BuildTests`: 6 of 6 (1 min 55 s). `dotnet test --project tests/Spar.PackageTests`: 12 of 12 (1 min 36 s), the black-box consumer included; its first run failed that case because the consumer's page never calls `ready()` (implementation record, Found).
- `dotnet run eng/check-size.cs`: 429 source files, 0 errors, 0 warnings.

### The spike (observed; `spikes/blackbox-desktop`)

50 of 50 launches of the Production Notebook on a desktop of their own: `ui-ready` after 385 ms (median; 377–541), the window found after 391 ms, a page heading found by name after 497 ms, Invoke until its dialog was found 2,062 ms (2,040–2,082), close to exit 34 ms (31–38), every process gone 126 ms after the close request (97–133). No sample with an app process in the foreground and no visible app window on the input desktop; the desktop gone after close; the real `%LOCALAPPDATA%\com.example.Notebook` never created. The fallback on the user's desktop (`SW_SHOWNOACTIVATE`) took the foreground in 2 of 2 launches.

### The build (observed)

- **The publish:** a first build of `Notebook.EndToEndTests` published the Notebook into `obj/Development/Debug/net11.0/win-x64/spar-apps/Notebook/` and copied `Notebook.exe` to `bin/…/spar-apps/Notebook/`; the generated `Spar.Projects.g.cs` holds `Projects.Notebook` (`"Notebook"`, `"com.example.Notebook"`, `"Notebook.exe"`, the project folder).
- **Incremental** (on the final state): a second, unchanged build took 2.3 s in all, generated no native code, and left the executable's time unchanged (09:07:31.029 before and after); touching `samples/Notebook/Program.cs` published again (native code generated, 10.5 s for the whole build). The same checks during development gave 1.5 s and 6.0 s.
- **Design time:** `dotnet msbuild -t:Compile -p:DesignTimeBuild=true -p:BuildingInsideVisualStudio=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true` with the `spar-apps` folder deleted: no publish, the folder not created, and the tested app's `AdditionalFiles` item there for the generator (binlog).
- **From a publish folder:** the NativeAOT lane's `artifacts/aot/Notebook.EndToEndTests/` holds `spar-apps/Notebook/Notebook.exe`, and its black-box tests passed from there.
- **The same executable as `dotnet publish -c Release`:** 5,234,688 B both, after the solution's configuration map was removed from the publish (before: 5,221,376 B from a solution build, the app linked against Debug builds of Spar; implementation record, Found). A CLI publish with `-p:SparBlackBoxBuild=true` into the black-box folders gave 5,234,688 B too.

### The production boundary (observed)

The Production Notebook (`dotnet publish samples/Notebook -c Release`) is **5,234,688 B**, the size before the step. Its image has none of the new markers (`SparBlackBox`, `SparNativeDialog`, `SparProject`, `BlackBoxQuery`, `IUIAutomationElementArray`; checked with `grep -c -a`, 0 each), and `Spar.BuildTests` checks them with the earlier ones (`ProductionBoundary.cs`). `HostPhases.UiReady` is a constant, inlined where the host logs it.

### Pins and checks (observed)

- **The ready line:** `tests/Spar.Tests/ReadyLineTests.cs` writes the host's phases through `HostLog` to a log file and finds exactly one line the fixture takes as ready; five lines that look alike (another phase, another category, an indented line) are refused.
- **UI Automation's slots:** `tests/Spar.Tests/UiAutomationSlotTests.cs` read the six declared interfaces from `Spar.Testing.dll` and found every method in its slot of `UIAutomationClient.h` (10.0.26100.0). It caught 22 placeholders of property getters named without `Get` (`Slot024_CurrentAcceleratorKey` for `get_CurrentAcceleratorKey`) before they were renamed.
- **NativeAOT:** both test projects publish with NativeAOT with no warning, and the UI Automation calls, the job object, the desktop and the dialogs work there (the NativeAOT runs above).

## Not run

- Linux; Windows 10; other Runtimes; the hosted CI runner (the workflow's steps have not run).
- Real input, focus and the first click (Windows Sandbox, step 8c).
- Exit 9 (a launch by another user on the same data folder); a crash record at the next start.
- A UNC `LOCALAPPDATA` (it would write into the developer's own profile: implementation record, Found).

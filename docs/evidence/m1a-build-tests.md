# M1a: the first build tests

Recorded 2026-11-15. What it shows: a clean copy of the working tree builds the Notebook and its tests on the first pass, an unchanged build runs none of Spar's incremental steps, no build writes to an authored file, and the Production executable carries its page and none of the test host, the loopback host or the test libraries. Decision: "Build tests: a clean copy, the step matrix from binary logs, the production boundary".

## What was added

| Path | Contents |
|---|---|
| `tests/Spar.BuildTests/Workspace.cs` | The assembly fixture: the files `git ls-files --cached --others --exclude-standard` lists at the root and under `src/`, `samples/`, `tests/` and `eng/`, copied to `artifacts/build-tests/<run>/repo/` (so no `bin/`, `obj/` or `node_modules/`), hashed, then `npm ci` and `npm run build` in the copy's `src/Spar.Client`, as CI does first. It deletes earlier runs at the start, and its own copy and publications after a run whose tests all passed; it refuses to copy when git lists files inside a `bin/`, `obj/` or `node_modules/` folder. |
| `tests/Spar.BuildTests/Dotnet.cs` | One isolated build: `DOTNET_CLI_USE_MSBUILD_SERVER=false`, `-nr:false`, a fresh `MSBUILDNODEHANDSHAKESALT` and `SharedCompilationId` per run, a binary log per build, then a diagnostic replay of the log read for `Building target "Spar…"` / `Skipping target "Spar…"`. Each build's time goes to `logs/timings.txt`. |
| `tests/Spar.BuildTests/NotebookBuildTests.cs` | The three tests below. |
| `.github/workflows/ci.yml` | A step after the `-mt` build: `dotnet test --project tests/Spar.BuildTests`. The project is not in `Spar.slnx`. |

The tests:
1. **A clean copy builds** `samples/Notebook.Tests`: the client step, the frontend install, type-check, Vite build and packing all run; the app assembly carries `Spar.Frontend` and `Spar.ThirdPartyNotices`; `obj/spar/client/package.json` exists; no authored file changed or appeared.
2. **An unchanged build** of the same project runs none of those five steps, and its log names each of them, so the check is not empty. Then a first and an unchanged Debug build of `samples/Notebook` alone, the budget's case; its time is recorded, not asserted.
3. **The Production publish** (`dotnet publish samples/Notebook -c Release`, NativeAOT) carries `SPARFE` and none of these: as UTF-8, `SparTestSession`, `SparTestApp`, `ISparEntryPoint`, `LoopbackHost`, `LoopbackChannel`, `LoopbackAssets`, `Spar.Loopback`, `Spar.Testing`, `Microsoft.AspNetCore.Server.Kestrel`; as UTF-16, `/_spar/channel`, `/_spar/send`, `Spar.TestHost.IsSupported`. A control runs the same search on `Spar.Loopback.dll` and the test build's `Spar.dll`, and finds the markers there.

Every test waits for the copy's first build, whichever xUnit runs first: in the first run the publish went first, installed the frontend and wrote the client, and the "first" build then skipped them.

## Results (observed, Windows 11, the two runs after the ordering fix)

| Check | Result |
|---|---|
| `dotnet test --project tests/Spar.BuildTests` | 3 of 3, about 69 s with the copy and both npm installs |
| First build of `Notebook.Tests` from the copy | 16.8–16.9 s |
| Unchanged build of `Notebook.Tests` (isolated, binary log) | 1.76–1.93 s, no Spar step run |
| Production publish of the Notebook | 10.4–10.5 s; `Notebook.exe` 2,918,400 B |
| Unchanged Debug build of `samples/Notebook`, isolated, with the binary log | 1.63–1.64 s |
| The same, isolated, without the binary log (fresh MSBuild processes each time) | 1.47–1.52 s |
| The same with node reuse and the MSBuild server (the developer's case) | 0.57–0.67 s after the first, which started them (1.63 s) |
| Authored files after all builds | unchanged, none added |

The 1.5 s no-op budget (plan, "Performance budgets") holds in the developer's case. The isolated case pays for starting MSBuild each time and sits at the budget; that is why the test records the time and does not assert it.

## Follow-up: one run on disk, a smaller copy, the guard (2026-11-15)

Four runs had left 2.4 GB (567-625 MB each: about 52 MB copied, 251 MB of `node_modules`, 143 MB of `bin/`, 18 MB of `obj/`, and 20-40 MB of text replay per build). Now, observed: a passing run starts by deleting the earlier runs and ends with only its binary logs and `timings.txt` (4.3 MB); a run with a deliberate failure kept its copy and publications (423 MB) until the next run; with the `node_modules/` line taken out of `.gitignore`, the run failed before copying, naming 50,694 files and five examples. A run cannot delete a folder another process has open (a shell whose working directory is inside it); it leaves it for the next run. Two runs at once in one checkout would delete each other's folders.

## Not done or not verified

- **Not built:** the `-mt` strict lane inside the build tests (CI runs `-mt` as its own step), variant builds and the adding, renaming and removing of a contract, the package-only consumer (`Spar.PackageTests`), and the production boundary on a console harness (plan M1a item 2; this one is the Notebook's own executable).
- **Not run:** Linux, CI.
- The copy holds the working tree's uncommitted files, so it is a clean clone of what is on disk rather than of a commit; line endings are the working tree's.
- While timing, one `dotnet build-server shutdown` stopped every build server on the machine, not only the run's own; nothing depended on them. The timing builds with node reuse left their own MSBuild nodes, which end themselves when idle.

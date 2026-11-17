# M1a: the Linux runs

Recorded 2026-11-16. What it shows: everything M1a builds and tests on Windows also builds and passes on Linux, run in WSL (Ubuntu 24.04) the way CI's Linux job runs it. That covers the size check, the TypeScript SDK, the solution's build and tests with the browser tests in Chromium, the NativeAOT lane, the `-mt` build, the build tests with the console harness, the package tests, and the budgets. Decision: "Linux runs in WSL for M1a; what Linux found".

## How it ran

`eng/wsl/setup.sh` and `eng/wsl/step.sh`, from Git Bash on Windows:

```
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/e/<repo>/eng/wsl/setup.sh /mnt/e/<repo> <run>
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/e/<repo>/eng/wsl/step.sh /mnt/e/<repo> <run> <step>
```

- **A fresh clone of HEAD,** in `/var/tmp/spar-linux/<run>/repo`, checked out with LF endings as CI checks out; the Windows working tree is never built from Linux. `/tmp` is not used: WSL empties it when the distribution restarts, which it does between `wsl.exe` calls.
- **The .NET SDK `global.json` asks for** (11.0.100, from `dotnet-install.sh`), the NuGet, npm and Playwright caches, and NSS and NSPR for Chromium (`apt-get download`, unpacked, no root), all in `/var/tmp/spar-linux/cache`.
- **HOME and every XDG folder** under the clone's own `artifacts/linux-homes/<run>/`. Nothing goes to the user's WSL home and nothing needs root.
- **CI's environment:** `ContinuousIntegrationBuild=true`, so the committed contract snapshots are verified, never rewritten.
- **Each step's output** is also written to the Windows repository's `artifacts/linux-runs/<run>/<step>.log`.

Machine: Windows 11 host, WSL 2 kernel 6.18.58.1, Ubuntu 24.04, 32 CPUs, Node 24.22.3, .NET SDK 11.0.100, Playwright's `chromium_headless_shell-1248`.

## Results (observed, run `m1a-20261116-150352`, on commit 09a1d49 plus the fixes below)

| Step | Result |
|---|---|
| `dotnet run eng/check-size.cs` | 209 files, clean |
| TypeScript SDK: `npm ci`, `npm run check` (TypeScript 6 and 7), `npm test`, `npm run build` | 141 of 141 |
| `dotnet build Spar.slnx -c Debug` (CI mode) | succeeded, no warnings; 2 min 25 s with an empty NuGet cache |
| `dotnet test --solution Spar.slnx` | 363 of 363: Spar.Tests 153, Generator 75, client step 56, WireShapes 34, WireShapes browser 16, Notebook 12, Notebook browser 7, AppModel 10 |
| NativeAOT lane, published and run | Spar.Tests 151, WireShapes.Tests 34, Notebook.Tests 12; no warnings |
| `dotnet build Spar.slnx -c Debug -mt` | clean |
| `Spar.BuildTests` | 6 of 6: the clean copy, the unchanged build, the Notebook's production boundary, the library compiled once, and the console harness, whose NativeAOT output equals its CoreCLR output and carries no test or loopback code |
| `Spar.PackageTests` | 9 of 9 (3 min 42 s, empty caches): both consumers, the browser test, the analyzer, design time, NativeAOT, the update and the downgrade |
| On Windows after the fixes | `dotnet test --solution Spar.slnx` 363 of 363; `Spar.PackageTests` 9 of 9 (47 s, the new package folder); size check clean |

### Budgets on Linux (Release, two runs)

| Measure | Budget | Run 1 | Run 2 |
|---|---|---|---|
| Command test, app start, CoreCLR | ≤ 2 ms | 0.29 ms (0.26–0.50) | 0.28 ms (0.21–0.54) |
| Command test, app start, NativeAOT | ≤ 2 ms | 0.11 ms (0.10–0.14) | 0.12 ms (0.11–0.23) |
| Browser test, app + context + journey | ≤ 250 ms | 59.8 ms (53.0–78.8) | 63.3 ms (62.2–81.8) |
| Small unary round trip over loopback | ≤ 2 ms | 0.70 ms (0.50–4.10) | 0.90 ms (0.50–1.90) |

The console harness's size is checked by its build test on Linux too (budget 3.5 MB); the size itself was not recorded separately.

## Found on Linux, and fixed

| Finding | Fix |
|---|---|
| **The static-state test failed:** the regex generator's file-local types are named after a hash of the source file's path (`<RegexGenerator_g>F298E…__Utilities`). `ContinuousIntegrationBuild` maps paths to `/_/`, so the names differ between CI and local builds, on any OS. | The test writes the hash as `{file}` (`<RegexGenerator_g>F{file}__Utilities`), and the reviewed list with it. |
| **Every browser test failed on one console warning:** Linux's headless Chromium has no Web Bluetooth and warns "Error with Permissions-Policy header: Unrecognized feature: 'bluetooth'." about Spar's deny list. | The header is unchanged (user decision); the two browser-test helpers accept exactly this message. |
| **The package consumer's NativeAOT publish failed to link:** `clang` got `/tmp/spar`, `package` and `tests/nuget-packages/…/libSystem.Native.a` as separate arguments. The NativeAOT link step on Linux splits paths into the NuGet package folder at spaces; the consumer's own folder, with a space and non-ASCII letters, linked fine. | The package tests' base folder is `spar-package-tests`; the consumer keeps `Nötes Äpp`. |
| **The SPAR121 check expected backslashes:** MSBuild writes item paths with `/` on Linux. | The test builds the expected item with `Path.Combine`. |

## Mistakes on the way, not in Spar

- The first attempt kept the clone in `/tmp`, which WSL had emptied by the next call. The step's `cd` into it failed, and `npm ci` ran in the Windows repository's `src/Spar.Client` and `tests/AppModel`, replacing their `node_modules` with Linux binaries. Restored on Windows with `npm ci --ignore-scripts` and `npm run build`; the SDK's tests passed there again (141 of 141). The scripts now stop when the clone is missing.
- The first homes were on the Windows drive, where `XDG_RUNTIME_DIR` cannot be 0700, and the .NET CLI's first run failed with "does not have the expected owner or permissions". They moved into the clone.

## Not covered

- **CI itself:** nothing has been pushed, so GitHub's Linux runner has not run the workflow. The WSL run follows its steps, except that Playwright's browser libraries come from `apt-get download` instead of `--with-deps`.
- **Visual Studio and VS Code squiggles** (a check that needs the user).
- **A desktop window:** M1a has none; the Linux host is M1c.

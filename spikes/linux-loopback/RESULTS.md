# Linux loopback spike: results

Recorded 2026-11-13. Question: can a Spar app in loopback mode be built and tested on Linux, driven by a headless browser? **Yes.** It took three small changes, and the generator, tasks and contract fingerprint behaved as on Windows.

## Environment

| Item | Value |
|---|---|
| Host | Windows 11, Docker Desktop 29.9.3 (Linux containers on WSL2, kernel 6.18.33.2), 32 CPUs and 46 GB visible to the container |
| Base image | `mcr.microsoft.com/playwright/dotnet:v1.64.1-noble` @ `sha256:6955861f6e17b2580c00f0970198e446f006cdae6c34fb8f9ad5318aaab50da0` (Ubuntu 24.04.5) |
| SDK | 11.0.100 (commit `297acf93`), from `dotnet-install.sh --version 11.0.100` into `/opt/dotnet11` (see `Dockerfile`). Runtimes: NETCore and AspNetCore 11.0.0 |
| Browser | `/ms-playwright/chromium_headless_shell-1248`, "Chrome for Testing 156.0.8078.4" |

**Browser compatibility:** Playwright for .NET 1.64.1's `browsers.json` expects revision 1248 (156.0.8078.4) for both `chromium` and `chromium-headless-shell`. The image ships exactly those, and `PLAYWRIGHT_BROWSERS_PATH=/ms-playwright`, so nothing was downloaded. The driver's `.playwright/node/linux-x64/node` kept its executable bit after the NuGet restore.

## Method

1. Copied the root config files (`.editorconfig`, `.gitattributes`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`) and `spikes/d1-pipeline` and `spikes/loopback` into a scratch folder, without `bin/`, `obj/`, `node_modules/` or `.vite/`.
2. Mounted the copy read-only at `/src` and copied it into a named volume at `/work`, so no Windows `obj/` was shared. NuGet used its own volume.
3. The copy is a snapshot taken about 08:30 local time. The originals have changed since.

```sh
docker build -t spar-linux-loopback:sdk11.0.100 spikes/linux-loopback
docker run -d --name spar-linux-loopback -v spar-linux-work:/work -v spar-linux-nuget:/root/.nuget/packages -v <copy>:/src:ro spar-linux-loopback:sdk11.0.100 sleep infinity
docker exec spar-linux-loopback cp -a /src/. /work/
# in /work/spikes/loopback:
dotnet build Loop.Tests/Loop.Tests.csproj -c Debug
bash smoke.sh ./Loop.App                     # in Loop.App/bin/Debug/net11.0/linux-x64
dotnet test --project Loop.Tests/Loop.Tests.csproj --no-build
bash killparent.sh <app> [off] [nostdin]
```

## Baseline: unchanged sources

- **Build:** `dotnet build Loop.Tests` **succeeded** with 0 warnings. It still targets `win-x64`, without `EnableWindowsTargeting`.
- **Restore:** it pulled the win-x64 host, runtime and NativeAOT packs, plus both the win-x64 and linux-x64 ILCompiler packages.
- **Tasks and generator:** the `TaskHostFactory`/`Runtime="NET"` tasks and the source generator ran: `spike-task client: 1 contract(s) … 431 ms`.
- **Running it without app changes:** `dotnet Loop.App.dll --loopback` from the `win-x64` output **ran on Linux**, and every smoke check passed.
- **Tests:** all 4 **failed** with "The app exited before it was ready". The tests launch `win-x64/Loop.App.exe`, which is a PE file that Linux cannot run.

## Changes made (all on the copy; see `linux-changes.diff`)

1. `Loop.App.csproj`: `RuntimeIdentifier` is now `$(NETCoreSdkRuntimeIdentifier)` when unset, instead of a hard-coded `win-x64`. That's `win-x64` on the dev box and `linux-x64` in the container. `PublishAot` is unchanged.
2. `Loop.Tests.csproj`: `LoopAppPath` uses `$(NETCoreSdkRuntimeIdentifier)`, and `.exe` only when `IsOSPlatform('Windows')`. The tests still launch the native apphost.
3. `BrowserJourneyTests.cs`: the `msedge-headless` case calls `Assert.Skip` when not on Windows.

The changed copy also passed on Windows: build 9.6 s, 4 of 4 tests passed, Edge included.

`EnableWindowsTargeting` is **not needed**: no Windows TFM is involved.

## Results (observed)

### Smoke test (`smoke.sh`, Linux apphost, an ELF file with mode 0755)

| Request | Result |
|---|---|
| Ready line | 81 ms wall from process start (`at=56ms`) |
| `GET /` | 200, 411 B |
| Valid save | 200 `{"text":"hi","tags":[],"version":1}` |
| No token | 401 `unauthorized` |
| Wrong `X-Spar-Contracts` | 409 `contract-mismatch`, detail `519edf0649949d82` |
| Missing `text` | 400 `invalid-request`, "Note requires 'text'." |
| Foreign `Origin` | 403 `forbidden-origin` |
| Stdin closed | exit code 0 after 5 ms |

**Fingerprint:** `519edf0649949d82`, the same as the Windows build's `contracts.json`.

### Browser tests (5 consecutive `--no-build` runs)

Every run: 3 passed, 1 skipped (Edge), 0 failed. Reported suite duration: 1.36–1.69 s; `dotnet test` wall time 1.66–2.02 s. With the no-op build included, wall time was 2.15–2.22 s.

Chromium headless shell 156.0.8078.4, from `artifacts/loopback/browser-timings.txt`:

| App ready | Browser launch | Page to `ui-ready` | Journey | Whole test |
|---:|---:|---:|---:|---:|
| 58–64 ms | 22–25 ms | 66–72 ms | 85–102 ms | **434–466 ms** |

### Build timings (`dotnet build Loop.Tests -c Debug`)

| Scenario | Time |
|---|---:|
| First build, cold NuGet. Slow network: one Playwright download hit an EOF and was retried | 8 min 24 s |
| First `linux-x64` restore after that (fetched the linux runtime and NativeAOT packs) | 56 s |
| Clean (`bin`/`obj` removed), warm NuGet, build servers running, two runs | 1.31 s, 1.32 s; 1.26 s in a third run |
| Clean after `dotnet build-server shutdown` | 4.59 s |
| Warm no-op rebuild, three runs (no task ran) | 501–535 ms |
| After appending a comment to `NotesService.cs` (client task ran, about 431 ms) | 1.12–1.32 s |

### Other checks

- **`KillOnParentExit` on Linux works.** The launcher (`killparent.cs`) was killed with SIGKILL:

  | Launch | Result |
  |---|---|
  | `KillOnParentExit` on, stdin watch off | app exited within 2 ms |
  | `KillOnParentExit` off, stdin watch on | app exited within 14 ms (stdin EOF) |
  | Both off | app still running after 1.17 s |

  In a plain container the exited app stays a zombie, because PID 1 (`sleep infinity`) doesn't reap it. `kill -0` reports that zombie as alive, so check `/proc/<pid>/stat`, or run with `docker run --init`.
- **Release build:** `Loop.App --loopback` took the desktop path.
- **Release NativeAOT publish:**
  - It **failed** in the stock image: "Platform linker ('clang' or 'gcc') not found".
  - After `apt-get install clang zlib1g-dev` (93 s, container only), it succeeded in 1.95 s with Loop.App's Release `obj`/`bin` removed. It produced a 1,593,616-byte ELF.
  - It contains no `LoopbackHost`, `Kestrel`, `AspNetCore`, `SPAR_READY` or `_spar/call` strings. Like the Windows check, this is a string scan.
- **Line endings:** sources are LF (`.gitattributes` `eol=lf`, 0 CRs in the copy); generated client files are LF.
- **Executable bits:** files copied from the Windows bind mount came out 0777. That's harmless.
- **Path casing:** the build hit no case-sensitivity errors.

## Not working, or not tried

- Edge on Linux: skipped by design.
- Linux NativeAOT needs `clang` and `zlib1g-dev`. The Playwright image lacks them, so CI would need them added to the image.
- The tests compute the app path at build time from the SDK RID. A cross-RID build, for example `-r linux-arm64`, would point at the wrong folder. Not tried.
- Not tried: arm64; GitHub-hosted Linux runners; a non-root user; `dotnet watch` on Linux; the Vite experiment; Firefox and WebKit; comparing generated files other than `contracts.json` with Windows (the Windows `obj/` predates later generator edits).

## Docker leftovers

| Kept | Why | Remove with |
|---|---|---|
| Image `spar-linux-loopback:sdk11.0.100` (5.84 GB, includes the 5.06 GB base) | Reruns skip the 2 min SDK install | `docker rmi spar-linux-loopback:sdk11.0.100 mcr.microsoft.com/playwright/dotnet:v1.64.1-noble` |
| Volume `spar-linux-nuget` | Reruns skip the 8 min restore | `docker volume rm spar-linux-nuget` |

The container and the `spar-linux-work` volume were removed.

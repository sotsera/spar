# M1d step 6b: the spar tool and the minimal build identity (evidence)

Recorded 2026-11-22. Plan M1d, build step 6, second half (decisions "M1d's decide-first questions", "M1d step 6's
shape: the template package and the spar tool" and "M1d step 6b's shape: the spar tool"), and the implementation record
"The spar tool (implementation record; choices for review; 2026-11-22)". The guide's new section is "8. Check and wire
with the spar tool" in [`docs/guide/getting-started.md`](../guide/getting-started.md).

## Setup

- Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24.21.0 (installed by nvm-windows and on `PATH`), npm 11.19.0, WebView2
  Runtime 157.0.4341.60, Visual Studio 18 Community with the C++ build tools, `LongPathsEnabled` off.
- Linux: WSL2 Ubuntu-24.04 in the copy `docs/evidence/m1c-blackbox/wsl.sh sync` keeps in `/var/tmp/spar-m1c-blackbox/repo`
  (written `<copy>`), with that script's caches, `HOME` and XDG folders, the core limit 1 byte.
- **Per-user folders:** every `dotnet tool restore` and `dotnet spar` of the package tests and the guide runs had
  `DOTNET_CLI_HOME` in the run's own folder (the restore writes the local tools' resolver cache and the CLI's first-use
  sentinels there: `toolResolverCache/1`, `11.0.100.dotnetFirstUseSentinel`, `sdk-advertising/…`, listed by the guide
  run); NuGet's and npm's caches are the package tests' (`%TEMP%\spar-package-tests`); `LOCALAPPDATA` for the Windows guide
  runs under `artifacts/webview2-profiles/m1d6b-guide/`. No tool was installed globally.
- The scripts: [`m1d-spar-tool/guide-win.sh`](m1d-spar-tool/guide-win.sh) (Windows, `%TEMP%\spar-guide-m1d6b`) and
  [`m1d-spar-tool/guide-linux.sh`](m1d-spar-tool/guide-linux.sh) (WSL, `/var/tmp/spar-guide-m1d6b`), each `pack new
  restore doctor init scaffold publish identity run`. Paths in copied logs are written `<repo>`, `<copy>`, `%TEMP%` and
  `%USERPROFILE%`.

## The build identity

The generator writes `Spar.BuildIdentity.g.cs` in every app and variant: `SparBuildIdentity.Block`, a UTF-8 literal, and
the catalog hands it to the host (`BuildIdentity = SparBuildIdentity.Block.ToArray()`). In the files:

| File | Block found |
|---|---|
| The Notebook's Debug `Notebook.dll` (this repository) | `[spar-build-identity/1 variant=Development revision=b59a518974b858fbd2c08edc4697000ffab8f996 dirty=true fingerprint=59cb559befd9b76b]` |
| Its apphost `Notebook.exe` | none (`grep -c` 0): the reader goes to the DLL beside it |
| The Notebook's published NativeAOT `Notebook.exe` | `[spar-build-identity/1 variant=Production revision=b59a518… dirty=true fingerprint=59cb559befd9b76b]` |
| A new app's published `Notes.exe` and Debug `Notes.dll`, outside any repository (package tests, guide) | `variant=Production` / `Development`, `revision=` and `dirty=` empty |

The host's first line, as each host wrote it:

- Windows, the new app's Debug window (guide run): `Host started 510.2 ms after the process; build Development, no
  revision (not built in a git work tree), contract fingerprint f0de0190c7341cfb.`
- Windows and Linux, WindowHost's Production NativeAOT executable under the black box: matched by the new assertion in
  `BlackBoxFixtureTests.The_window_is_read_and_driven_through_the_accessibility_API` (passed on both).
- Loopback, the Notebook under `dotnet watch -lp Loopback`: `Host started on loopback (no window); build Development,
  revision b59a518… (uncommitted changes), contract fingerprint 59cb559befd9b76b; app id com.example.Notebook.Development,
  folder …` (the `folder` stays last, so `watch-run.mjs`'s reader still matches).

### Cost and incrementality

- **No-op Debug build of the Notebook** (warm MSBuild server, this repository, 8 builds per block after 2 warm-ups,
  alternating blocks): with the identity 0.666–0.753 s (medians 0.695 and 0.695 s), with `-p:SparBuildIdentity=false`
  0.531–0.607 s (medians 0.557 and 0.551 s): **+0.14 s**, against the 1.5 s budget. Isolated (no server, `-nr:false`,
  5 builds): 2.357–2.491 s (median 2.43) against 2.173–2.288 s (median 2.27, one outlier of 7.2 s left out). In the
  performance summary the target is 151 ms, all of it the `Exec` of git; `git --no-optional-locks status` itself took
  97–126 ms from PowerShell (`status --porcelain` 120, `-uno` 112, `diff-index --quiet HEAD` 101 ms medians), so the
  untracked scan costs little and the process start most.
- **Design-time build:** `dotnet msbuild -t:Compile -p:DesignTimeBuild=true -p:BuildingInsideVisualStudio=true
  -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true` left the Notebook's `GeneratedMSBuildEditorConfig.editorconfig`
  byte for byte as the build wrote it (MD5 `559638dc…`, time unchanged), with no git process. The same design-time build
  with `-p:SparBuildIdentity=false` (no record read) rewrote it (MD5 `46fc327e…`, both values empty), and the next build
  wrote it back and compiled: what a design-time build without the record would do after every real build.
- **What changes the identity** (and so recompiles C# and, when publishing, relinks the native code): a commit (already
  the case: the SDK's `InformationalVersion` carries `+<revision>`), and the tree's state turning from clean to dirty or
  back, once each. Never the client step or the frontend steps; `Spar.BuildTests`' unchanged build still runs none of
  Spar's incremental steps.
- **Without git on PATH** (PowerShell, `PATH` without `\Git\`): the build succeeded with 0 warnings; the record said
  `revision=b59a518…` (from Source Link) and `dirty=` (unknown).
- **Outside a work tree** (the package tests' and the guide's consumers): no git process, the revision and the state
  empty, 0 warnings.
- **Hot Reload** (`docs/evidence/m1d-hot-reload/watch-run.mjs --edits body` on the Notebook, Windows, loopback): the body
  edit applied by Hot Reload 602 ms after the write (its restore 87 ms), the toast and the page's refetch as in step 3,
  0 CSP messages, 0 failures: the identity's file did not make the session's first edit a rude edit.

## The tool

- **Package:** `Spar.Tool.0.1.0-dev.N.nupkg` 365,974 B: `tools/net11.0/any/` with `Spar.Tool.dll` (165,888 B), its
  `deps.json`, `runtimeconfig.json`, `DotnetToolSettings.xml`, the PDB, and `runtimes/win-{x64,arm64,x86}/native/
  WebView2Loader.dll`. Packed in 1.5–2.6 s. A first version with `PrivateAssets="all"` on the WebView2 reference packed
  227 KB without the three DLLs (they were in `deps.json` only), and its WebView2 check still passed here because
  `WebView2Loader.dll` is on this machine's `PATH` (the Windows SDK's Windows Performance Toolkit): the package test now
  checks the package's entries.
- **NativeAOT check** (not shipped): `dotnet publish src/Spar.Tool -r win-x64 -p:PublishAot=true -p:PackAsTool=false`
  into the scratch folder: 0 warnings, a 7,497,216 B executable that ran `doctor --json` on the Notebook (19 findings,
  the identity from the Notebook's native `Notebook.exe`).
- **Commands as run** (Windows guide, from packages): `dotnet tool restore` 350–774 ms; `dotnet spar doctor` 0.7–1.5 s;
  `dotnet spar --version` 0.19–0.36 s (`0.1.0-dev.guide…+b59a518…`); `dotnet spar init frontend` on the new app 0.22 s
  ("The frontend of Notes is already wired", in the second run; the first said "Notes's frontend …", since reworded).

### `spar doctor`

On the new app from packages (Windows guide), every finding information, exit 0:
`dotnet-sdk` (11.0.100, global.json 11.0.100 `latestFeature`), `spar-sdk` (from the package folder), `node` 24.21.0,
`npm` 11.19.0, `webview2-runtime` 157.0.4341.60, `windows-nativeaot` (VS 18 Community), `windows-long-paths` (off, and
what that means), `project`, `frontend-folder`, `frontend-spar-dependency`, `frontend-scripts`, `frontend-vite-plugin`,
`frontend-lockfile`, `frontend-node-modules`, `contract-snapshot`, `launch-profiles`, `build-identity` (the Debug DLL:
Development, no revision). `--json` the same, exit 0. `--app …/publish/Notes.exe` read Production from the NativeAOT
executable; `--app …/Debug/…/Notes.exe` read Development from `Notes.dll` beside it.

On the Notebook in this repository: the same, plus `spar-tool-version` information ("Spar.Sdk 0.0.0-source is Spar's
source tree") and the identity with the repository's revision and "uncommitted changes".

### `spar init frontend`

The Windows guide's `scaffold` step on a second new app (`Pages`, `--no-end-to-end-tests`) whose `frontend/` was removed:
- `dotnet spar init frontend --framework react`, standard input not a terminal: "would make frontend/ with npm create
  vite@9.3.4 (template react-ts) … Nothing written", exit 1, 0.2 s, no `frontend/` and no npm run.
- With `--yes`: create-vite 9.3.4 wrote 18 files into `obj/spar/init/<id>/frontend`, the diff showed `@spar/app` added
  first in `dependencies` and `import spar from '@spar/app/vite'` with `spar()` in `plugins` (the file's own quotes and
  no semicolons), the folder moved into place, `npm install --ignore-scripts` wrote the lockfile; exit 0 in 11.0 s (12.8 s
  in the package tests), `obj/spar/init/` left empty.
- `dotnet build` then succeeded, and `dotnet spar doctor` gave every project finding information but
  `frontend-type-check` (warning, exit 1): the scaffold's `tsconfig.json` is `"files": []` with references.
- That warning is real: in the scaffold, `node node_modules/typescript/bin/tsc --noEmit -p .` (the build's check)
  exited 0 with `src/bad.ts` holding `const x: number = "a"`.

## Tests

| Run | Windows | Linux (WSL) |
|---|---|---|
| `dotnet build Spar.slnx -c Debug` | 0 warnings, 0 errors | 0 warnings, 0 errors |
| CoreCLR tests without window and black-box | 1,237: 1,214 passed, 23 skipped | 1,237: 1,196 passed, 41 skipped |
| `Spar.Tool.Tests` (in the above) | 79 of 79 | 79 of 79 |
| `Spar.Generator.Tests` (in the above) | 132 of 132 (6 new) | same |
| `Spar.WindowTests` | 220: 118 passed, 102 skipped | 220: 170 passed, 50 skipped, 0 processes left |
| `Notebook.EndToEndTests` | 27: 23 passed, 4 skipped | not run |
| TypeScript SDK `npm run check`, `npm test`, `npm run build` | 330 tests passed | `client` step passed |
| `Spar.BuildTests` | 7 of 7 (twice, the second after the fix) | not run (its `boundary` step not run) |
| `Spar.PackageTests` | 28 of 28 in 2 min 30 s, and in 2 min 23 s after the tool package's fix (below) | 28 of 28 in 3 min 58 s, 0 processes left |
| `eng/check-size.cs` | 627 files, 0 errors | — |

`Spar.PackageTests`' new `ToolTests` (4) and `NewAppTests`' manifest test: `dotnet tool restore` 0.17–0.33 s, `dotnet
spar doctor --json` 0.62–0.64 s each, `--app` on the published executable and the Debug apphost, the moved-away
`node_modules` exit 1, `init frontend` without and with `--yes` as in the guide.

## Linux

The guide's tools section from packages in WSL (`guide-linux.sh`, all steps; times are shorter than on Windows):
- `dotnet tool restore` 161 ms (`spar.tool` restored; the CLI's home, under the run's `HOME`, got `toolResolverCache/1`
  and the first-use sentinels).
- `dotnet spar doctor` on the new app, every finding information, exit 0: `dotnet-sdk` 11.0.100, `spar-sdk`, `node`
  24.22.3 and `npm` 11.19.1 (`/usr/bin`), `linux-gtk4` 4.14.5, `linux-webkitgtk` 2.52.6, `linux-sandbox-tools`, `linux-icu`
  (`libicuuc.so.74`), `linux-openssl` (`libssl.so.3`), `linux-clang`, `linux-zlib` (`libz.so`), the nine project findings,
  `build-identity` (the Debug DLL, Development, no revision). `--json` exit 0.
- `dotnet spar init frontend` on the new app: already wired, exit 0. The `scaffold` step as on Windows: exit 1 and
  nothing without `--yes`, exit 0 in 19.4 s with it, `dotnet build` succeeded, doctor exit 1 with `frontend-type-check`.
- `--app …/publish/Notes` (NativeAOT ELF): Production, no revision; `--app …/Debug/…/Notes` (the apphost): Development,
  read from `Notes.dll`.
- The published executable on a private Xvfb (:198) with its own D-Bus session: `Host started 7 ms after the process;
  build Production, no revision (not built in a git work tree), contract fingerprint f0de0190c7341cfb.`, then `ui-ready
  at 207.2 ms`; ended with `SIGTERM`, no process left.

## Not verified

- `spar doctor` on a machine that lacks something for real: every "missing" and "too old" path is checked against the
  fake machine only (no installs or uninstalls). The version managers' folders other than nvm-windows (on `PATH` here)
  were never seen on a real machine.
- `spar init frontend`'s question answered in a real terminal (the tests feed it `y` and `n`; the runs here had no
  terminal), `--framework vue` with the real scaffolder, and a `vite.config` from a real project too unusual to edit.
- The identity of a compressed single-file bundle (`EnableCompressionInSingleFile`): not read, and doctor says so.
- The lock race that `--no-optional-locks` avoids (from git's documentation, not reproduced).
- CI; `dnx Spar.Tool …` from nuget.org; the tool on macOS.

## Left behind

Nothing outside the repository's disposable `artifacts/`: no process of the runs (no `msedgewebview2` with a command line
under the repository, none naming the guide's folders), the guide runs' `%TEMP%\spar-guide-m1d6b`, `/var/tmp/spar-guide-m1d6b`
and `/dev/shm/spar-guide-m1d6b` deleted after, no entry written today in the user's `~/.dotnet/toolResolverCache`,
`~/.dotnet/tools` or `~/.templateengine`. In `artifacts/`: the guide's Windows profiles (`webview2-profiles/m1d6b-guide/`),
the watch run's root and the logs (`m1d-spar-tool/`), and `tool-tests/` empty. The package tests' shared caches in
`%TEMP%\spar-package-tests` stay, as before.

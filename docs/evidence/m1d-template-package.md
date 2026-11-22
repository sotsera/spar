# M1d step 6a: the template package and the getting-started guide (evidence)

Recorded 2026-11-22. Plan M1d, build step 6, first half (decision "M1d step 6's shape: the template package and the
spar tool", user decisions, 2026-11-22), and the implementation record "The template package (implementation record;
choices for review; 2026-11-22)". The guide: [`docs/guide/getting-started.md`](../guide/getting-started.md).

## Setup

- Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24.21.0, Playwright 1.64.1, WebView2 Runtime 157.0.4341.60.
- Linux: WSL2 Ubuntu-24.04, the same SDK, Node 24.22.3, WebKitGTK 6.0 from the system, the unpacked Xvfb of
  `docs/evidence/m1c-host/wsl.sh`, in the copy `docs/evidence/m1c-blackbox/wsl.sh sync` keeps in
  `/var/tmp/spar-m1c-blackbox/repo` (written `<copy>`); `DISPLAY` and `WAYLAND_DISPLAY` unset except for a private Xvfb
  a step starts itself, the core limit 1 byte.
- **Template hives:** every `dotnet new` here ran with `--debug:custom-hive` (the package tests' under their run folder,
  the guide runs' under theirs). The user's own `%USERPROFILE%\.templateengine` has no entry written on 2026-11-22
  (checked with `find -newermt` after the runs).
- **Per-user folders:** the package tests' consumer runs get `SPAR_TEST_ARTIFACTS=<repo>/artifacts`, as in step 4; the
  guide runs on Windows set `LOCALAPPDATA` under `artifacts/webview2-profiles/m1d6a-guide/` for every command, so the
  apps' folders, their WebView2 profiles, Playwright's and npm's own folders never reached the user's `%LOCALAPPDATA%`;
  in WSL `HOME` and every XDG folder were under `/var/tmp/spar-guide-m1d6a/home-root/`.
- Paths in the copied logs are written `<repo>`, `<copy>`, `%TEMP%` and `%USERPROFILE%`.

## Results

### The package

`dotnet pack src/Spar.Templates -c Release -p:VersionSuffix=dev.N`: `Spar.Templates.0.1.0-dev.N.nupkg`, 29,350 B, package
type Template, no dependencies and no `lib/`, 29 files under `content/spar-app/` (the 27 a new app gets, plus
`.template.config/template.json` and `dotnetcli.host.json`). Its `template.json` carries the repository's .NET SDK
(`11.0.100`) and the packed version in the two constants; the authored file keeps `PACKED_…`, and `git status` reads the
same before and after (the package tests' check). The pack takes 1.1–2.5 s.

- **The Linux pack wrote a broken `template.json`** at first: `\"windows\"` came out as `/"windows/"` (MSBuild on Unix
  turns a task item's backslashes into slashes), and `dotnet new install` refused it ("'w' is invalid after a value",
  "No templates were found in the package"). The conditions now use single quotes, the leading-digit pattern `[.]`, and
  the pack fails on any backslash; the same pack on Linux then installed and made the app (below).

### `dotnet new spar-app`

| Run (Windows) | Result |
|---|---|
| `-n Notes` | 27 files: the content with `MyApp` → `Notes` and `myapp` → `notes` (`notes-frontend`), the solution with 3 projects, `<SparPlatforms>windows;linux</SparPlatforms>`, `<SparAppId>com.example.Notes</SparAppId>`, `global.json` with `11.0.100`/`latestFeature` and `Spar.Sdk` at the package's version, ending in LF; no `nuget.config`, no `.config/` |
| `--platforms windows`, `--platforms linux` | `<SparPlatforms>windows</SparPlatforms>`, `<SparPlatforms>linux</SparPlatforms>` |
| `--app-id org.example.notes.Tool` | `<SparAppId>org.example.notes.Tool</SparAppId>` |
| `--no-end-to-end-tests` | no `Notes.EndToEndTests/`, the solution with 2 projects |
| `-n my-app` | `my-app/` holding `my_app/my_app.csproj`, `my_app.Tests/`, `my_app.slnx`, every path inside `my_app`, app id `com.example.my_app`, npm name `my_app-frontend` |
| `-n Acme.Notes` | `Acme.Notes/Acme.Notes.csproj`, namespace `Acme.Notes`, app id `com.example.Acme.Notes`, npm name `acme.notes-frontend` |
| `-n 1Nötes.2x` | `_1Nötes._2x/…` (the namespace form), app id `com.example._1N_tes._2x` |
| `-h` | the three options with their help, `both|linux|windows` (default `both`), no short aliases |

With `sourceName` `MyApp` (the first try), `-n my-app` had folders `my-app/` and paths `my_app/my_app.csproj` inside the
solution and the test projects, which pointed at nothing (pitfalls); the derived name fixed it, as the table shows.

### The package tests

`dotnet test --project tests/Spar.PackageTests` (after `npm run build` in `src/Spar.Client`): **23 of 23 on Windows in
3 min 33 s, 23 of 23 in 4 min 47 s on Linux.** Every command and its time: [`timings-windows.txt`](m1d-template-package/timings-windows.txt),
[`timings-linux.txt`](m1d-template-package/timings-linux.txt). An earlier Windows run of the same tests, before the
backslash fix, passed 23 of 23 in 3 min 18 s.

| New in step 6a | Windows | Linux |
|---|---|---|
| `dotnet new install <feed>/Spar.Templates.<First>.nupkg --debug:custom-hive <run>/template-hive` | 1.66 s | 0.34 s |
| `dotnet new spar-app -o "Nötes Äpp" -n Notes` (the main consumer; then only its `nuget.config` is written) | 0.61 s | 0.24 s |
| `NewAppTests`: the files equal the template's content renamed; `global.json`; platforms, app id, profiles, solution | passed | passed |
| `--no-end-to-end-tests` into "Fast Äpp", then `dotnet test` in its folder (the solution: 2 tests) | 0.59 s + 13.0 s | 0.26 s + 3.9 s |
| `--platforms <this OS> --app-id org.example.notes.Single` into "One Äpp", `dotnet build Notes`; `--platforms <other OS>` into "Other Äpp" | 0.62 s + 8.7 s; 0.60 s | 0.25 s + 2.5 s; 0.25 s |

The other 18 are step 4's, now on the app `dotnet new` made: its first build and fast tests (12.9 s on Windows), their
NativeAOT lane (32.2 s + 0.14 s), `npm test`, the browser, window and black-box invocations (7.5, 17.4 with the
end-to-end project's first build, 5.8 s), the trait check, the analyzer, SPAR120–SPAR122, SPAR130, the design-time
build, the publish without its host, the update and downgrade (which now change only the `Spar.Sdk` version in the
template's own `global.json`), the central and library consumers. On Linux the first build and fast tests took 77.8 s
and the end-to-end project's first build (inside the trait check's run) 126.7 s, then the browser, window and black-box
invocations 2.2–2.6 s each (none skipped: Xvfb, `dbus-daemon` and WebKitGTK 6.0 found), the NativeAOT lane 16.9 s +
0.01 s; `census` found 0 processes left.

### The guide's commands

Every command of [`docs/guide/getting-started.md`](../guide/getting-started.md) except the prerequisites' install
commands, from Spar's eight packages packed at a version of their own into a feed outside the repository, with the
`nuget.config` naming it in the folder above the new app (a published Spar needs none). The scripts:
[`guide-win.sh`](m1d-template-package/guide-win.sh) (Git Bash), [`guide-linux.sh`](m1d-template-package/guide-linux.sh)
(WSL) with [`linux-stop.sh`](m1d-template-package/linux-stop.sh), and [`loopback-check.cjs`](m1d-template-package/loopback-check.cjs),
which runs `dotnet run -lp Loopback` with `SPAR_LOOPBACK_BROWSER=none`, opens the printed address in Playwright's headless
Chromium, greets a name and ends the app's tree and its Vite server. The window runs on Windows use
`docs/evidence/m1d-hot-reload/WatchDesktop.cs` with its new `--command` (a desktop of the run's own: nothing on the
monitor); on Linux a private Xvfb (`:197`) with `dbus-run-session`. The Windows runs used the scripts' scratch versions
(the same commands; `guide-win.sh` was then given its `loopback` and `exe` steps and run again for `run`, through the
repository's `WatchDesktop.cs`: `ui-ready at 1033.9 ms`) and a package packed before the backslash fix, whose content
differs only in `template.json`'s quoting; the Linux runs used the fixed package.

| Command | Windows | Linux (WSL) |
|---|---|---|
| `dotnet new install Spar.Templates@<version>` (with `--add-source <feed>`; on Windows written `::<version>`, which still works and prints that `::` is deprecated for `@`) | 2.2 s | 0.33 s |
| `dotnet new list spar`, `dotnet new spar-app -h` | 0.6 s each, the template and its three options | not run |
| `dotnet new spar-app -n Notes` | 0.9 s, 27 files | 0.24 s |
| `dotnet run` (in `Notes/Notes`) with no display | — | exit 6 in 6.8 s: "No graphical session … run the page in a browser instead: dotnet run -lp Loopback" |
| `dotnet run` (first build included) | `ui-ready` 12.7 s after the command (restore, npm ci, client, type-check, Vite started); the host's own `ui-ready at 1267.1 ms` | `ui-ready at 583.8 ms` (and 611 ms in a second run), `xdg-desktop-portal` warnings only |
| `dotnet run -lp Loopback` (`loopback-check.cjs`) | the address after 4.4 s; "Hello, Ada!" in Chromium at 6.1 s | the address and "Hello, Ada!" at 3.2 s |
| `dotnet watch`, then `Hello, ` → `Hi, ` in `GreetingService.cs` | `ui-ready at 1038.8 ms`; "Hot Reload applied revision 1 (Notes.Greetings.GreetingService)" 0.84 s after the save ("changes applied in 751ms") | `ui-ready at 433.5 ms`; applied 0.4 s after ("changes applied in 399ms") |
| `dotnet test --project Notes.Tests` | 9.0 s, 2 of 2 | 4.0 s, 2 of 2 |
| `npm test` in `Notes/frontend` | 3.4 s, 3 of 3 | 0.44 s, 3 of 3 |
| `dotnet build Notes.EndToEndTests` | 16.4 s (the app's black-box NativeAOT publish included); `playwright.ps1` in `bin/Development/Debug/net11.0/win-x64/` | 7.2 s; in `…/linux-x64/` |
| `pwsh …/playwright.ps1 install chromium` | 125 s, 719 MB into the run's `PLAYWRIGHT_BROWSERS_PATH` (Chromium, headless shell, ffmpeg, winldd) | not run: no PowerShell in the distribution; the shared cache already had the headless shell 1248 |
| `--filter-trait Surface=Browser`, `Window`, `BlackBox` | 7.5, 5.5, 3.1 s, 1 of 1 each | 2.2, 2.2, 2.1 s, 1 of 1 each |
| `dotnet publish Notes.Tests -c Release -p:SparTestLane=Aot -o Notes.Tests/bin/aot-tests`, then the executable | 30.0 s + 0.30 s, `Total: 2, Errors: 0, Failed: 0, Skipped: 0` | 20.2 s + 0.01 s, the same |
| `dotnet publish Notes -c Release` | 15.2 s; `Notes/bin/Production/Release/net11.0/win-x64/publish/Notes.exe` 4,739,072 B (`Notes.pdb` 19,779,584 B) | 6.1 s; `…/linux-x64/publish/Notes` 4,671,672 B (`Notes.dbg` 7,457,352 B) |
| the published executable | `ui-ready at 356.1 ms`, app id `com.example.Notes` | `ui-ready at 220.2 ms` |
| `dotnet new uninstall Spar.Templates` | 1.8 s | 0.29 s |

- **What git sees:** after every command above, `git init` in the new app's folder listed exactly the 27 files
  `dotnet new` wrote and `Notes/Notes.contracts.snapshot`: the `.gitignore` covers `bin/`, `obj/` (the generated client,
  the test results, `Notes.Tests/bin/aot-tests`) and `node_modules/`.
- **The app's folders:** Windows `%LOCALAPPDATA%\com.example.Notes.Development` (Debug) and `com.example.Notes` (the
  published one), with `logs/app.log`; Linux `$XDG_DATA_HOME/com.example.Notes.Development` and `com.example.Notes`
  (`~/.local/share/…`), each with `logs/`.
- **Ctrl+C:** the Windows runs' Ctrl+C went to a run without a console (the tool's shell has none), so the harness ended
  `dotnet run`'s and `dotnet watch`'s trees itself; the Ctrl+C path is step 3's evidence.
- **Left behind:** on Windows no process with the run's folders in its command line, no WebView2 process of the runs'
  roots; `artifacts/webview2-profiles/m1d6a-guide/local/` empty (nothing used the redirected `LOCALAPPDATA` but the apps).
  In WSL, the first `dotnet watch` run left its `dbus-run-session` tree and the Vite server (the script ended the wrong
  session); `linux-stop.sh kill` ended those 12 processes, and after the fix (`exec setsid`) the `run` and `watch` steps
  left none. Both runs' folders (`%TEMP%\spar-guide-m1d6a`, with Playwright's 719 MB, and `/var/tmp/spar-guide-m1d6a`)
  were deleted afterwards; the apps' folders and profiles stay in the disposable `artifacts/webview2-profiles/m1d6a-guide/`.
- **The prerequisites' packages:** `dpkg -s` in the WSL distribution found `libwebkitgtk-6.0-4`, `libgtk-4-1`,
  `bubblewrap`, `xdg-dbus-proxy`, `xauth`, `dbus-daemon`, `at-spi2-core`, `clang` and `zlib1g-dev` installed; `xvfb` is
  not (the repository's runs use an unpacked Xvfb). No `apt install` was run.

### Spar's own runs after the change

Windows:

| Run | Result |
|---|---|
| `dotnet build Spar.slnx -c Debug` (now with `Spar.Templates`) | 0 warnings, 0 errors, 70 s |
| CoreCLR, the solution without `Surface=Window` and `Surface=BlackBox` | 1,150 (1,127 passed, 23 skipped) |
| `dotnet test --project tests/Spar.WindowTests` | 220 (118 passed, 102 skipped) in 1 min 25 s |
| `dotnet test --project samples/Notebook.EndToEndTests` | 27 (23 passed, 4 skipped) |
| TypeScript SDK: `npm run check`, `npm test`, `npm run build` | clean; 330 tests |
| `dotnet test --project tests/Spar.BuildTests` | 6 of 6 in 3 min 8 s |
| `dotnet run eng/check-size.cs` | 586 files, 0 errors, 0 warnings |

Linux (`docs/evidence/m1c-blackbox/wsl.sh sync client appmodel build test`, then `packagetests`): the build in 25 s;
CoreCLR without window and black-box 1,150 (1,109 passed, 41 skipped); the package tests 23 of 23 in 4 min 47 s with 0 processes left.

## Not run

- CI (not run until the first push), and so the package tests on the hosted runners.
- The prerequisites' install commands (`apt install`, the .NET SDK, Node.js, WebView2, the C++ build tools): the
  machines had them; the package names were checked with `dpkg` on Ubuntu 24.04 only.
- `dotnet new install Spar.Templates` from nuget.org (Spar is not published): the runs installed the same package by id
  from a local feed with `--add-source`.
- Playwright's `playwright.ps1` on Linux (no PowerShell in the distribution) and with `--with-deps`.
- `Spar.WindowTests` in WSL, the NativeAOT lanes of Spar's own projects and the Sandbox suite: nothing they build
  changed (the template's content is built only by the package tests).
- F5 of a new app in Visual Studio and VS Code (added to the plan's table "Checks that need the user at the PC").
- An on-screen window of a new app: every window ran on a desktop of its own or a private Xvfb.

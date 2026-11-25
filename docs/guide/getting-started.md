---
title: Getting started
description: From an empty machine to a published Spar desktop app on Windows and Linux.
---

# Getting started

This guide takes you from a machine with nothing installed to a published desktop app. The app has a C# backend and a page written with React, TypeScript and Vite, shown in one window: on Windows through the WebView2 Runtime, on Linux through WebKitGTK. You create it with `dotnet new`, run it with `dotnet run`, change it under `dotnet watch`, test it with `dotnet test` and `npm test`, and publish it as one native executable with `dotnet publish`.

## 1. Install the prerequisites

You need the .NET SDK and Node.js on every machine, and the web engine of the OS. Publishing a native executable needs the OS's native build tools as well.

### The .NET SDK

Install the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) ([install .NET on Windows](https://learn.microsoft.com/dotnet/core/install/windows), [on Linux](https://learn.microsoft.com/dotnet/core/install/linux)). Check it:

```sh
dotnet --version
```

A new app's `global.json` asks for the SDK it was made with, or a later feature band of the same version (11.0.100 or later).

### Node.js

Install [Node.js](https://nodejs.org/) 24 or later, which comes with npm. A version manager works too, as long as `node` and `npm` are on `PATH` where you run `dotnet`. Check it:

```sh
node --version
npm --version
```

The build installs the page's packages with npm, type-checks and bundles it with Vite, and a Debug run serves it from Vite's development server, so Node.js is needed to build and to run in Debug. A published app does not need it.

### Windows

- **The WebView2 Runtime.** Windows 11 comes with it. On other systems install the Evergreen Runtime ([WebView2 distribution](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution)).
- **For publishing:** NativeAOT needs the C++ build tools of Visual Studio, the "Desktop development with C++" workload ([NativeAOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites)).

### Linux

A desktop session with GTK 4 and WebKitGTK 6.0. On Ubuntu 24.04:

```sh
sudo apt install libwebkitgtk-6.0-4 libgtk-4-1 bubblewrap xdg-dbus-proxy
```

- **For publishing:** NativeAOT needs `clang` and the zlib development files ([NativeAOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites)):

  ```sh
  sudo apt install clang zlib1g-dev
  ```

- **For the end-to-end tests:** the window and black-box tests start a private X server and D-Bus session of their own, and read the app through the accessibility bus, so they need:

  ```sh
  sudo apt install xvfb xauth dbus-daemon at-spi2-core
  ```

## 2. Install the template

```sh
dotnet new install Spar.Templates
```

This installs the `spar-app` template from NuGet. The template and the app it makes use the same version of Spar: a new app's `global.json` names it. `dotnet new install Spar.Templates@<version>` installs a given version, `dotnet new list spar` shows the template, and `dotnet new uninstall Spar.Templates` removes it ([`dotnet new install`](https://learn.microsoft.com/dotnet/core/tools/dotnet-new-install)).

## 3. Create an app

```sh
dotnet new spar-app -n Notes
cd Notes
```

`-n` names the app; without it the app takes the current folder's name. The name becomes the projects' names and the C# namespace, so characters a namespace cannot have (a `-`, a leading digit) become `_`.

The template's options:

| Option | Default | What it does |
|---|---|---|
| `--platforms <both\|windows\|linux>` | `both` | The OSes the app runs on, written as `<SparPlatforms>` in the app's project. |
| `--app-id <id>` | `com.example.<name>` | The app's id, `<SparAppId>`: reverse-DNS, unique to the app. It names the app to the OS and its per-user folders; replace `com.example` with a domain you own before you ship. |
| `--no-end-to-end-tests` | off | Leaves out the end-to-end test project. The app and its fast tests are always created. |

`dotnet new spar-app -h` lists them. The new folder holds:

```text
Notes/
  Notes.slnx                    the solution, with the projects below
  global.json                   pins the .NET SDK and Spar.Sdk, and the test runner
  .config/dotnet-tools.json     the local tool manifest: the spar tool, at the same Spar version
  .github/workflows/ci.yml      CI on GitHub Actions: build, tests and publish on each OS the app runs on
  .gitignore
  Notes/                        the app
    Notes.csproj                <Project Sdk="Spar.Sdk">, its SparAppId and SparPlatforms
    Program.cs                  the app's start: its window's size and colours
    Greetings/GreetingService.cs  a contract: C# the page calls
    Properties/launchSettings.json  the launch profiles Desktop and Loopback
    frontend/                   the page: React + TypeScript + Vite
      package.json              "@spar/app": "file:../obj/spar/client", the generated client
      package-lock.json         commit it: the build installs exactly these packages
      vite.config.ts            the Spar Vite plugin
      index.html
      src/                      App.tsx, greet.ts (the call), greet.test.ts (page tests)
  Notes.Tests/                  fast tests: commands through the generated C# clients
  Notes.EndToEndTests/          browser, window and black-box tests
```

Every warning fails the build in each project (`TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors`); turn them off in a project if you prefer. The `.gitignore` keeps `bin/`, `obj/` and `node_modules/` out of version control.

## 4. Run it

From the app's folder:

```sh
cd Notes
dotnet run
```

The first build restores the NuGet packages, installs the page's npm packages, generates the TypeScript client of the app's contracts into `obj/spar/client`, and type-checks the page. A Debug run starts Vite's development server for the page (or reuses the one it started before) and shows the window. Type a name and press Greet: the page calls `GreetingService.GreetAsync` in the backend.

`dotnet run` uses the first launch profile, `Desktop`, the window. The other profile serves the same app to a browser instead, with no window:

```sh
dotnet run -lp Loopback
```

It prints the page's address, `http://127.0.0.1:<port>/#token=…`, and opens it in the default browser the first time. Restarts keep the address, and the open page reloads by itself when the backend is back. Set `SPAR_LOOPBACK_BROWSER=none` to keep the browser closed. A Release build has no loopback host.

On Linux without a graphical session (no `DISPLAY` or `WAYLAND_DISPLAY`, such as over SSH), `dotnet run` stops with exit code 6 and a message that names `dotnet run -lp Loopback`; the app never switches by itself.

The first build also writes `Notes.contracts.snapshot` beside the project, the contracts as the page sees them. Commit it: a CI build (`ContinuousIntegrationBuild=true`) fails when it is missing or out of date, and a local build updates it.

## 5. Change it while it runs

```sh
dotnet watch
```

`dotnet watch` builds and starts the app, then follows your edits:
- **The page:** Vite updates it in place (Fast Refresh), keeping its state.
- **A C# method body:** Hot Reload applies it to the running app, and the window shows a short "Backend updated" note.
- **A contract** (a command's name or parameters, a type the page sees, an error case): the app restarts, and the build regenerates the TypeScript client, so the page's type-check sees the change.

It uses the `Desktop` profile, as `dotnet run` does. Stop it with Ctrl+C. See [`dotnet watch`](https://learn.microsoft.com/dotnet/core/tools/dotnet-watch) for its own options.

## 6. Test it

The test commands run from the solution's folder. The fast tests run the app's own `Program.cs` inside the test process and call its commands through the generated C# clients:

```sh
cd ..
dotnet test --project Notes.Tests
```

The page tests run the page's code in Node with Vitest, against a fake backend from `@spar/app/testing`. They import the generated client, so build once first (any of the commands above does):

```sh
cd Notes/frontend
npm test
cd ../..
```

The end-to-end tests run each kind as its own invocation, picked by its `Surface` trait:

```sh
dotnet test --project Notes.EndToEndTests --filter-trait Surface=Browser
dotnet test --project Notes.EndToEndTests --filter-trait Surface=Window
dotnet test --project Notes.EndToEndTests --filter-trait Surface=BlackBox
```

- **Browser:** the app's page in headless Chromium, driven by [Playwright](https://playwright.dev/dotnet/), against the backend in the test process. Playwright needs its Chromium once per machine. Build the project, then run its install script with [PowerShell](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) (the folder `win-x64` on Windows, `linux-x64` on Linux, where `--with-deps` also installs Chromium's system libraries):

  ```sh
  dotnet build Notes.EndToEndTests
  pwsh Notes.EndToEndTests/bin/Development/Debug/net11.0/win-x64/playwright.ps1 install chromium
  ```
- **Window:** the app's real window, offscreen and never activated (on Linux, on a private X server), so it never takes your focus.
- **BlackBox:** the app's published Production executable, launched and read through the OS's accessibility API. The build publishes it once, beside the tests.

A test that starts the window or the browser without its trait fails at its start and names the trait to add. The fast tests also run published with NativeAOT, as the shipped app runs:

```sh
dotnet publish Notes.Tests -c Release -p:SparTestLane=Aot -o Notes.Tests/bin/aot-tests
Notes.Tests/bin/aot-tests/Notes.Tests
```

(`Notes.Tests.exe` on Windows.) The browser tests stay out of that lane: Playwright does not run under NativeAOT. Test results go to each test project's `obj/test-results/`.

## 7. Publish it

```sh
dotnet publish Notes -c Release
```

This builds the page with Vite, packs it into the executable, and publishes one NativeAOT executable for the OS you run it on, in `Notes/bin/Production/Release/net11.0/<runtime>/publish/`: `win-x64/publish/Notes.exe` on Windows, `linux-x64/publish/Notes` on Linux, about 4.7 MB for the new app, beside its debug symbols (`Notes.pdb`, `Notes.dbg`), which you do not ship. NativeAOT does not cross-compile between OSes: publish each OS's executable on that OS, or in a container of it. The executable needs no .NET or Node.js on the user's machine; on Windows it needs the WebView2 Runtime, on Linux GTK 4 and WebKitGTK 6.0.

The app can publish differently, each a choice in its own project file: on CoreCLR (framework-dependent, or as a self-contained single file), with NativeAOT while accepting a library's AOT warnings, or with culture data. [Publishing choices](publishing-choices.md) shows each, with Microsoft.Data.SqlClient as a worked example.

## 8. Check and wire with the spar tool

A new app's local tool manifest, `.config/dotnet-tools.json`, names the `spar` tool (the `Spar.Tool` package) at the app's own Spar version. Restore it once, from the solution's folder ([`dotnet tool restore`](https://learn.microsoft.com/dotnet/core/tools/dotnet-tool-restore)):

```sh
dotnet tool restore
```

Then check the machine, the app and its last build, from the solution's folder or the app's:

```sh
dotnet spar doctor
```

It checks the .NET SDK against `global.json` and Spar.Sdk; Node.js and npm (and names a Node.js that a version manager such as nvm, fnm, Volta, mise or asdf installed but that is not on `PATH`); the WebView2 Runtime and NativeAOT's C++ build tools on Windows, or GTK 4, WebKitGTK 6.0, ICU, OpenSSL, `clang` and zlib on Linux; the app's frontend folder, its `@spar/app` dependency, Spar's Vite plugin, the `dev` and `build` scripts, the lockfile and `node_modules`, the contract snapshot and the launch profiles; and the build identity of the newest build under `bin/`: the variant, the git revision, whether the work tree had uncommitted changes, and the contracts' fingerprint. Each finding has a stable id, a severity and its fix. By default it prints what needs attention, with its fix, and how many checks passed; `--verbose` lists every check, and `--json` writes every finding as JSON, `--app <file>` reads the identity of another executable (a published one, say) without running it, and the exit code is 0 with nothing to fix, 1 with warnings and 2 with errors. The app writes the same identity in the first line of its log.

`dotnet spar init frontend`, in the app's folder, wires a Vite frontend into an app. An app from the template has its frontend wired already, and there it says so and changes nothing:

```sh
cd Notes
dotnet spar init frontend
```

In an app with no frontend folder yet, `--framework react` (or `vue`) first makes one with Vite's own scaffolder, with TypeScript, and then wires it:

```sh
dotnet spar init frontend --framework react
```

It adds the `@spar/app` dependency and Spar's Vite plugin, and `SparFrontendPath` in the project when the folder is not `frontend/` (`--frontend <folder>` names it), then runs `npm install --ignore-scripts` for the lockfile. It shows every change as a diff and asks before writing; `--yes` writes without asking, and without a terminal to ask in it writes nothing unless given `--yes`. When `vite.config` is too unusual to edit safely, it prints the lines to add instead. Build the app after it (`dotnet build`), and commit the new lockfile.

While a Development build runs, the tool also looks at it and drives its page: `dotnet spar tree` lists the page's elements, `dotnet spar click`, `fill` and `wait` act on them, `dotnet spar call` runs one of the app's commands, and `dotnet spar logs` and `calls` show what happened. [Devtools for agents and scripts](devtools.md) describes them.

## 9. Run it in CI

A new app comes with a [GitHub Actions](https://docs.github.com/actions) workflow, `.github/workflows/ci.yml`, which runs on every push to `main` and on every pull request, once for each OS the app runs on (`--platforms`): the Windows runner and the Ubuntu runner. Each run restores and builds the solution, runs the fast tests, the page tests (`npm test`) and the fast tests published with NativeAOT, then the end-to-end tests the runner can run, and publishes the app. It keeps the test results (TRX reports from each test project's `obj/test-results/`) and the published executable, without its debug symbols, as the run's artifacts.

- **The browser tests** run on both runners, after the workflow installs Playwright's headless Chromium.
- **The window and black-box tests** run on the Windows runner. GitHub's Ubuntu runner does not let WebKitGTK start its sandbox (it refuses the unprivileged user namespaces that `bubblewrap` needs), so on Linux run them on a machine with a desktop, as section 6 shows.
- **The contract snapshot:** the workflow builds with `ContinuousIntegrationBuild=true`, so the build fails when `Notes/Notes.contracts.snapshot` is missing or out of date. Commit it after a local build.
- **The actions** are pinned to full commit SHAs, each with its release in a comment; update them on purpose.

An app made with `--no-end-to-end-tests` gets a workflow without the end-to-end steps.

## Where things live

| What | Where |
|---|---|
| The generated TypeScript client | `Notes/obj/spar/client`, the page's `@spar/app` package. Never edit it; the build writes it. |
| The contract snapshot | `Notes/Notes.contracts.snapshot`, committed. |
| Build output | each project's `bin/` and `obj/`, by variant: `Development` (Debug), `Production` (Release), `Test` (built by a test project). |
| Vite's development server | its record and log in `Notes/obj/` (`spar-dev-server.json`, `spar-dev-server.log`); it ends 60 s after its last use. |
| The app's data and log | the per-user folders named by the app id: on Windows `%LOCALAPPDATA%\com.example.Notes` (`com.example.Notes.Development` for a Debug build, so Debug never shares data with an installed copy), on Linux the XDG folders (`~/.local/share/com.example.Notes` and the others). The log is `logs/app.log` there. |
| Test results | each test project's `obj/test-results/`. |
| The spar tool | `.config/dotnet-tools.json` names it; `dotnet tool restore` puts it in NuGet's package folder, like any package. |

---
name: spar-shipping
description: Publishes and observes a Spar desktop app - the Release (Production) NativeAOT executable per OS, what it carries and needs on the user's machine, the runtime choices an app makes in its own project, its app id, size and third-party notices, the optional OpenTelemetry export and the template's GitHub Actions workflow. Use when publishing a Spar app, choosing its runtime options, checking what ships, or turning on its OpenTelemetry export.
---

# Shipping a Spar app

A Spar app ships as one native executable per OS: the C# backend, Spar, and the page built by Vite, all in one file. This skill covers publishing it, what the user's machine needs, the runtime choices the app's own project can make, and observing it with OpenTelemetry.

Related skills: `spar-app` for writing the app (`Program.cs`, contracts, the start-up hook), `spar-dev-loop` for Debug runs, `dotnet watch` and `spar dev`, and `spar-testing` for the test projects, including the black-box tests that run the published executable.

## Publish

From the solution's folder, for an app named `Notes`:

```sh
dotnet publish Notes -c Release
```

- **Release is the Production variant.** Debug builds `Development` (development server, devtools, loopback host), Release builds `Production`. Ship only a Release publication. Publishing any other variant (`-c Debug`, or an explicit `SparVariant`) fails with SPAR140 before anything is written; `-p:SparAllowNonProductionPublish=true` turns it into a warning, even under warnings as errors, for a support build that must never reach users as a release.
- **NativeAOT by default.** Spar.Sdk sets `PublishAot` to true for every app unless the project says otherwise. The output is one executable, about 4.7 MB for the template's app: `Notes/bin/Production/Release/net11.0/win-x64/publish/Notes.exe` on Windows, `.../linux-x64/publish/Notes` on Linux. The `.pdb` and `.dbg` files beside it are debug symbols: keep them for crash analysis, never ship them.
- **One OS per publish, on that OS.** NativeAOT does not cross-compile. Publish the Windows executable on Windows and the Linux one on Linux (or in a container of it). Spar sets the runtime identifier to the SDK's own (`win-x64`, `linux-x64`); pass `-r win-arm64` or `-r linux-arm64` for arm64, which Spar maps but has not yet qualified, so test on real hardware ([runtime identifiers](https://learn.microsoft.com/dotnet/core/rid-catalog)).
- **The native build tools** are needed to publish: the "Desktop development with C++" workload of Visual Studio on Windows, `clang` and `zlib1g-dev` on Linux ([NativeAOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites)).
- **Every warning fails the build** in an app from the template, trim (`IL2xxx`) and AOT (`IL3xxx`) warnings included. Spar's own code reports none; a library may (see "Accept a library's AOT warnings").

### `SparPlatforms` and `SparAppId`

The template's project names the OSes the app runs on and its app id:

<!-- source: src/Spar.Templates/content/spar-app/MyApp/MyApp.csproj -->
```xml
<Project Sdk="Spar.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Names the app to the OS and its per-user folders: reverse-DNS, unique to this app. -->
    <SparAppId>com.example.MyApp</SparAppId>

    <!-- The OSes the app runs on: windows, linux, or both. Each executable is published on its own OS. -->
    <SparPlatforms>windows;linux</SparPlatforms>
  </PropertyGroup>
  <!-- … -->
</Project>
```

- **`SparPlatforms`** is `windows`, `linux`, or both (the default). It decides which host (WebView2 or WebKitGTK) the build adds; each NativeAOT executable keeps only its own OS's host. A build started on an OS it does not list prints a message naming `<SparPlatforms>` and exits with code 3.
- **`SparAppId`** is required (SPAR130 without it or when it is invalid): reverse-DNS, two or more dot-separated parts of ASCII letters, digits and underscores, at most 116 characters. Replace `com.example` with a domain you own before the first release, then never change it: it names the app to the OS (its AppUserModelID on Windows) and its per-user folders, so a new id loses the users' data.

### Per-user folders

The app id names the folders where the app keeps its data, log and browser profile:

| OS | Data root | Browser profile |
|---|---|---|
| Windows | `%LOCALAPPDATA%\com.example.Notes` | `WebView2` inside it |
| Linux | the XDG folders, such as `~/.local/share/com.example.Notes` | `WebKit` inside it, its cache in `cache/WebKit` |

A Debug build uses `com.example.Notes.Development`, so a developer's runs never share data with an installed copy. The log is `logs/app.log` there; its first line is the build identity (variant, git revision, uncommitted changes, contracts' fingerprint). `spar.Folders(f => f.Profile(path))` in `Program.cs` moves the profile; a path that is not absolute and local, or a profile folder that cannot be created, stops the app with exit code 70.

## What the executable carries

- **The page, embedded.** The Release build runs Vite, checks the output and packs it into the executable. Brotli is used only when it saves more than its decoder costs; `SparFrontendCompression` (`auto`, `brotli`, `none`) overrides that. The build prints one line saying which.
- **Never source maps or secrets.** Spar's Vite plugin turns source maps off, and packing refuses (SPAR110) a `.map` file, a hidden file or folder (`.env`, `.npmrc`, version control), a key or secret file, a link, a file type the hosts do not serve, and names that differ only by case.
- **The third-party notices.** By default the frontend's licences, as Vite collects them, are embedded as the resource `Spar.ThirdPartyNotices`, and the build prints how many packages they cover and their size. Set `SparThirdPartyNotices` to `false` for none, or to the path of your own notices file (SPAR111 when it does not exist). The OpenTelemetry layer appends its page chunk's licences when the app uses it.
- **No test or development code.** NativeAOT removes the test host, development server, devtools and Hot Reload support from a Production executable; the loopback host is never referenced. A CoreCLR publication keeps that code in Spar's assemblies, switched off by its runtime configuration.
- **The build identity**, readable without running the app: `dotnet spar doctor --app <executable>` (not for a compressed single file).

## What the user's machine needs

A NativeAOT executable needs no .NET and no Node.js. It needs the OS's web engine:

- **Windows:** Windows 10 22H2 or later and the WebView2 Runtime, version 139 or later. Windows 11 includes it; otherwise install the Evergreen Runtime ([WebView2 distribution](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution)).
- **Linux:** a desktop session with GTK 4 (4.10 or later) and WebKitGTK 6.0 (2.48 or later), and the helpers WebKit's sandbox needs. On Ubuntu 24.04:

  ```sh
  sudo apt install libwebkitgtk-6.0-4 libgtk-4-1 bubblewrap xdg-dbus-proxy
  ```

  An app with culture data turned on also needs the ICU libraries.

`dotnet spar doctor` checks the developer's machine for the same components.

### Exit codes when the app cannot start

An app launched from an icon has no terminal, so Spar shows a native message (a task dialog on Windows, a GTK message on Linux), writes the problem to standard error and the log, and exits with a fixed code. Installers, launchers and support scripts can rely on them:

| Code | Kind | Meaning |
|---|---|---|
| 3 | `Missing` | the web engine is not installed (also: no host for this OS in `SparPlatforms`) |
| 4 | `TooOld` | the web engine, or GTK, is older than Spar's minimum |
| 5 | `Broken` | installed but does not start; on Linux, its helpers or sandbox are missing |
| 6 | `NoGraphicalSession` | Linux with no display (over SSH, from a service) |
| 7 | `OsTooOld` | Windows older than Windows 10 22H2 |
| 8 | `NotResponding` | a relaunch could not reach the running app |
| 9 | `OtherUser` | the app already runs for another user on the same data folder |
| 10 | | shutdown did not finish and the user chose "Exit now" |
| 70 | | the browser profile folder is not local, or cannot be created |

To replace Spar's message with your own (for example a company install guide), use `spar.OnStartupProblem` in `Program.cs`; see `spar-app`. The exit code stays the kind's whatever the hook does.

## Publishing choices

Spar.Sdk's defaults: NativeAOT, trim and AOT warnings as errors (in a template app), no culture data (`InvariantGlobalization` true). Each choice below is a few lines in the app's own project file, only that app's, after the template's groups. Never put them in a shared props file for a library's sake: a setting one library needs belongs in the project of the app that uses it. Spar's guide "Publishing choices" has the details and measurements.

### CoreCLR instead of NativeAOT

Choose CoreCLR when a library the app needs fails under NativeAOT, or the publishing machine has no native build tools. The window, page, bridge and Production build are the same; the start is 45 to 65 ms slower and the first calls into new code are slower while the JIT compiles them.

Framework-dependent: smallest, but the user's machine needs the .NET 11 runtime (`Microsoft.NETCore.App`) ([framework-dependent deployment](https://learn.microsoft.com/dotnet/core/deploying/#publish-as-framework-dependent)):

<!-- source: docs/guide/publishing-choices.md -->
```xml
<PropertyGroup>
  <!-- CoreCLR, using the .NET runtime installed on the user's machine. -->
  <PublishAot>false</PublishAot>
</PropertyGroup>
```

The publication is a folder (launcher, the app's and Spar's assemblies, `*.runtimeconfig.json`, `*.deps.json`, and `WebView2Loader.dll` on Windows): ship all of it.

Self-contained single file: no .NET needed, at about 80 MB ([single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)):

<!-- source: docs/guide/publishing-choices.md -->
```xml
<PropertyGroup>
  <!-- CoreCLR, with the runtime and every library inside one executable. -->
  <PublishAot>false</PublishAot>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>

  <!-- The native libraries (on Windows the WebView2 loader) inside it too, extracted when the app starts. -->
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
</PropertyGroup>
```

On Windows the loader is extracted at first start to `%TEMP%\.net\<App>\<hash>\`; without the last line it ships beside the executable instead. `EnableCompressionInSingleFile` halves the size, slows the start, and stops `spar doctor --app` from reading the identity.

Measured on the template's app (size without debug symbols; start to `ui-ready`, median of five):

| Publication | Windows | Linux | The user's machine needs |
|---|---|---|---|
| NativeAOT (default) | 4.7 MB, 1 file; 378 ms | 4.7 MB, 1 file; 201 ms | the web engine |
| CoreCLR framework-dependent | 1.8 MB, 10 files; 423 ms | 1.6 MB, 9 files; 263 ms | the web engine and .NET 11 |
| CoreCLR single file | 81.2 MB, 1 file; 441 ms | 79.0 MB, 1 file; 265 ms | the web engine |

Publish each shape into an empty folder (`-o`, or delete `bin/Production/` first): a publish folder keeps an earlier shape's files.

### Accept a library's AOT warnings

A library written before NativeAOT reports its warnings once per assembly: `IL2104` (trim) and `IL3053` (AOT). Accept those two IDs so they stay visible warnings without failing the publish:

<!-- source: docs/guide/publishing-choices.md -->
```xml
<PropertyGroup>
  <!-- A library's trim and AOT warnings stay warnings: every publish shows them, none fails. -->
  <WarningsNotAsErrors>$(WarningsNotAsErrors);IL2104;IL3053</WarningsNotAsErrors>
</PropertyGroup>
```

- You own the risk: the flagged code may fail at run time, only when it runs. Test the features you use against the published executable (the template's black-box tests run it; see `spar-testing`).
- This accepts whole assemblies, and any assembly added later. Read the warnings every publish prints.
- Never use `NoWarn` for them (it hides new assemblies' warnings), and never add member-level IDs (`IL2026`, `IL3050`) to the list: those are the IDs your own code reports, which must keep failing the build.
- To see what one `IL2104` covers, publish once with `-p:TrimmerSingleWarn=false`. That publish fails; it is for reading ([trim warnings](https://learn.microsoft.com/dotnet/core/deploying/trimming/fixing-warnings), [AOT warnings](https://learn.microsoft.com/dotnet/core/deploying/native-aot/fixing-warnings)).

### Culture data

The backend is invariant by default: formatting, parsing, comparison and casing behave the same on every machine ([invariant mode](https://learn.microsoft.com/dotnet/core/runtime-config/globalization#invariant-mode)). The page is unaffected; the web engine has its own culture data. Turn culture data on when backend code must follow the user's locale, or a library refuses invariant mode:

<!-- source: docs/guide/publishing-choices.md -->
```xml
<PropertyGroup>
  <!-- The backend uses the system's culture data (ICU). -->
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

It adds about 200 KB to the executable. On Linux the user's machine then needs ICU, or the app does not start; `spar doctor` checks it (`linux-icu`) ([ICU on Linux](https://learn.microsoft.com/dotnet/core/extensions/globalization-icu)).

### Recipe: Microsoft.Data.SqlClient with NativeAOT

[SqlClient](https://learn.microsoft.com/sql/connect/ado-net/introduction-microsoft-data-sqlclient-namespace) is not written for NativeAOT, refuses invariant mode, and on Windows loads a native networking library. Add to the app's project: the package, the warnings block and the culture block above, and the managed-networking block below.

<!-- source: docs/guide/publishing-choices.md -->
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Data.SqlClient" Version="7.1.2" />
</ItemGroup>
```

<!-- source: docs/guide/publishing-choices.md -->
```xml
<ItemGroup>
  <!-- SqlClient's managed networking on Windows, so the publication needs no native SNI library. -->
  <RuntimeHostConfigurationOption Include="Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows" Value="true" Trim="true" />

  <!-- The native SNI package SqlClient brings, left out of the publication. Keep its version SqlClient's. -->
  <PackageReference Include="Microsoft.Data.SqlClient.SNI.runtime" Version="7.1.2" ExcludeAssets="all" PrivateAssets="all" />
</ItemGroup>
```

- Without the warnings block the publish fails on `IL2104`/`IL3053`; without the culture block `SqlConnection.Open` throws `NotSupportedException`; without the last block the executable alone fails at the first `Open` with `DllNotFoundException` on Windows.
- Managed networking on Windows is described by SqlClient as meant for testing and debugging, and does not support non-domain Windows authentication. Spar has not tested integrated authentication, named pipes or shared memory with it: test them, or leave the last block out and ship `Microsoft.Data.SqlClient.SNI.dll` beside the executable ([AppContext switches](https://learn.microsoft.com/sql/connect/ado-net/appcontext-switches#enable-managed-networking-on-windows)).
- On Linux SqlClient always uses managed networking: TCP only, Kerberos only for integrated authentication (`libgssapi_krb5.so.2`).

## Measure the size

Size is a budget: measure the published executable after each change that adds a package, a setting or a large page asset, and compare it with the last release. Reference points: the template's app is about 4.7 MB; the Notebook sample about 5.3 MB; culture data adds about 200 KB; the OpenTelemetry layer about 6 MB for an app with no HTTPS stack before.

```powershell
(Get-Item Notes\bin\Production\Release\net11.0\win-x64\publish\Notes.exe).Length
```

```sh
stat -c %s Notes/bin/Production/Release/net11.0/linux-x64/publish/Notes
```

When a jump has no obvious cause, look at what the publish pulled in (a library bringing the HTTP client and TLS is the usual one) before accepting it.

## Traces, metrics and logs with OpenTelemetry

Optional. The `Spar.OpenTelemetry` package exports the backend's traces, metrics and logs over OTLP, and the page's spans through the backend, so one click is one trace from the page's call to the backend's work. Spar's guide "Traces, metrics and logs with OpenTelemetry" has the details.

### Turn it on

Reference the package without a version; it takes the Spar SDK's (with central package management the SDK adds the `PackageVersion` unless the file has one):

<!-- source: samples/Notebook/Notebook.csproj -->
```xml
  <!-- Traces, metrics and logs over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (docs/guide/telemetry.md). -->
  <ItemGroup>
    <PackageReference Include="Spar.OpenTelemetry" />
  </ItemGroup>
```

Call `Telemetry` in `Program.cs`, inside `SparApplication.Create`:

<!-- source: samples/Notebook/Program.cs -->
```csharp
    // Off unless OTEL_EXPORTER_OTLP_ENDPOINT is set, as when the Aspire dashboard starts the app.
    spar.Telemetry();
```

The page needs no code: the build sees the package and gives the page its telemetry module, loaded only when an endpoint is set. `Telemetry` also takes OpenTelemetry's builder for the app's own instrumentation, for example with the `OpenTelemetry.Instrumentation.Http` package:

<!-- source: docs/guide/telemetry.md -->
```csharp
spar.Telemetry(otel => otel
    .WithTracing(tracing => tracing.AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddHttpClientInstrumentation()));
```

### What is exported

- **Backend traces:** a span per command call, named `contract/command` (such as `pages/save`), a child of the page's span of the same call; plus your instrumentation.
- **Backend metrics:** `spar.call.duration`, by contract, command and outcome; plus your meters.
- **Backend logs:** every `ILogger` line of the app and Spar, at the log file's levels, including the page's errors and console messages.
- **Page traces:** a span per call, the document's load, and `traced` spans. Never the calls' requests or results.

### Group an interaction with `traced`

<!-- source: docs/guide/telemetry.md -->
```ts
import { traced } from "@spar/app/sdk";
import { pages } from "@spar/app/pages";

await traced("open page", () => Promise.all([pages.get({ id }), pages.list()]));
```

The page keeps no context across `await`: only calls started before `run`'s first `await` become children. Start the calls first, then await them together. Without telemetry, `traced` just runs `run`.

### Settings

Nothing is exported until `OTEL_EXPORTER_OTLP_ENDPOINT` is set when the app starts; the standard variables apply: `OTEL_EXPORTER_OTLP_PROTOCOL` (`grpc` by default, `http/protobuf`, `http/json`), `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME` (defaults to the app id; the page's is the same with `-page`), `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_TRACES_SAMPLER`, `OTEL_BSP_SCHEDULE_DELAY` ([observability with OpenTelemetry](https://learn.microsoft.com/dotnet/core/diagnostics/observability-with-otel)).

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "http/protobuf"
.\MyApp.exe
```

Spar sets none of them in a published app: whether users' machines export, and where, is the app's decision.

The page never sees the endpoint or its headers. It posts to `/_spar/otlp/v1/traces` on its own origin and the backend forwards the bytes, so a strict content security policy needs nothing added. The relay accepts OTLP traces only (415 otherwise), at most 1 MiB a request (413) and 20 requests a second (429), refuses for 5 s after the endpoint was unreachable (503), and answers 404 without the package or an endpoint.

### Cost

The OpenTelemetry SDK brings .NET's HTTP client and TLS: the Notebook's NativeAOT executable grows from 5.3 MB to 11.3 MB on Windows (less for an app that already makes HTTPS requests). The page module is about 18 KB compressed, loaded after start and only with an endpoint. Measure before and after.

### View it in the Aspire dashboard

Create an app host with Aspire's template (Aspire 13; see [the Aspire documentation](https://aspire.dev/get-started/app-host/)):

```sh
dotnet new aspire-apphost -n MyApp.AppHost
dotnet add MyApp.AppHost reference MyApp
```

In `AppHost.cs`:

<!-- source: docs/guide/telemetry.md -->
```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.MyApp>("myapp", launchProfileName: "Desktop");

builder.Build().Run();
```

`dotnet run --project MyApp.AppHost` starts the dashboard and the app's window with the endpoint set. The services are `myapp` and `myapp-page`. Use `launchProfileName: "Loopback"` to work on the page in a browser; open the address it prints (with `127.0.0.1`, not `localhost`) from the app's console log in the dashboard.

## CI: the template's GitHub Actions workflow

A new app has `.github/workflows/ci.yml`, run on pushes to `main` and on pull requests, once per OS in `SparPlatforms` (`windows-latest`, `ubuntu-latest`). Each job restores, builds, runs the fast tests, the page tests (`npm test`) and the fast tests published with NativeAOT, then the end-to-end tests the runner can run, publishes the app and uploads the test results and the executable.

- **The contract snapshot is checked, never rewritten:**

  <!-- source: src/Spar.Templates/content/spar-app/.github/workflows/ci.yml -->
  ```yaml
  # Deterministic paths, and the committed contract snapshot is checked, never rewritten.
  ContinuousIntegrationBuild: "true"
  ```

  Commit `MyApp/MyApp.contracts.snapshot` after a local build, or CI fails.
- **NativeAOT's build tools** are installed on the Linux runner:

  <!-- source: src/Spar.Templates/content/spar-app/.github/workflows/ci.yml -->
  ```yaml
  - name: NativeAOT's build tools (Linux)
    if: runner.os == 'Linux'
    run: sudo apt-get update && sudo apt-get install -y --no-install-recommends clang zlib1g-dev
  ```

- **The published app is the run's artifact,** without debug symbols:

  <!-- source: src/Spar.Templates/content/spar-app/.github/workflows/ci.yml -->
  ```yaml
  - name: The published app
    uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2
    with:
      name: MyApp-${{ matrix.os }}
      # The executable without its debug symbols, which are not shipped.
      path: |
        MyApp/bin/Production/Release/net11.0/*/publish/
        !MyApp/bin/Production/Release/net11.0/*/publish/*.pdb
        !MyApp/bin/Production/Release/net11.0/*/publish/*.dbg
      if-no-files-found: error
      retention-days: 14
  ```

- **Window and black-box tests run on the Windows runner only.** GitHub's Ubuntu runner refuses the unprivileged user namespaces WebKitGTK's sandbox (`bubblewrap`) needs; run the Linux ones on a machine with a desktop, or a container that allows them.
- **Actions are pinned to full commit SHAs,** with the release in a comment. Update them on purpose, never by tag.
- An app made with `--no-end-to-end-tests` gets the workflow without the end-to-end steps.

Signing, installers and release uploads are not in the workflow; add them as steps after "Publish".

## Before a release

1. `SparAppId` uses a domain you own, and will not change again.
2. `dotnet publish <App> -c Release` on each OS in `SparPlatforms`, into a clean folder.
3. The black-box tests pass against that publication (`spar-testing`).
4. The executable's size is measured and compared with the last release.
5. The build's notices line covers what you expect, or `SparThirdPartyNotices` names your own file.
6. Only the executable (or, on CoreCLR framework-dependent, the folder) ships: no `.pdb` or `.dbg`.
7. Users' prerequisites are documented: the WebView2 Runtime on Windows; GTK 4, WebKitGTK 6.0 (and ICU with culture data) on Linux; .NET 11 for a framework-dependent publication.

# How a Spar app is written

This design record shows, with complete examples, the code a Spar app author writes and the code Spar generates for it. It collects in one place what `docs/decisions.md` and `docs/plan.md` decided about contracts, errors, streams, the generated client and the app's configuration. It is the reference for M1 and the starting point for the user guides.

**Status of each part.** Every section is marked:
- **Decided:** a user decision recorded in `docs/decisions.md`;
- **Leaning:** the current recommendation, waiting for the user's confirmation;
- **Open:** the names or shapes are placeholders until the decision is taken.

Where a decision changes, update this document in the same change.

**Checked by compilation.** The C# and TypeScript in this document must compile, against stand-ins for the Spar API, in `tests/AppModel/`, which `dotnet test` checks. Each block names the file it corresponds to there. The C# blocks that Spar can already build are also built by real Spar, with its analyzer and generator (`tests/AppModel/Real`); the contracts with streams, subscriptions or file grants join that build when Spar supports them. The stand-ins have no contract analyzer, so a script there (`tools/check-contracts.mjs`) checks what compiling cannot: the request shape, the return forms, reserved contract names and the `cancelled` error code.

The example is a small part of Spar's first consumer, the SQL Server tool: saved connections, and copy jobs that plan, preview and run a copy and report progress.

## 1. Project layout

**Decided** (decisions "User project layout" and "Client wiring, test projects, and the spar tool commands").

```text
SqlTool/
  SqlTool.csproj            <Project Sdk="Spar.Sdk">, with <SparAppId>com.example.SqlTool</SparAppId>
                            and <SparPlatforms>windows;linux</SparPlatforms>
  Program.cs
  Properties/
    launchSettings.json     the launch profiles: Desktop (the default, first) and Loopback
  Connections/              one folder per feature
  CopyJobs/
  frontend/                 an ordinary Vite + React + TypeScript project
    package.json            "@spar/app": "file:../obj/spar/client"
    vite.config.ts          uses the Spar Vite plugin from "@spar/app/vite"
    index.html
    src/
  SqlTool.contracts.snapshot   committed; written by the build (decided 2026-11-14)
SqlTool.Tests/              fast tests, run by every dotnet test
SqlTool.EndToEndTests/      optional: browser and window tests, the black-box executable, containers
SqlTool.slnx                the solution, with the projects above
global.json                 pins the SDK and Spar.Sdk (msbuild-sdks)
.config/dotnet-tools.json   the local tool manifest: the spar tool (Spar.Tool) at Spar.Sdk's version
```

- `dotnet new spar-app -n SqlTool` writes this layout from the `Spar.Templates` package (plan, M1d step 6), with one contract and its tests as a start, `global.json` naming the template's own Spar version, a local tool manifest naming the `spar` tool at the same version (`dotnet tool restore`, then `dotnet spar doctor` and `dotnet spar init frontend`), and a `.gitignore`; `--platforms`, `--app-id` and `--no-end-to-end-tests` choose `SparPlatforms`, `SparAppId` and whether the end-to-end project is made. [The getting-started guide](guide/getting-started.md) goes from an empty machine to a published app.

- The app id, `SparAppId`, is required: reverse-DNS, checked by the build. It names the app to the OS and its per-user folders: `%LOCALAPPDATA%\com.example.SqlTool` on Windows, the XDG folders on Linux, and `com.example.SqlTool.Development` for a Debug build, so Debug never shares data with an installed copy (decision "M1b's remaining questions without a spike").
- The target hosts come from `SparPlatforms` (both by default); `Spar.Sdk` adds the host packages itself.
- The frontend is found through `SparFrontendPath`, by default `frontend/`.
- **A Debug build serves the page from the frontend's Vite development server,** through the app's own origin, so hot reload works with the policy, storage and secure context of a Release build (decisions "Debug serves Vite through the app's own origin" and "M1d step 1's shape"). The app starts it with the frontend's own `dev` script (`npm run dev`, which runs `vite`, with the Spar Vite plugin in `vite.config.ts`), or reuses the one it started before, and leaves it running for the next start; Vite's output goes to the app's log under `Spar.DevServer`. If it fails or stops, the window shows what happened with Vite's last output and a Retry button, and the app keeps running. Without Node.js, the app stops with a message saying how to install it.
  - `SPAR_DEV_SERVER_URL`, in the environment, or `<SparDevServerUrl>`, in the project (read when the app is built), names a development server the app did not start, such as one an orchestrator or the developer runs: the app uses it and starts none. It must be `http://` on this machine (`127.0.0.1`, `[::1]` or `localhost`) with its port, such as `http://127.0.0.1:5173`; the variable wins over the property.
  - `-p:SparDevServer=false` embeds the page in a Debug build through a Release build's code, without hot reload.
- **A Debug build runs in its window, or in a browser** (decisions "M1d step 2's shape: loopback in the development loop" and "Loopback in the development loop (implementation record; choices for review)"). `dotnet run` uses the first launch profile, `Desktop`, the window; `dotnet run -lp Loopback` sets `SPAR_HOST=Loopback`, and the app serves its page on `127.0.0.1` to an ordinary browser, with the same backend, page security and development server, and no window. The app's own command-line arguments stay its own. A Release build has no loopback host and ignores `SPAR_HOST`. If the window cannot start (on Linux, no display), the app stops with a message that names `dotnet run -lp Loopback`; it never switches by itself.

  ```json
  {
    "profiles": {
      "Desktop": { "commandName": "Project" },
      "Loopback": { "commandName": "Project", "environmentVariables": { "SPAR_HOST": "Loopback" } }
    }
  }
  ```

  - The app prints the page's address with its token, `http://127.0.0.1:<port>/#token=…`, and logs it. The first run of a dev session opens the default browser there; later runs keep the port and the token, so a restart does not open another tab: the open page waits for the backend and reloads by itself, once. A call that was running when the backend stopped ends as `transport` / `lost`, and nothing is sent again.
  - The port and the token are recorded in the project's `obj/spar-loopback.json`, readable by its owner only. A dev session ends when no run has held that record for 4 hours; the next run then takes a new token and opens the browser again, and a page left open from before is told to open the new address.
  - `SPAR_LOOPBACK_PORT` and `SPAR_LOOPBACK_TOKEN` (base64url of 16 to 48 bytes) set the address for an orchestrator: no record, and no browser. `SPAR_LOOPBACK_BROWSER` names the program that opens the page (`browser --profile dev {url}`), or `none`.
- The generated client lives under `obj/spar/client/` and is never written into `node_modules`.
- Each contract is a subpath of `@spar/app`: `@spar/app/copyJobs`, `@spar/app/connections`. The SDK is `@spar/app/sdk`.
- A test project names the app it tests with `SparTestedApplication` (decision "SparTestedApplication: the test project names its app"), so the app builds in the Test variant, with the test host and the typed test clients. A plain `ProjectReference` to the app fails the build (SPAR121); other projects, such as a library, are referenced as usual. An end-to-end project also sets `<SparBrowserTests>true</SparBrowserTests>` for the Browser surface.

  ```xml
  <Project Sdk="Spar.Sdk">
    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <TargetFramework>net11.0</TargetFramework>
      <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
      <SparTestedApplication Include="..\SqlTool\SqlTool.csproj" />
    </ItemGroup>
  </Project>
  ```

- **What a test project gets** (decision "M1d step 4's shape: tests in the template"; built in M1d step 4). `dotnet test` writes its results under the test project's own `obj/test-results/` (`<SparTestResultsDirectory>` names another folder), and leaves no `TestResults/` folder behind. A new app's two test projects, from the template, also have `[assembly: CaptureConsole]`, so what the code writes to the console lands in each test's own output, and treat warnings as errors, which makes xUnit's xUnit1051 (a call that could take `TestContext.Current.CancellationToken` and does not) an error. Browser, window and black-box tests carry a `Surface` trait (`Browser`, `Window`, `BlackBox`) and run as their own invocations, `dotnet test --filter-trait "Surface=Window"` and the others, apart from the fast tests; Spar checks the first two (6.12). Page tests run with `npm test` in `frontend/`; `dotnet test` runs only the C# tests.

## 2. `Program.cs`

**Decided** (user, 2026-11-15; decision "`Program.cs` shape confirmed"). `Program.cs` holds exactly one `SparApplication.Create` and one `Run`, directly in the top-level statements; anything else is a diagnostic (plan section 2, "`Program.cs`").

- **Read by the build, so constants only:** the declarations the build needs sit directly in the `Create` configuration with constant values, so the build reads them without running code. They are:
  - the CSP (plan 3b);
  - the permissions (plan 3b);
  - the window kinds, whose names become a TypeScript union in `@spar/app/windows` (6.7);
  - the window colours (plan 3d).
- **Run at start-up, so ordinary values are fine:** every other configuration call runs when the app starts and may use run-time values. The example does so with `spar.Folders(f => f.Logs(Path.Combine(AppContext.BaseDirectory, "logs")))` and with the tray menu's lambda. `spar.ResolveFolders()` gives the configuration the folders the app's services will get, for what it builds itself (3.13).

File `SqlTool/Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar;
using SqlTool.Connections;
using SqlTool.CopyJobs;
using SqlTool.Monitoring;
using SqlTool.Reports;
using SqlTool.Settings;
using SqlTool.Support;

var app = SparApplication.Create(args, spar =>
{
    // Window colours before the page exists, in both themes (plan 3d), and a title bar the page draws (6.9). Decided.
    spar.Window(w => w
        .Background(light: "#ffffff", dark: "#1f1f1f")
        .TitleBar(TitleBar.Custom)
        .Size(1200, 800)
        .MinSize(800, 600)
        .RememberPlacement()
        .CloseToTray());

    // A tray icon for the monitoring the tool keeps running (3.5). Decided.
    spar.Tray(t => t
        .Icon("assets/tray.png")
        .Tooltip("SQL Tool")
        .OnActivate(TrayAction.ShowWindow)
        .Menu(m => m
            .Item("Open", TrayAction.ShowWindow)
            .Item<IMonitor>("pause", "Pause monitoring", monitor => monitor.TogglePause())
            .Separator()
            .Item("Quit", TrayAction.Quit)));

    // The strict CSP is the default; each addition is explicit (plan 3b). A directive method takes several sources and
    // may be called again: Spar sends each directive once, its sources together. Violations are reported in every
    // build by default; here report-uri, the backstop, reports only in Development and Test builds. Decided.
    spar.ContentSecurity(csp => csp
        .FontSrc("https://fonts.gstatic.com", "https://fonts.example-cdn.com")
        .FontSrc("https://fonts.gstatic.com")
        .ImgSrc("data:")
        .Reporting(listener: CspReports.Always, endpoint: CspReports.DevelopmentOnly));

    // Powerful features are denied unless declared (plan 3b). Decided.
    spar.Permissions(p => p.Allow(Permission.ClipboardRead));

    // File types the app may open with their default program; nothing opens by default (6.6). Decided.
    spar.Shell(s => s.AllowOpening(".csv", ".pdf"));

    // Where data, logs and crash records go; each has a per-user default (3.13). Decided.
    // Here logs sit next to the executable, as in a portable install.
    spar.Folders(f => f.Logs(Path.Combine(AppContext.BaseDirectory, "logs")));

    // Deployment choices beside the executable, in SqlTool.spar.json: an installation may move the folders and the
    // settings file, or keep the browser's data in memory, without a new build; never security (3.19). Built in M3b.
    spar.InstallationFile();

    // The folders the services will get, resolved now for what Program.cs builds itself: the hang record goes in the
    // logs folder chosen above without repeating its path (3.13). spar.Folders(…) throws after this call. Built; the
    // shape is for review (decision "Configuration stays the app's choice").
    var folders = spar.ResolveFolders();

    // Window kinds the page opens by name (6.7). Decided.
    spar.Window("settings", w => w
        .Route("/settings")
        .Size(640, 520)
        .Modal()
        .SingleInstance()
        .ProtectFromCapture()
        .Commands(c => c.Allow<IConnections>()));
    spar.Window("queryEditor", w => w.Route("/query").Size(1000, 700).Zoom(min: 0.5, max: 3).FindInPage());

    // Closing asks first: the page's guard, then this one, which asks while a copy runs (6.11). Decided.
    spar.OnCloseRequested((request, services) => services.GetRequiredService<RunningCopies>().Any
        ? CloseDecision.Ask("A copy is still running. Close anyway?")
        : CloseDecision.Close);

    // A second launch shows and focuses the main window, then hands its arguments here (3.14). Decided.
    spar.OnRelaunch((relaunch, services) =>
    {
        if (relaunch.Arguments is ["--recipe", var file])
        {
            services.GetRequiredService<RecipeQueue>().TryAdd(relaunch.ResolvePath(file));
        }
    });

    // A missing prerequisite opens the company's install guide in place of Spar's message (3.15). Decided.
    spar.OnStartupProblem(SupportPage.TryOpen);

    // A page that stops responding is recorded for support; Spar's own dialog still asks the user (3.16). Decided.
    spar.OnPageUnresponsive((hang, services, cancellationToken) =>
        services.GetRequiredService<HangLog>().RecordAsync(hang, cancellationToken));

    // The UI-thread stall probe stays in this Production build, logging only, for users' machines (3.13). Decided.
    spar.Diagnostics(d => d.KeepStallProbe());

    // The log file: Information and above, and Debug for the copy jobs; a logging.json in the per-user folder overrides
    // these on a user's machine without a new build (3.13). Decided.
    spar.Logging(l => l.Level(LogLevel.Information).Level("SqlTool.CopyJobs", LogLevel.Debug));

    // What the tool remembers between runs, in settings.json in the data folder (3.19). Built in M3b.
    spar.Settings(new ToolSettings(BatchSize: 50_000, PreviewRows: 200, LastRecipe: null));

    // Traces, metrics and logs over OTLP, the page's spans with them, when OTEL_EXPORTER_OTLP_ENDPOINT is set; nothing
    // otherwise (3.18). Built in M2d.
    spar.Telemetry();

    // The browser keeps its strict defaults; only the support build the team sends to one user adds devtools (3.17).
    // Decided. On Linux it also draws without the GPU, which rules a broken graphics driver out (3.17). Built; this use
    // of the option is for review.
    if (SupportBuild.IsSupportBuild)
    {
        spar.Browser(b => b
            .AllowDevTools()
            .HardwareAcceleration(HardwareAcceleration.Never));
    }

    // Services: Microsoft DI, one scope per command call.
    spar.Services.AddSingleton<IConnectionStore, FileConnectionStore>();
    spar.Services.AddScoped<IConnections, ConnectionsApi>();
    spar.Services.AddSingleton<IMonitor, InstanceMonitor>();
    spar.Services.AddSingleton<RunningCopies>();
    spar.Services.AddSingleton<RecipeQueue>();
    spar.Services.AddSingleton(new HangLog(folders.Logs));
    spar.Services.AddSingleton<ICopyHistory, CopyHistory>();
    spar.Services.AddSingleton<IActivityCharts, ActivityCharts>();
});

return app.Run();
```

- **Class contracts** are registered by the generated code.
- **Interface contracts** need their implementation registered, as `IConnections` above, whether the app declares them or a library it lists does (3.4). An app that registers none does not start: the error names the contract and its interface.
- **Overrides of security defaults** (plan 3c) go in the same configuration: the page's in `ContentSecurity` and `Permissions` below, the browser's in `Browser` (3.17). Spar logs each one at startup, and `spar doctor` lists them.
- **Page security** (plan 3b; decision "Page security's public shape"; built in M1b):
  - `ContentSecurity` and `Permissions` are read by the build: each a statement of its own directly in `Create`, with constant values. Anything else is an error at its place: SPAR101 for a call elsewhere (a condition, a helper method), SPAR102 for a value that is not a constant, SPAR103 for a shape the build cannot read (a delegate variable, other statements in the lambda), SPAR104 for a source the policy refuses.
  - The directive methods (`FontSrc`, `ImgSrc`, `ConnectSrc`, `MediaSrc`, `ScriptSrc`, `StyleSrc`, `FrameSrc`) take hosts and schemes only, and `FrameSrc` also `'self'`. A keyword or a nonce in them is SPAR104, whose message names the method that gives it: `AllowInlineStyles()`, `AllowEval()`, `AllowWebAssembly()` or `StyleNonce()`. Each of those, and `WithoutTrustedTypes()` and `WithoutIntegrity()`, documents its risk, when it is reasonable and the safer alternative.
  - The policy above is sent as `font-src 'self' https://fonts.gstatic.com https://fonts.example-cdn.com` and `img-src 'self' data:`, with the rest of the strict default; duplicates are dropped. With `AllowInlineStyles()`, `style-src` gets no nonce or hash, which browsers would let override `'unsafe-inline'`, so `StyleNonce()` beside it is the SPAR105 warning.
  - `Reporting(listener, endpoint)` sets each channel to `Always` (the default), `DevelopmentOnly` or `Off`: the listener is the host's script in each document, `endpoint` the `report-uri` backstop. Violations are logged grouped and with flood limits; in Development each new one says what C# would allow it.
  - Under Trusted Types (on unless `WithoutTrustedTypes()`), `new Worker(url)` is a script sink: pass the worker's address through a policy, `trustedTypes.createPolicy("worker", { createScriptURL: url => url }).createScriptURL(address)`. Styles added from script use constructed stylesheets or `StyleNonce()`.
  - `Permissions` declares the features the page may use (`Permission.Camera`, `Microphone`, `Geolocation`, `ClipboardRead`, `Sensors`, `LocalFonts`, `Midi`, `WindowManagement`, `Autoplay`). A declared feature is granted when the page asks, with no prompt; everything else is denied, also in the `Permissions-Policy` header. The browser never remembers an answer. On Linux, where WebKitGTK ignores the header, the host's answer to each request is the only gate, with the same rule.

## 3. Contracts

**Decided** (decisions "Contract syntax", "Contract syntax follow-ups", "Contract rules approved", "Derived contract names", "Cancellation token delivery").

### 3.1 Requests, results and errors

Requests and results are positional records. Errors are C# 15 `closed record` hierarchies with nested sealed cases; a `switch` over them is exhaustive without a default arm. A missing case is CS8509, which is only a warning unless the project treats warnings as errors. **Decided:** `Spar.Sdk` leaves CS8509 to the app. Spar's own rule SPAR055 (an error) reports a `switch` expression over a `Result`, or over a contract's `closed` error hierarchy, that misses a case, so it stays an error in apps without `TreatWarningsAsErrors` (decision "CS8509 stays the app's setting; a Spar rule for `Result` switches").

File `SqlTool/CopyJobs/CopyJobTypes.cs`:

```csharp
namespace SqlTool.CopyJobs;

public sealed record CopyRecipe(string Source, string Target, IReadOnlyList<string> Tables);

public sealed record CopyPlan(Guid PlanId, IReadOnlyList<PlanStep> Steps);

public sealed record PlanStep(string Table, long EstimatedRows);

public sealed record RunCopy(Guid PlanId, bool TruncateTarget);

public sealed record CopyReport(int Tables, long Rows, TimeSpan Elapsed);

public sealed record PreviewRow(IReadOnlyList<string?> Cells);

public sealed record CopyProgress(string JobId, long Rows);

/// <summary>Why a copy could not be planned.</summary>
public closed record PlanError
{
    /// <summary>The named connection is not configured.</summary>
    public sealed record ConnectionNotFound(string ConnectionId) : PlanError;

    /// <summary>The table does not exist on that connection.</summary>
    public sealed record TableMissing(string Connection, string Table) : PlanError;
}

/// <summary>Why a planned copy did not run to the end.</summary>
public closed record RunError
{
    public sealed record PlanOutdated(string Reason) : RunError;

    public sealed record TargetNotEmpty(string Table, long Rows) : RunError;

    /// <summary>Rows committed before the failure stay in the target.</summary>
    public sealed record PartiallyCopied(long RowsCommitted, string Reason) : RunError;
}
```

- Wire types: positional records, primitives, nullable values, arrays and `IReadOnlyList<T>`, nested records, `closed record` errors. The wire rules for `long`, `decimal`, dates, `Guid`, `byte[]`, enums and strings are in plan section 2. All of them are decided (decision "Wire format choices").
- A case cannot be shared by two error types; it is declared in each.
- Two different types with the same name inside one contract need `[SparName("…")]`.
- **The `Result` container** (Decided 2026-11-14) is Spar's own type; authors never declare it. It is a `readonly struct` declared as a C# 15 union, so a successful result allocates nothing.
  - **Reading in C#:** one exhaustive `switch` over the result, covering the success type and every error case, for example in a test: `await service.PlanAsync(recipe, ct) switch { CopyPlan p => …, PlanError.TableMissing e => … }`. Leaving out a case is CS8509 and SPAR055.
  - **Other members:** `IsSuccess`, `IsError`, `TryGetValue`, `TryGetError`. `Value` and `Error` throw if the result holds the other state, with that state in the message.
  - **`default(Result<…>)`** is neither a value nor an error, and every member throws on it. Spar's analyzers report the ways a default result can arise (rules SPAR050–SPAR054, plan section 2; decision "Analyzer rules SPAR050–SPAR054 adopted"), for example `default`, `new()`, an array created without values, `FirstOrDefault`, or a field some constructor leaves unassigned. A member that may legitimately be empty is declared `Result<…>?`.
  - **Commands with errors but no value** return `Result<TError>`, writing `return Result.Success;`. On the wire this is a `result` with `"ok":true` and no payload (section 4).

### 3.2 A class contract

One instance per call, created from the call's DI scope. Only methods marked `[SparCommand]` are commands, so other public methods stay C#-only.

File `SqlTool/CopyJobs/CopyJobsService.cs`:

```csharp
using System.Runtime.CompilerServices;
using Spar;
using SqlTool.Connections;

namespace SqlTool.CopyJobs;

/// <summary>Plans, previews and runs table copies. Wire name: copyJobs.</summary>
[SparContract]
public sealed class CopyJobsService(IConnectionStore connections, ISparCall call)
{
    /// <summary>Checks the recipe against both connections and estimates the work.</summary>
    [SparCommand]
    public async Task<Result<CopyPlan, PlanError>> PlanAsync(CopyRecipe recipe, CancellationToken cancellationToken)
    {
        if (!await connections.ExistsAsync(recipe.Source, cancellationToken))
        {
            return new PlanError.ConnectionNotFound(recipe.Source);
        }

        var steps = new List<PlanStep>();
        foreach (var table in recipe.Tables)
        {
            var rows = await connections.CountRowsAsync(recipe.Source, table, cancellationToken);
            if (rows is null)
            {
                return new PlanError.TableMissing(recipe.Source, table);
            }

            steps.Add(new PlanStep(table, rows.Value));
        }

        return new CopyPlan(Guid.CreateVersion7(), steps);
    }

    /// <summary>
    /// Copies the planned tables. Cancelling stops between batches. Copies run one at a time, whichever window starts
    /// them: a second waits its turn without holding a call slot (3.20).
    /// </summary>
    [SparCommand]
    [SparSerial("copies")]
    public async Task<Result<CopyReport, RunError>> RunAsync(RunCopy request, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
        return new RunError.PlanOutdated($"Plan {request.PlanId} was made before the last schema change (call {call.CallId}).");
    }

    /// <summary>The first rows of each table, streamed as they are read.</summary>
    [SparCommand]
    public async IAsyncEnumerable<PreviewRow> PreviewAsync(CopyRecipe recipe, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var table in recipe.Tables)
        {
            await Task.Delay(1, cancellationToken);
            yield return new PreviewRow([table, null]);
        }
    }

    /// <summary>Live progress of a running copy. Observation only, so it may resume after a lost connection.</summary>
    [SparCommand]
    [SparSubscription(Capacity = 256, Resume = SubscriptionResume.Automatic)]
    public async IAsyncEnumerable<CopyProgress> ProgressAsync(string jobId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long rows = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(250, cancellationToken);
            rows += 1000;
            yield return new CopyProgress(jobId, rows);
        }
    }

    /// <summary>Plain Task: success carries no value.</summary>
    [SparCommand]
    public Task CancelAllAsync() => Task.CompletedTask;

    /// <summary>Not a command: no [SparCommand].</summary>
    public static string Describe(CopyRecipe recipe) => $"{recipe.Source} -> {recipe.Target}";
}
```

- **Command shape:** zero or one request (a positional record or a scalar such as `string jobId`), then an optional `CancellationToken`. A list or an array is not a request: it goes inside a record, as `ExportRequest` in 3.6.
- **Returns:** `Task`, `Task<T>`, `Task<Result<T, TError>>`, `Task<Result<TError>>`, their `ValueTask` forms, `IAsyncEnumerable<T>`, `IAsyncEnumerable<Result<T, TError>>` (a stream with declared errors; yielding an error ends it, and the page's `match` gets an `errors` object as for unary calls), or a subscription.
- **Conversions:** `return plan;` and `return new PlanError.TableMissing(…);` both convert into the `Result` implicitly. These are union conversions, so they also work for values typed as an interface, such as `IReadOnlyList<T>`.
- **The per-call token fires** when the page aborts the call, when the page that started it goes away, or when the app shuts down. A command that ignores the token ends as "outcome unknown" when cancelled, never as "cancelled".
- **`ISparCall`** gives the token, the call id, the names and the caller (`SparCaller.Page`, `Test` or `Devtools`) to the contract and to any scoped service of the call. A singleton cannot depend on it (`ValidateScopes`).
- **Wire types (decided 2026-11-14):**
  - `long` is a decimal string on the wire and `bigint` in TypeScript.
  - `TimeSpan` is a string in the constant format `[-][d.]hh:mm:ss[.fffffff]`, so `elapsed` below is a `string`.

### 3.3 An interface contract

Every method of a `[SparContract]` interface is a command; `[SparCommand]` there only overrides a name. The implementation comes from DI and exposes only the interface's members.

ReSharper and Rider count a contract class and its commands as used, since the page calls them. An interface's methods count as used only with `[SparCommand]`; without it, those tools report each method as never used until C# code calls it.

File `SqlTool/Connections/IConnections.cs`:

```csharp
using Spar;

namespace SqlTool.Connections;

public sealed record ConnectionInfo(string Id, string Server, string Database, bool IntegratedSecurity);

public closed record ConnectionError
{
    public sealed record NotFound(string ConnectionId) : ConnectionError;

    public sealed record LoginFailed(string Server, int SqlNumber) : ConnectionError;
}

/// <summary>Saved connections. Wire name: connections.</summary>
[SparContract]
public interface IConnections
{
    Task<IReadOnlyList<ConnectionInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Opens and closes a connection to check that it works.</summary>
    Task<Result<ConnectionInfo, ConnectionError>> TestAsync(string connectionId, CancellationToken cancellationToken);

    [SparCommand("forget")]
    Task RemoveAsync(string connectionId);
}
```

### 3.4 Names

| C# | Wire and TypeScript |
|---|---|
| class `CopyJobsService` | `copyJobs` |
| interface `IConnections` | `connections` |
| `IOStatsService`, `SQLServerMonitorService` | `ioStats`, `sqlServerMonitor` |
| method `PlanAsync` | `plan` |
| `[SparCommand("forget")] RemoveAsync` | `forget` |
| error case `PlanError.TableMissing` | code `tableMissing` |

- Classes keep their name, interfaces drop one leading `I`, and both drop a trailing `Service`. Then the name is put in acronym-aware camelCase.
- Names follow ASCII camelCase: an explicit name with another character, or a class whose derived name has one (`Copy_JobsService`), is an error that asks for an explicit name.
- Two contracts with the same name are an error, compared ignoring case, including contracts from the libraries the app serves (below).
- A contract declared in a library reaches the page only when the app lists the library, by assembly name, in its project file. Nothing else is needed: the app's build generates the library's dispatch, serializers and TypeScript module, and registers its class contracts. The library's interface contracts are implemented by the app, which registers the implementation as for its own. Only public contracts can be served. The library may be a project, as below, or a NuGet package, listed the same way beside its `PackageReference`.

  ```xml
  <ItemGroup>
    <ProjectReference Include="..\SqlTool.Contracts\SqlTool.Contracts.csproj" />
    <SparContracts Include="SqlTool.Contracts" />
  </ItemGroup>
  ```

  A referenced library that is not listed adds nothing to what the page can call, even when it declares contracts. The library's documentation comments become its module's JSDoc when the library writes its XML documentation file (`<GenerateDocumentationFile>true</GenerateDocumentationFile>` in its project), as the app's own comments do.
- A contract name cannot be a JavaScript reserved word or a subpath that `@spar/app` uses itself, such as `sdk`, `vite` or `windows` (plan section 2, "Names"). An `ExportService` would be named `export`, so the export contract in 3.6 is `CsvExportService`, named `csvExport`.

### 3.5 Native services: the tray

**Decided** (2026-11-14). The tray is declared in `Program.cs` (section 2). App services change it at run time through the injectable `ITray`. The page reaches the tray only through the app's own contracts.

File `SqlTool/Monitoring/InstanceMonitor.cs`:

```csharp
using Spar;

namespace SqlTool.Monitoring;

public interface IMonitor
{
    void TogglePause();
}

/// <summary>Polls the monitored instances; the tray item "Pause monitoring" toggles it.</summary>
public sealed class InstanceMonitor(ITray tray) : IMonitor
{
    private bool _paused;

    public void TogglePause()
    {
        _paused = !_paused;
        tray.SetChecked("pause", _paused);
        tray.SetTooltip(_paused ? "SQL Tool: monitoring paused" : "SQL Tool");
    }
}
```

- **Window placement** (decided 2026-11-14): `Size` sets the first start, centred. `RememberPlacement()`, the default for the main window, saves size, position, maximized state and screen in the app's data folder and restores them on a visible screen; Wayland restores size and maximized state only. App services change windows through the injectable `IWindows`.
- **Built-in actions:** `ShowWindow` and `Quit` need no app code, and `Quit` goes through the normal shutdown.
- **Items with a handler** get a service from the app's container, and an id that `ITray` uses to change them later.
- **Linux safety:**
  - Some Linux hosts send no clicks, so a diagnostic warns when the `OnActivate` action is not also in the menu.
  - Where no tray exists (`ITray.IsAvailable` is false), `CloseToTray` minimises instead of hiding, so the window stays reachable.

### 3.6 Native services: file dialogs

**Decided** (2026-11-14). Opening a file returns a grant or a declared error, so the compiler makes the caller handle every case. Reads and writes have their own error set, because they can fail after a successful pick.

File `SqlTool/Recipes/RecipesService.cs`:

```csharp
using Spar;

namespace SqlTool.Recipes;

public sealed record RecipeInfo(string Name, int Tables);

/// <summary>Why a recipe could not be imported.</summary>
public closed record ImportError
{
    public sealed record Dismissed : ImportError;

    public sealed record NotAllowed(string Path) : ImportError;

    public sealed record NoFileDialog(string Reason) : ImportError;

    public sealed record TryAgain : ImportError;

    public sealed record Unreadable(string File) : ImportError;
}

[SparContract]
public sealed class RecipesService(IFileDialogs dialogs)
{
    /// <summary>Lets the user pick a recipe file and imports it.</summary>
    [SparCommand]
    public async Task<Result<RecipeInfo, ImportError>> ImportAsync(CancellationToken cancellationToken)
    {
        var picked = await dialogs.OpenAsync(new() { Title = "Import recipe", Filters = [new("Recipes", "*.sparrecipe")] }, cancellationToken);
        return picked switch
        {
            FileGrant file => Read(file),
            FileDialogError.Dismissed => new ImportError.Dismissed(),
            FileDialogError.AccessDenied e => new ImportError.NotAllowed(e.Path),
            FileDialogError.Unavailable e => new ImportError.NoFileDialog(e.Reason),
            FileDialogError.Busy => new ImportError.TryAgain(),
        };
    }

    private static Result<RecipeInfo, ImportError> Read(FileGrant file) => file.OpenRead() switch
    {
        Stream stream => new RecipeInfo(file.Name, Tables: CountTables(stream)),
        FileAccessError => new ImportError.Unreadable(file.Name),
    };

    private static int CountTables(Stream stream)
    {
        using (stream)
        {
            return 0;
        }
    }
}
```

A folder works the same way. The grant declares read or read-write access, and every path is relative to the chosen folder. The table names arrive in a request record, `ExportRequest`, because a request is one positional record or one scalar, never a bare list (3.2).

File `SqlTool/Recipes/CsvExportService.cs`:

```csharp
using Spar;

namespace SqlTool.Recipes;

public sealed record ExportRequest(IReadOnlyList<string> Tables);

/// <summary>Why an export did not finish.</summary>
public closed record ExportError
{
    public sealed record Dismissed : ExportError;

    public sealed record NoFolder(string Reason) : ExportError;

    public sealed record WriteFailed(string File) : ExportError;
}

/// <summary>Exports tables as CSV files. Wire name: csvExport.</summary>
[SparContract]
public sealed class CsvExportService(IFileDialogs dialogs)
{
    /// <summary>Writes one CSV per table into a folder the user picks.</summary>
    [SparCommand]
    public async Task<Result<int, ExportError>> ExportAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        var picked = await dialogs.OpenFolderAsync(new() { Title = "Choose export folder", Access = FolderAccess.ReadWrite }, cancellationToken);
        return picked switch
        {
            FolderGrant folder => await WriteAsync(folder, request.Tables, cancellationToken),
            FileDialogError.Dismissed => new ExportError.Dismissed(),
            FileDialogError.AccessDenied e => new ExportError.NoFolder(e.Path),
            FileDialogError.Unavailable e => new ExportError.NoFolder(e.Reason),
            FileDialogError.Busy => new ExportError.NoFolder("busy"),
        };
    }

    private static async Task<Result<int, ExportError>> WriteAsync(FolderGrant folder, IReadOnlyList<string> tables, CancellationToken cancellationToken)
    {
        foreach (var table in tables)
        {
            // Relative to the chosen folder; "..", absolute paths and links leaving it are refused (OutsideFolder).
            if (await folder.WriteTextAsync($"{table}.csv", "", cancellationToken) is FileAccessError)
            {
                return new ExportError.WriteFailed($"{table}.csv");
            }
        }

        return tables.Count;
    }
}
```

On the page, `files.open()` and `files.openFolder()` are normal `Call`s. The page sees only the file's name; a command can also take the grant as a `FileGrant` parameter and read it in C#.

File `frontend/src/notes.ts`:

```ts
import { files } from "@spar/app/sdk";

declare function toast(text: string): void;

export async function openNotes(show: (text: string) => void): Promise<void> {
  await files.open({ title: "Open notes", filters: [{ name: "Text", extensions: ["txt", "md"] }] }).match({
    success: async file => show(await file.readText().unwrap()),
    errors: {
      dismissed: () => {},
      accessDenied: e => toast(`No access to ${e.path}`),
      unavailable: e => toast(e.reason),
      busy: () => {},
    },
  });
}

export async function listFolder(show: (names: readonly string[]) => void): Promise<void> {
  await files.openFolder({ title: "Choose a folder", access: "read" }).match({
    success: async folder => show((await folder.list().unwrap()).map(entry => entry.name)),
    errors: { dismissed: () => {}, accessDenied: e => toast(e.path), unavailable: e => toast(e.reason), busy: () => {} },
  });
}
```

Dropping files from the OS works only on areas the page declares. Each dropped file becomes the same grant a dialog gives.

File `frontend/src/drop.ts`:

```ts
import { files, type PickedFile } from "@spar/app/sdk";

declare function toast(text: string): void;

export function acceptCsvDrops(area: Element, importFiles: (dropped: readonly PickedFile[]) => void): () => void {
  // The returned function removes the drop area, for example in a React effect's cleanup.
  return files.dropTarget(area, { accept: [".csv"], folders: false, limit: 10 }, result =>
    result.match({
      success: dropped => importFiles(dropped.files),
      errors: {
        notAccepted: e => toast(`${e.name} is not a CSV file`),
        tooMany: e => toast(`At most ${e.limit} files at a time`),
      },
    }));
}
```

- **Drag and drop** (decided 2026-11-14; the host side from the spike, decision "The drop design, from the file-drop spike"; built in M3c):
  - Off by default: only an area the page declares takes files, and a drop anywhere else stays refused by the navigation lock.
  - While a drag of files hovers the area, the SDK sets `data-spar-drop="over"` on it, so CSS can highlight it.
  - Page-internal drag and drop, such as sortable lists, is unaffected.
  - The handler gets the dropped files as grants, never `File` objects or paths. On Windows the SDK hands the drop's files to the host with the drop message, and the browser vouches that each is a file the user gave the page; on Linux the host reads the drag's files itself and grants them only after a real (trusted) drop. WebKitGTK gives the page no `File`s for a drop at all, so grants are the one way on both hosts.
- **The dialog's cases:** `dismissed` (not `cancelled`, which is the `CallError` of an aborted call), `accessDenied`, `unavailable` and `busy`. The app's own error types above follow the same rule: their case for a closed dialog is `Dismissed`. On Linux the dialog goes through the desktop's file-chooser portal; without one it is `unavailable`, with the reason, and no fallback dialog is shown.
- **Read and write cases:** `notFound`, `accessDenied`, `inUse`, `tooLarge`, `grantExpired`, `failed` (any other file-system failure, such as a full disk), and for folders `outsideFolder` and `readOnlyGrant`.
- **Folder grants** declare `Read` (the default) or `ReadWrite`. They offer list, read, safe write, create folder and delete (write access only). The host checks the final resolved path at each use, so links leaving the folder are refused. Remembering a folder across restarts is not in the first version.
- **Grants** are `{"id","name","size","access"}` on the wire (no `size` for a folder), ids of 128 random bits. A command's `FileGrant` or `FolderGrant` parameter is resolved to the window's own grant before the command runs; an id the window does not hold is refused as `rejected` / `invalid-request`. A grant belongs to the window: it outlives a reload of the page, is revoked when the window closes, and a window holds at most 1,000, past which a new pick is `busy`; `Expiry` in the options gives a shorter life. C# reads `grant.Path`; no grant reaches the browser's profile folder.
- **The page reads and writes** a grant within the bridge's 4 MiB message (`readText`, `readBytes`, `writeText`, `writeBytes`, `tooLarge` above); `blob()` gives the file as a handle on the app's own origin for any size (3.21); a larger write goes through a command that takes the grant.
- **Saves** follow the same pattern. `files.save({ suggestedName })` and `IFileDialogs.SaveAsync` give a grant that can write; every write goes to a temporary file next to the target, then replaces it.

### 3.7 Native services: the clipboard

**Decided** (2026-11-14). The page uses the web's own `navigator.clipboard`:
- writing is allowed;
- reading needs `Permission.ClipboardRead` declared in C#, as in `Program.cs` above. That sets both the header and the host's gate.

The backend has an injectable `IClipboard` for copies a command makes itself. It is text only in the first version.

File `SqlTool/Scripts/ScriptService.cs`:

```csharp
using Spar;

namespace SqlTool.Scripts;

/// <summary>Why a script could not be copied.</summary>
public closed record CopyScriptError
{
    public sealed record ClipboardBusy : CopyScriptError;

    public sealed record ScriptTooLarge(long Limit) : CopyScriptError;

    public sealed record NoClipboard(string Reason) : CopyScriptError;
}

[SparContract]
public sealed class ScriptService(IClipboard clipboard)
{
    /// <summary>Copies the generated CREATE script of a table.</summary>
    [SparCommand]
    public async Task<Result<CopyScriptError>> CopyCreateScriptAsync(string table, CancellationToken cancellationToken)
    {
        var copied = await clipboard.SetTextAsync($"CREATE TABLE {table} (…);", cancellationToken);
        return copied switch
        {
            Success => Result.Success,
            ClipboardError.Busy => new CopyScriptError.ClipboardBusy(),
            ClipboardError.TooLarge e => new CopyScriptError.ScriptTooLarge(e.Limit),
            ClipboardError.Unavailable e => new CopyScriptError.NoClipboard(e.Reason),
        };
    }
}
```

- **The cases:** `Busy` (another program holds the clipboard, after about 100 ms of retries), `TooLarge` (above 4 MiB of text), `Unavailable`. `GetTextAsync` reads in C#, as a deliberate call; a clipboard with no text reads as empty.
- **Threads** (built in M3c): on Windows the clipboard has a thread of its own with a message-only window, never the window's UI thread, since another program can keep a read waiting; on Linux it is the display's clipboard, through its asynchronous calls on the main loop.
- **Tests:** under the test host `IClipboard` is a fake that holds text (`app.Clipboard.Text`), so a test never touches the machine's clipboard; the real one is checked in Windows Sandbox and on a private X display.

### 3.8 Native services: printing

**Decided** (2026-11-14). The first version prints the current page, styled with CSS `@media print`. `window.print()` keeps working and does the same as `print.show()`.

File `frontend/src/printing.ts`:

```ts
import { print } from "@spar/app/sdk";

declare function toast(text: string): void;

export async function printReport(): Promise<void> {
  await print.show().match({
    success: () => {},
    errors: { dismissed: () => {}, noPrinter: () => toast("No printer"), unavailable: e => toast(e.reason), failed: e => toast(e.reason) },
  });
}

export async function saveReportAsPdf(): Promise<void> {
  // The user picks where to save; the PDF is written to a temporary file, then replaces the target.
  await print.toPdf({ paper: "A4", landscape: false, margins: "default", background: true }).match({
    success: file => toast(`Saved ${file.name}`),
    errors: {
      dismissed: () => {},
      noPrinter: () => {},
      unavailable: e => toast(e.reason),
      failed: e => toast(e.reason),
      accessDenied: e => toast(`No access to ${e.path}`),
      busy: () => {},
    },
  });
}
```

- **C#:** an injectable `IPrinting` writes the page as a PDF into a grant without a dialog, for example `ToPdfAsync(options, folderGrant, "report.pdf", ct)`, returning `Result<FileGrant, PrintError>`.
- **The cases:** `Dismissed`, `NoPrinter`, `Unavailable`, `Failed`, plus the file cases when the PDF is written.
- **Later, if an app needs it:** printing a document other than the visible page, rendered in a hidden window.

### 3.9 Native services: resource usage

**Decided** (2026-11-14). An injectable `IResourceUsage` reports private memory (app total, web engine including its GPU process, the app process, and each engine process by kind) and the size of the data folders. The page sees it only through a contract the app writes.

File `SqlTool/Status/StatusService.cs`:

```csharp
using Spar;

namespace SqlTool.Status;

[SparContract]
public sealed class StatusService(IResourceUsage usage)
{
    /// <summary>Memory for the status bar: only the newest value, no gap events.</summary>
    [SparCommand]
    [SparSubscription(LatestOnly = true)]
    public IAsyncEnumerable<MemoryUsage> Memory(CancellationToken cancellationToken) =>
        usage.WatchMemory(TimeSpan.FromSeconds(1), cancellationToken);

    /// <summary>Folder sizes for the settings page, on request.</summary>
    [SparCommand]
    public Task<Result<DiskUsage, FileAccessError>> DiskAsync(CancellationToken cancellationToken) =>
        usage.GetDiskAsync(cancellationToken);
}
```

- **Sampling** runs only while someone is subscribed, never on the UI thread. A value is pushed only on a change over 1 % or 1 MiB. The minimum interval is 250 ms.
- **Disk** is read on request. A `WatchDisk` with a 5 s minimum can be added if an app needs it.
- **No ready-made Spar contract:** the few lines above keep explicit what the page can see.

### 3.10 Native services: taskbar progress and badges

**Decided** (2026-11-14). An injectable `ITaskbar` per window shows progress and a count on the taskbar button (Windows) or the launcher entry (Linux). The page has the same calls as `taskbar` in the SDK. Where it is unsupported, the calls do nothing and `IsAvailable` is false, because it is decoration, not a result.

File `SqlTool/CopyJobs/CopyProgressReporter.cs`:

```csharp
using Spar;

namespace SqlTool.CopyJobs;

/// <summary>Mirrors a running copy on the taskbar.</summary>
public sealed class CopyProgressReporter(ITaskbar taskbar)
{
    public void Report(long rowsCopied, long rowsTotal) =>
        taskbar.SetProgress(rowsTotal == 0 ? 0 : (double)rowsCopied / rowsTotal);

    public void Failed() => taskbar.SetProgress(TaskbarProgress.Error);

    public void Finished(int tablesWithWarnings)
    {
        taskbar.ClearProgress();
        taskbar.SetBadge(tablesWithWarnings == 0 ? null : tablesWithWarnings);
    }
}
```

- **The badge:** on Windows a count is drawn by Spar as a small numbered overlay in the theme colours, and an app can pass its own icon instead. On Linux the launcher shows the count.

### 3.11 Native services: power and session events

**Decided** (2026-11-14). An injectable `ISystemEvents` reports sleep, wake, lock, unlock and session end, and `IPower` keeps the machine awake during long work. The page gets the same events through `system.on(…)`.

File `SqlTool/Monitoring/PowerAwareMonitor.cs`:

```csharp
using Spar;

namespace SqlTool.Monitoring;

public sealed class PowerAwareMonitor(ISystemEvents systemEvents, IPower power, IMonitor monitor)
{
    /// <summary>Pauses monitoring while the machine sleeps; saves state before the session ends.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var e in systemEvents.Watch(cancellationToken))
        {
            switch (e)
            {
                case SystemEvent.Suspending or SystemEvent.Resumed:
                    monitor.TogglePause();
                    break;
                case SystemEvent.SessionEnding ending:
                    await SaveAsync(ending.Deadline);
                    break;
            }
        }
    }

    /// <summary>A long copy keeps the PC awake until it ends.</summary>
    public async Task CopyAsync(Func<Task> copy)
    {
        using var awake = power.KeepAwake("Copying tables");
        await copy();
    }

    private static Task SaveAsync(DateTimeOffset deadline) => Task.CompletedTask;
}
```

- **Session end** is not cancellable. The app has a short, fixed deadline, and then the normal shutdown runs.
- **How the host listens:**
  - Windows: `WM_POWERBROADCAST`, session-change notifications, and `WM_QUERYENDSESSION`/`WM_ENDSESSION`;
  - Linux: logind over D-Bus (`PrepareForSleep`, `PrepareForShutdown`, lock and unlock), with short delay inhibitors.
- **Testing:** real sleep and wake are tested in Windows Sandbox or a Linux VM, never on the user's machine unattended.

### 3.12 Native services: network status

**Decided** (2026-11-14). The page uses the web's own `navigator.onLine` and the `online` and `offline` events, which both web views take from the OS. The backend has an injectable `INetworkStatus`.

File `SqlTool/Monitoring/NetworkAwareMonitor.cs`:

```csharp
using Spar;

namespace SqlTool.Monitoring;

public sealed class NetworkAwareMonitor(INetworkStatus network, IMonitor monitor)
{
    /// <summary>Pauses monitoring while no network is available.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var paused = !network.Current.IsAvailable;
        await foreach (var status in network.Watch(cancellationToken))
        {
            if (status.IsAvailable == paused)
            {
                monitor.TogglePause();
                paused = !status.IsAvailable;
            }
        }
    }
}
```

- **Available is not reachable:** the tool still checks each instance itself, and a failed check is the real signal.
- **Metered** connections are reported on Windows, so an app can postpone big transfers.

### 3.13 Logs and crash reports

**Decided** (2026-11-14; the log's shape in decision "Logging's public shape", built in M1b).
- **One log file, in every app:** `app.log` in the logs folder gets Spar's lines and every `ILogger` of the app's services, from the host's first moment, so a start-up failure is there too. Inject `ILogger<T>` as usual: the app's services use Microsoft.Extensions.Logging's logger factory, and an app may add its own providers (`AddLogging`, for a console or OpenTelemetry), which work beside the file. Writing never waits for the disk: a full queue drops the newest lines and the file says how many. Rotation comes in M3 (5 files of 10 MB); until then a file over 5 MB is moved aside to `app.1.log` when the app starts.
- **Without a file:** `spar.Logging(l => l.WithoutFile())` adds no file, for an app that sends its logs only to a sink of its own. The risk: support has no local file to ask for, and a failure before the app's services exist (a missing WebView2 Runtime, a relaunch that cannot reach the running app) reaches only standard error, which an app started from its icon does not have; the start-up message then names no log file.
- **Levels:** Information and above in Production, Debug and above in Development and Test builds. `spar.Logging(l => l.Level(…).Level("SqlTool.CopyJobs", LogLevel.Debug))` (section 2) sets the minimum and the level of a category and of every category it is a prefix of, as Microsoft.Extensions.Logging's filter rules do: case-insensitive, the longest match winning, one `*` allowed. They are the app's levels, for every provider, the file and any the app adds: they are applied after the app's own `AddLogging` configuration, so they replace its `SetMinimumLevel`, while a rule the app adds for one provider (`AddFilter<ConsoleLoggerProvider>(…)`, or the file's alias `SparFile`) still applies to that provider.
- **The app's own logging rules override `spar.Logging`:** Microsoft.Extensions.Logging uses a matching rule before the minimum level, and the default of `spar.Logging` is the minimum. So a `"Default"` in the app's own `Logging` configuration section, or an `AddFilter` without a category, sets the level of every category without a rule of its own, Spar's `Spar.*` lines in `app.log` included: with `"Default": "Warning"`, the host's Information lines (`ui-ready`, the security headers, the exit) leave the file. A category rule of the app's replaces the one `spar.Logging` sets for the same category. That is the app's choice; to keep Spar's lines under a stricter default, give them their own level, `"Spar": "Information"` in the app's configuration or `spar.Logging(l => l.Level("Spar", LogLevel.Information))`.
- **Without a new build:** a `logging.json` in the app's per-user folder (`SparFolders.DataRoot`, `%LOCALAPPDATA%\<app id>` on Windows) overrides those levels when the app starts, writable without administrator rights and kept across updates. Its `"level"` holds over the app's own configuration too: Spar applies it after every configuration of the levels, whatever the order, and again when the app's configuration reloads, as the level of every category, so support can raise it even in an app whose configuration sets a `"Default"`. A category level, the app's or `spar.Logging`'s, is more specific and keeps its level; the file's `"categories"` apply last, so naming the same category there changes it. A file Spar cannot use (another shape, an unknown level, a comment) is ignored with one warning in the log, and the app starts with the levels of `Program.cs`. Its whole shape:

```json
{ "level": "Debug", "categories": { "SqlTool.CopyJobs": "Trace" } }
```

- **Lines:** one line per entry, in UTC, readable in any editor: `2026-11-17T22:41:07.318Z INFO  SqlTool.CopyJobs.CopyJobsService  Copied 1200 rows`. A message's further lines and an exception's type, message and stack follow indented by four spaces, so nothing the app or the page logs can look like another entry; scopes and the message's structured values are not written.
- **Page errors:** `console.error` calls, uncaught exceptions and unhandled rejections reach the same log under `Spar.Page`, grouped by kind, text and place in the page: the first of each group at once, as an error with its stack, the rest counted in a summary at most every 10 s, with bounds per document. The page's own console is unchanged. A Development build reads `console.error` from the devtools' own console events, so the devtools' links point at the page's code; Production and Test builds wrap it in the page.
- **Test runs** write the same file in the test's own logs folder, deleted with its data root.
- **Crashes:** an unhandled .NET exception writes a crash record before the process ends.
- **Folders:** every folder has a per-user default under the app id's folder (section 1) and can be set in `Program.cs` (`spar.Folders(…)`, as above; the browser profile in 3.17). App code reads the folders in use from the `SparFolders` service (`DataRoot`, `Data`, `Logs`, `Cache`, `Profile`). `Program.cs` gets the same value earlier from `spar.ResolveFolders()`, for what it builds before the services exist, as the SQL tool does for its hang record (section 2): the per-user folder (`%LOCALAPPDATA%\<app id>`, with `.Development` after the id in a Development build), the app's `spar.Folders(…)` choices, or a test run's own root. Call `spar.Folders(…)` first: after `ResolveFolders()` it throws, so the two can never differ. Test runs move them all under their own data root; a black-box test does it through the environment, since the Production executable takes no data-root switch.
- **The app's own configuration:** Spar reads no `appsettings.json` and takes no configuration package. An app that wants one adds Microsoft.Extensions.Configuration itself; its packages work in a NativeAOT build without warnings, and a JSON file applied to logging measured about 350 KB of executable. The file can sit in the app's data folder without repeating Spar's folder rule: `var folders = spar.ResolveFolders();`, then `new ConfigurationBuilder().AddJsonFile(Path.Combine(folders.Data, "appsettings.json"), optional: true).Build()`, applied with `spar.Services.AddLogging(b => b.AddConfiguration(configuration.GetSection("Logging")))`. Its `"Default"` then overrides `spar.Logging`, as above, and `logging.json` still overrides it.

File `SqlTool/Status/CrashReport.cs`:

```csharp
using Spar;

namespace SqlTool.Status;

public sealed record CrashInfo(DateTimeOffset At, string Summary);

[SparContract]
public sealed class CrashReport(IAppHealth health)
{
    /// <summary>The previous run's crash, so the page can offer to send a report.</summary>
    [SparCommand]
    public Task<CrashInfo?> LastCrashAsync() =>
        Task.FromResult(health.LastCrash is { } crash ? new CrashInfo(crash.At, crash.Exception) : null);
}
```

- **A command is never synchronous** (3.2, "Returns"). This one has nothing to await, so it returns a completed task (`Task.FromResult`).
- **Spar never uploads anything by itself.** What happens with a report is the app's choice.
- **"Send logs":** `diagnostics.collect()` on the page, or `IAppHealth.CollectAsync` in C#, packs logs and crash records into one zip saved through a grant, ready to attach to an email.
- **Secrets:** Spar's own entries never contain secrets. Apps keep theirs out too, because the zip is sent to other people.
- **The UI-thread stall probe** (decision "Hangs: the page's hook, the Production stall probe and "Exit now""): Development and Test builds log each stall of the window's thread over 50 ms as a warning, naming the Spar callback that was running. A Production build has no probe unless `spar.Diagnostics(d => d.KeepStallProbe())` keeps it, as the SQL tool does (section 2); it then only logs. Without the call the probe's code is not in the executable at all.

### 3.14 Single instance and relaunches

**Decided** (decisions "Windows single instance: a lock file plus a pipe" and "The relaunch handler"). One instance of the app runs per data folder. Launching it again hands the new launch's arguments to the running instance: Spar shows and focuses the main window, then calls the handler in `Program.cs` (section 2). The SQL tool's handler accepts `--recipe <file>`, so a shortcut or a scheduled task can queue a saved recipe in the running tool.

File `SqlTool/CopyJobs/RecipeQueue.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SqlTool.CopyJobs;

/// <summary>Saved recipes handed over by a relaunch (Program.cs), run one after another by the copy jobs.</summary>
public sealed class RecipeQueue
{
    private readonly ConcurrentQueue<string> _files = new();

    /// <summary>Queues a recipe file. A relaunch's arguments come from any process of this user, so they are checked.</summary>
    public bool TryAdd(string path)
    {
        if (!path.EndsWith(".sqlrecipe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        _files.Enqueue(path);
        return true;
    }

    public bool TryTake([NotNullWhen(true)] out string? path) => _files.TryDequeue(out path);
}
```

- **What a relaunch does:** the second process hands its arguments and working directory to the running instance, then exits. Spar shows and focuses the main window, restoring it from the tray or from minimized, then runs the handler. Without a handler, the window is focused and the arguments are ignored.
- **The handler** runs off the UI thread, one relaunch at a time in arrival order. It receives the app's root services, as the close guard does; for a scoped service it creates a scope. An async overload, `(relaunch, services, cancellationToken) => ValueTask`, gets a token that is cancelled at shutdown. A relaunch is acknowledged when it is queued, before the handler runs, and is never replayed.
- **Arguments are untrusted input:** any process of the same user can start the app with any arguments, so the handler checks them, as `TryAdd` does. `ResolvePath` resolves a relative path against the relaunch's working directory, not the running instance's.
- **The first launch** gets its arguments in `Program.cs`'s `args`, as usual. Only later launches reach the handler.
- **The page** sees nothing unless the app passes it on, for example through a subscription.
- **When the running instance cannot be reached,** the second process shows a native message, writes stderr and the system log, and exits with a fixed code, as start-up errors do (plan section 3):
  - 8 when the app is running but not responding: it is hung (no answer within 2 s), or still closing after about 5 s;
  - 9 when it runs for another user on the same data folder.

  In the automated mode the message goes only to stderr and the log.
- **Several instances:** an app that needs them says so in C#. The option is designed when an app needs it, together with how their data folders are kept apart.
- **How:** a lock file in the app's data folder, plus a named pipe on Windows and a Unix socket on Linux, only for the same user. The handoff takes about 1 ms once the second process runs.

### 3.15 Start-up problems

**Decided** (user, 2026-11-17; decision "Start-up: prerequisites, single instance and theme"). An app launched from an icon has no terminal, so when it cannot start, Spar shows a native message saying what is wrong and how to fix it, then exits with a fixed code (plan section 3, "Missing prerequisites"). An app can show its own message instead, through `spar.OnStartupProblem` in `Program.cs` (section 2). The SQL tool is installed by a company's IT department, whose users cannot install the WebView2 Runtime themselves, so it opens the company's install guide in place of Microsoft's download page.

File `SqlTool/Support/SupportPage.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using Spar;

namespace SqlTool.Support;

/// <summary>The install guide the company's IT department keeps, for what users cannot fix themselves.</summary>
public static class SupportPage
{
    private const string Guide = "https://intranet.example.com/sqltool/install";

    /// <summary>Opens the guide for a missing or outdated prerequisite; true when it opened, so Spar shows no message.</summary>
    public static bool TryOpen(StartupProblem problem)
    {
        if (problem.Kind is not (StartupProblemKind.Missing or StartupProblemKind.TooOld or StartupProblemKind.OsTooOld))
        {
            return false;
        }

        try
        {
            var page = $"{Guide}?component={Uri.EscapeDataString(problem.Component)}";
            Process.Start(new ProcessStartInfo(page) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
```

- **What a problem says:** `Kind`, `Component` (for example `"WebView2 Runtime"` or `"Windows"`), the `Found` and `Required` versions when a version matters, `Details` (a sentence for people, with an error code when there is one) and `HelpUrl` (where to get or repair the component, or null).
- **The kinds and their exit codes,** the same on every OS and for every app:
  - `Missing` 3 and `TooOld` 4: the web engine (the WebView2 Runtime on Windows, WebKitGTK on Linux) is not installed, or older than Spar's minimum (Runtime 139 on Windows, WebKitGTK 2.48 on Linux, where GTK older than 4.10 is too old as well);
  - `Broken` 5: it is installed but does not start, or, on Linux, its helper programs are missing or the sandbox its web content runs in cannot start;
  - `NoGraphicalSession` 6: on Linux, no display to show the app in (started over SSH or from a service); nothing graphical can be shown, so the hook is the app's only way to add to standard error and the log;
  - `OsTooOld` 7: Windows older than Windows 10 22H2;
  - `NotResponding` 8 and `OtherUser` 9: a relaunch could not reach the running app (3.14).

  The one other fixed code is not a start-up problem: 10 when shutdown did not finish and the user chose "Exit now" (6.11).
- **Return true only when the app showed its own message;** Spar then shows none. Returning false, or throwing, leaves Spar's message: on Windows a task dialog naming what is missing, with Microsoft's download page as a link and a button; on Linux a GTK message naming the package and the command that installs it, or, without GTK 4, kdialog, zenity or xmessage.
- **Written regardless:** standard error and the per-user log (`app.log` in the logs folder, 3.13) get the problem, and the exit code is the kind's, whatever the hook returns.
- **Before the app's services exist:** the hook gets no service provider. It runs on a thread of its own (on Linux the process's main thread, before any window), which may show a window or a message box, and it should return promptly, since the process exits after it.
- **In the automated mode** (tests and agents, 6.11) the hook is not called, and no message is shown: standard error and the log only.

### 3.16 A page that stops responding

**Decided** (decisions "What "Wait" does after the hung-page dialog" and "Hangs: the page's hook, the Production stall probe and "Exit now""). While a window is visible, Spar pings its page every 5 s, and the page's SDK answers from its message handler. A page that misses three pings in a row (15 to 20 s after it stopped) gets Spar's native dialog, "Wait / Reload page". Wait asks again after about 20 s, then 40 s, 80 s and every 160 s while the page stays silent, and a page that answers again closes the dialog itself. Reload page ends the page's process, then loads the page again once; the backend keeps running, and no command is sent again. The app cannot replace the dialog: an app has no UI of its own to show while its page does not answer. It can observe each change through `spar.OnPageUnresponsive` in `Program.cs` (section 2), which the SQL tool uses to keep a record for its support team. The hook returns a `ValueTask` and gets a `CancellationToken`, like the close guard and the relaunch handler; a hook that finishes at once can take the shorter form `(hang, services) => …`.

File `SqlTool/Support/HangLog.cs`:

```csharp
using System.Collections.Concurrent;
using System.Globalization;
using Spar;

namespace SqlTool.Support;

/// <summary>
/// The page's hangs, for the status page and support reports: kept in memory for this run, and appended to
/// <c>hangs.txt</c> in the logs folder for the support team. Program.cs records each change.
/// </summary>
public sealed class HangLog
{
    private const int Kept = 50;

    private readonly ConcurrentQueue<PageHang> _changes = new();
    private readonly string _filePath;

    /// <summary>A record in <paramref name="logsFolder"/>, the logs folder Program.cs resolved.</summary>
    public HangLog(string logsFolder)
    {
        _filePath = Path.Combine(logsFolder, "hangs.txt");
    }

    /// <summary>
    /// One change: unresponsive, recovered, reloaded or closed anyway. Spar's dialog has already asked the user. Spar
    /// awaits it before the next change, and cancels the token when the app exits and stops waiting.
    /// </summary>
    public async ValueTask RecordAsync(PageHang hang, CancellationToken cancellationToken)
    {
        _changes.Enqueue(hang);
        while (_changes.Count > Kept && _changes.TryDequeue(out _))
        {
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:O} {hang.Window} {hang.State} after {hang.Duration.TotalSeconds:0.0} s{Environment.NewLine}");
        await File.AppendAllTextAsync(_filePath, line, cancellationToken);
    }

    /// <summary>How often the page stopped responding in this run, and the longest it went without answering.</summary>
    public (int Hangs, TimeSpan Longest) Summary()
    {
        var hangs = _changes.Count(change => change.State == PageHangState.Unresponsive);
        var longest = _changes.Select(change => change.Duration).DefaultIfEmpty(TimeSpan.Zero).Max();
        return (hangs, longest);
    }
}
```

- **What the hook gets:** the window kind (`main` for the main window, as in a close request), the `State` (`Unresponsive`, `Recovered`, `Reloaded` or `ClosedAnyway`), and the `Duration` the page had not answered, from the first ping it missed.
- **When it runs:** off the window's UI thread, one change at a time in order, each awaited before the next, with the app's root services. A hook that throws is logged, and the next change still reaches it. At exit the changes already reported still reach it within about 3 s; then its token is cancelled and the rest are dropped. It only observes: it cannot hide the dialog, reload the page or close the window.
- **What else counts as unresponsive:** WebView2 reporting the page's renderer unresponsive, which it does only after input reaches a hung page, and a page that answers nothing for about 5 s after a close request (6.11). They are the same state, with one dialog. A page that still answers but whose close guard is silent gets the same "Wait / Close anyway" without being reported here: it is not hung.
- **What is not pinged:** a minimized or hidden window, whose page the browser throttles, and a page that never connected the SDK, which cannot answer.
- **In the automated mode** (6.11) no dialog shows: the change is logged and the hook is called, and a close of a page that does not answer closes anyway.

### 3.17 The browser's defaults, the app id and the profile

**Decided** (decisions "M1b's remaining questions without a spike" and "Security defaults' public shape"; built in M1b). The browser in a Production build is locked down, and the app loosens a default only by name in `spar.Browser(b => …)`, whose methods each document the risk, when the opt-in is reasonable and the safer alternative. Every window shares one browser profile, so each opt-in is for the whole app, and Spar logs every opt-in in force at start-up. The SQL tool keeps them all; only the support build its team sends to one user turns devtools on (section 2).

File `SqlTool/Support/SupportBuild.cs`:

```csharp
namespace SqlTool.Support;

/// <summary>
/// Whether this is the support build, which the team sends to a user whose problem it cannot reproduce: the project
/// built with -p:DefineConstants=SUPPORT_BUILD. A shipped build keeps every browser default.
/// </summary>
public static class SupportBuild
{
#if SUPPORT_BUILD
    public static bool IsSupportBuild { get; } = true;
#else
    public static bool IsSupportBuild { get; }
#endif
}
```

- **The defaults and their opt-ins:**
  - devtools (F12 and Inspect; on Linux WebKitGTK's inspector through Inspect Element): `AllowDevTools()`; GTK's own inspector keys (Ctrl+Shift+I, Ctrl+Shift+D) stay closed outside a Debug build either way;
  - the browser's own keys (reload, print, find, zoom, back and forward): `AllowAcceleratorKeys()`; WebKitGTK binds none of them, so on Linux nothing changes;
  - saving passwords and general autofill, so a database password typed into the page is never offered for saving: `AllowPasswordSaving()`; WebKitGTK has neither;
  - downloads the page starts: `AllowDownloads()`;
  - back and forward to another document, from the mouse's side buttons, Alt+Left, a swipe or `history.back()`: `AllowHistoryNavigation()`; a router's moves within one document always work;
  - the browser's full context menu: `AllowContextMenu()`; without it editable fields keep Cut, Copy, Paste and Select all, and elsewhere no menu shows;
  - the page's own file pickers, `<input type="file">` and `showOpenFilePicker`, `showSaveFilePicker`, `showDirectoryPicker`: `AllowPagePicker()`; the safer way is the file commands of 3.6, which hand the page a grant, never a path;
  - the environment variables that change the browser before it starts: on Windows the WebView2 ones (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER`, `WEBVIEW2_USER_DATA_FOLDER`, the release-channel and script-debugger variables), on Linux `WEBKIT_INSPECTOR_SERVER`, `WEBKIT_INSPECTOR_HTTP_SERVER` and `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS`, and in a Release build every JavaScriptCore option (`JSC_*`) and `GTK_DEBUG`, which can open GTK's inspector: `AllowBrowserEnvironmentOverrides()`. Without it Spar removes them from its process before the browser starts and logs each one by name. Everything else passes, the Linux GPU workarounds (`WEBKIT_DISABLE_DMABUF_RENDERER`, `WEBKIT_DISABLE_COMPOSITING_MODE`, `__NV_DISABLE_EXPLICIT_SYNC`) included, and `JSC_useWasm` always follows `AllowWebAssembly()`. Policies an administrator sets in the registry stay in force.
- **GPU drawing, Linux only for now** (decision "M1c's first questions"; built in M1c): `HardwareAcceleration(…)` is not an opt-in, since it is no security setting, and Windows ignores it. With `Auto`, the default, Spar checks the GPU before the window exists and turns WebKit's acceleration and GTK's GL drawing off where the only GPU is a software renderer, as in virtual machines, WSL and containers, where they cost a slower first window and a much larger app for nothing. `Always` keeps them on even there; `Never` turns them off everywhere, for a graphics driver that breaks the page, which is what the SQL tool's support build does. The user's own `GSK_RENDERER` and `WEBKIT_DISABLE_DMABUF_RENDERER` always win, and Spar logs what it found and chose at start-up.
- **Always off, with no opt-in:** host objects in the page, and a remote debugging port opened by Spar. SmartScreen's reputation check is off while the page stays on the app's origin, which the navigation lock guarantees.
- **A Debug build** has the same defaults, except devtools and, on Windows, the reload keys (F5, Ctrl+R), and on Linux it keeps JavaScriptCore's options from the environment for debugging, so a blocked download or picker shows while developing rather than after shipping.
- **The app id** (section 1) names the app to the OS: it is the process's AppUserModelID on Windows, with `.Development` in a Debug build, and the key an administrator's WebView2 policies are found under. It reaches the app through the build, never by reflection.
- **The browser profile** is the folder named after the web engine under the app's per-user folder: `WebView2` on Windows, `%LOCALAPPDATA%\com.example.SqlTool\WebView2`, and `WebKit` on Linux, `~/.local/share/com.example.SqlTool/WebKit` (its cache beside it in `cache/WebKit`), or `com.example.SqlTool.Development` for a Debug build (decision "The browser profile folder per OS"). `spar.Folders(f => f.Profile(path))` moves it on either OS; the path must be absolute and on a local drive, or the app shows a message and exits with code 70, its own start failure, since a profile on a share is slow and readable by others. The same message and code come when the profile folder cannot be created, and when `LOCALAPPDATA` itself is a network path: the app then refuses to start rather than use another folder. Spar checks the folder the browser actually uses, which a policy can move, logs a moved one, and no file grant ever reaches either folder.

**Decided** (decisions "Contract syntax in the generator" and "Bridge messages: encoding C header fields"; plan sections 2 and 2c). Each message is a JSON header, a TAB (shown as `⇥`), then the payload's JSON. The call `copyJobs.plan`, id 7:

| Outcome | Message |
|---|---|
| The call | `{"type":"call","id":7,"contract":"copyJobs","command":"plan"}⇥{"source":"src","target":"dst","tables":["dbo.Orders"]}` |
| Success | `{"type":"result","id":7,"ok":true}⇥{"planId":"0193…","steps":[{"table":"dbo.Orders","estimatedRows":"120000"}]}` |
| Declared error | `{"type":"result","id":7,"ok":false}⇥{"code":"tableMissing","connection":"src","table":"dbo.Orders"}` |
| Infrastructure failure | `{"type":"failure","id":7,"kind":"response","code":"serialization","execution":"completed"}⇥` |
| Plain `Task` | `{"type":"result","id":7,"ok":true}⇥` (empty payload) |

The page turns a `failure` into a `CallError` with the header's `kind`, `code` and `execution`, plus `contract` and `command` from its own record of call 7.

When a command throws, the page never sees the exception. The failure's payload carries a correlation id, the call's W3C trace id, which the app's log entry for the exception also names, and only a Development host adds the exception's type and message:

| Build | Failure of a command that threw |
|---|---|
| Release | `{"type":"failure","id":7,"kind":"failed","code":"failed","execution":"unknown"}⇥{"correlation":"4bf92f3577b34da6a3ce929d0e0e4736"}` |
| Development | the same, with `"detail":"IOException: disk full"` beside the correlation |

Spar logs the exception through the app's `ILogger` (category `Spar.Bridge`), so it goes wherever the app sends its logs; with no logger registered, nothing is written.

**Limits.** The page is untrusted, so each document's bridge is bounded. A call larger than 4 MiB is refused with `rejected` / `too-large` before its payload is read; a result larger than 16 MiB fails only its call, with `response` / `too-large`; 16 commands run at once, the next ones wait in a queue of up to 1,024 calls and 16 MiB, and a call past that is refused with `rejected` / `busy`. Every refusal is `notStarted`. An app that has measured a need raises one in `Program.cs` with `builder.Bridge(o => o.MaxMessageBytes = …)`; a large transfer belongs in a stream.

`long` travels as a decimal string because JSON numbers lose precision above 2^53. Each C# type has one spelling on the wire and one TypeScript type:

| C# | On the wire | TypeScript |
|---|---|---|
| `string`, `char` (one UTF-16 code unit) | a string; a lone surrogate becomes U+FFFD | `string` |
| `bool` | `true` or `false` | `boolean` |
| `byte`, `sbyte`, `short`, `ushort`, `int`, `uint` | a number in the type's range | `number` |
| `long`, `ulong` | a canonical decimal string: `"-12"`, never `"+12"` or `"012"` | `bigint` |
| `float`, `double` | a finite number; NaN or an infinity fails the call | `number` |
| `decimal` | a string that keeps its scale and has no exponent: `"12.50"`; a value with too many digits is refused, never rounded | `string` |
| `Guid` | `"0f8fad5b-d9cb-469f-a165-70867728950e"` | `string` |
| `DateTimeOffset` | ISO 8601 with an offset: `"2026-11-16T08:30:00+01:00"` | `string` |
| `DateOnly`, `TimeOnly`, `TimeSpan` | `"2026-11-16"`, `"08:30:00.25"`, `"-1.02:30:00"` | `string` |
| `byte[]` | padded base64 with no whitespace | `Uint8Array` |
| an enum, such as `enum Priority { Low, High }` | the member name, exactly: `"High"` | `type Priority = "Low" \| "High"` |
| `T?` | the value, or `null` | `T \| null` |
| arrays and `IReadOnlyList<T>` | a JSON array | `readonly T[]` |

`DateTime` works too, written with `Z`, an offset or nothing by its `Kind`, but the build warns (SPAR024): use `DateTimeOffset`, whose instant does not depend on the reader's time zone.

An enum travels only when each value has one name: a `[Flags]` enum, two members with one value, and an enum with no members are build errors (SPAR025). A value that is no member, such as `(Priority)7`, fails the call that returns it.

### 3.18 Telemetry

**Decided** (decision "M2d's decide-first questions"; built in M2d, `docs/guide/telemetry.md`). The optional `Spar.OpenTelemetry` package, referenced without a version (it takes the SDK's), and `spar.Telemetry()` in `Program.cs` (section 2) export the app's traces, metrics and logs over OTLP, and the page's spans through the backend, when `OTEL_EXPORTER_OTLP_ENDPOINT` is set at run time; without it nothing is exported and the page loads none of its telemetry code. The build sees the reference and gives the page its module: the page needs no code.
- **The backend:** each command call's span (`contract/command`, a child of the page's span of the same call), `spar.call.duration`, and every `ILogger` line at the log file's levels; `spar.Telemetry(otel => …)` takes OpenTelemetry's builder for the app's own instrumentation, such as SqlClient's.
- **The page:** a span for each call it makes, the document's load, and `traced` (6.16); its errors and console messages are already in the backend's logs. Its spans go to `/_spar/otlp/v1/traces` on its own origin and the backend forwards their bytes unchanged, with the endpoint's headers, which the page never sees: protobuf or JSON only, at most 1 MiB a request and 20 requests a second, a 503 for 5 s after the endpoint could not be reached, and a 404 for every `/_spar/otlp/` path without the package or an endpoint (the page's settings request gets an empty 204, so its console shows no failed request).
- **Settings:** OpenTelemetry's standard variables (`OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME`, with `-page` for the page, `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_TRACES_SAMPLER`, `OTEL_BSP_SCHEDULE_DELAY`). An Aspire app host sets them for the app it starts, so its dashboard shows both services.
- **Cost:** the OpenTelemetry SDK and .NET's HTTP client and TLS, about 6 MB of a NativeAOT executable that had no HTTPS before; the page's module, about 18 KB compressed, loads after the page's start.

### 3.19 Settings and the installation file

**Decided** (decision "M3b's decide-first questions", items 4 and 6; built in M3b). `spar.Settings(defaults)` in `Program.cs` (section 2) declares a settings record, a positional record under the wire rules; the generator writes its serializer and Spar registers `ISparSettings<T>`. The file is `{"version":1,"settings":{…}}`, `settings.json` in the data folder (`%LOCALAPPDATA%\<app id>\data` on Windows, `~/.local/share/<app id>/data` on Linux, `.Development` before the extension in a Development build); a property the file lacks takes the default's value, and one the record no longer has is dropped with a log line.
- **Updates** run one at a time, each written at once: staged beside the file, then moved over it. A file that cannot be read is kept as `settings.json.bad-<time>` with a log line, and the app starts on the defaults; one written by a newer version is left alone, and updates fail.
- **Options:** `s => s.Name("layout")` for a second record in its own file; `s.File(path)` for this file alone, an absolute path; `s.Version(2).Migrate(from: 1, json => …)` rewrites an older file's `JsonObject` before it is read. A file on a network share needs `s.AllowNetworkFile()`: Spar keeps a local copy, starts on it when the share cannot be reached (an update then fails with `SparSettingsUnavailableException`), and runs an update's change again on the newer file when another machine wrote it meanwhile.
- **No secrets:** settings files are plain JSON. Passwords go to the credential store (M3c).
- **The installation file**, read only because `Program.cs` calls `spar.InstallationFile()`: `SqlTool.spar.json` beside the executable sets deployment choices without a new build: the folders, `folders.ephemeralBrowserData` (cookies, storage and cache in memory), `folders.removeOnExit` (any of profile, cache, data and logs, removed after the browser's processes exit and again at the next start after a crash), each record's `settings.file` and `allowNetworkFile`, and the log levels in `logging.json`'s shape. Environment variables are expanded; an unknown key, a bad value or a key the code locked (`i => i.Locked("settings.file")`) stops the start with a message that names it. What it set is in the log's first lines and in `spar doctor`. It never sets the page's policy, permissions or the browser's defaults.

File `SqlTool/Settings/ToolSettings.cs`:

```csharp
using Spar;

namespace SqlTool.Settings;

/// <summary>What the tool remembers between runs. Connections live in their own store; passwords never here.</summary>
public sealed record ToolSettings(int BatchSize, int PreviewRows, string? LastRecipe);

/// <summary>The settings the page shows and changes. Wire name: preferences.</summary>
[SparContract("preferences")]
public sealed class PreferencesService(ISparSettings<ToolSettings> settings)
{
    [SparCommand]
    public Task<ToolSettings> GetAsync() => Task.FromResult(settings.Current);

    /// <summary>A batch size between 1,000 and 1,000,000 rows; another one is kept as it was.</summary>
    [SparCommand]
    public Task<ToolSettings> SetBatchSizeAsync(int rows, CancellationToken cancellationToken) =>
        settings.UpdateAsync(current => rows is >= 1_000 and <= 1_000_000 ? current with { BatchSize = rows } : current, cancellationToken);
}
```

A deployment on a Citrix server, where the executable runs from a share and each user's settings stay on it, writes `SqlTool.spar.json`:

```json
{
  "folders": { "ephemeralBrowserData": true, "removeOnExit": ["profile", "cache"] },
  "settings": { "file": "\\\\fileserver\\tools\\settings\\%USERNAME%.json", "allowNetworkFile": true }
}
```

### 3.20 Serial groups

**Decided** (decision "M3b's decide-first questions", item 1; built in M3b). `[SparSerial("copies")]` on a command, or on a contract for all its commands, runs the group's calls one at a time, app-wide, in arrival order, whichever window, test or devtools session makes them (`CopyJobsService.RunAsync`, section 3.2). A waiting call holds no call slot: it counts in the bridge's `QueuedCalls` and `QueuedBytes`. Its `TimeoutSeconds` starts when it runs, one cancelled while it waits never runs (`cancelled` / `cancelled-before-start`), and its span records the wait (`spar.serial.wait`). Streams and subscriptions cannot join a group (SPAR019).

### 3.21 Binary data

**Decided** (decision "M3b's decide-first questions", items 2 and 3; built in M3b). A `byte[]` crosses the bridge as base64, about 30 ms a megabyte; above about 64 KB the page reads binary data from the app's own origin instead, several times faster, and an `<img>` or a download link needs no widened policy.
- **Handles** for data that exists once: a `SparBlob` in a result, or in a result's record, from `SparBlob.FromBytes`, `SparBlob.FromFile` or the `SparBlobs` service's `FromStreamAsync`, which copies the stream to a temporary file. The page gets `url`, `contentType`, `size`, `name`, `bytes()`, `blob()` and `release()`; the handle is valid until it is released or the page's document goes. A document holds at most 10,000 handles and 512 MB of `byte[]` handles (`spar.Bridge(b => b.MaxBlobs = …)`, `MaxBlobBytes`); a result that would pass a limit fails its own call with `response` / `too-large`, and nothing is released to make room. A blob is never in a request, a stream's items or a declared error (SPAR026).
- **Resources** for data the backend can fetch again by a key: a `[SparResource]` command takes a scalar or a record of scalars and returns `SparBlob?`. The page does not call it; it puts its address, `reports.activityChart.url({ server, minutes })`, in an `<img>`, and each load runs the command in its own scope, so a page may show any number and the backend keeps nothing; a null result is a 404 (SPAR027 for any other shape).
- Images, audio, video, plain text, CSV, JSON and PDF are served with their type; any other type, HTML and SVG included, as an attachment, which the page reads with `bytes()` and the browser never shows in place (downloads stay denied by the browser's defaults, 3.17; a file is saved through a dialog, M3c). On loopback (development and browser tests) every address carries the page's key.
- **To the backend** binary data stays a `byte[]` request, within the bridge's 4 MiB message; a file the user picks arrives as a grant (M3c).

File `SqlTool/Reports/ReportsService.cs`:

```csharp
using Spar;

namespace SqlTool.Reports;

/// <summary>A server's activity chart for the last minutes, by its key: what the page's address carries.</summary>
public sealed record ChartKey(string Server, int Minutes);

/// <summary>Reports the page shows or saves. Wire name: reports.</summary>
[SparContract]
public sealed class ReportsService(ICopyHistory history, IActivityCharts charts, SparBlobs blobs)
{
    /// <summary>A finished copy's rows as CSV, for the page to show or save: a handle it releases when it is done.</summary>
    [SparCommand]
    public async Task<SparBlob> ExportCopyAsync(string jobId, CancellationToken cancellationToken)
    {
        await using var csv = history.OpenCsv(jobId);
        return await blobs.FromStreamAsync(csv, "text/csv", $"copy-{jobId}.csv", cancellationToken);
    }

    /// <summary>The chart as PNG, drawn at each load of its address; none for a server the tool does not monitor.</summary>
    [SparCommand]
    [SparResource]
    public async Task<SparBlob?> ActivityChartAsync(ChartKey key, CancellationToken cancellationToken)
    {
        var png = await charts.DrawAsync(key.Server, TimeSpan.FromMinutes(key.Minutes), cancellationToken);
        return png is null ? null : SparBlob.FromBytes(png, "image/png");
    }
}
```

### 3.22 Native services: credentials

**Decided** (decision "M3c's decide-first questions", item 6; built in M3c). Secrets the app keeps for the user, such as a database password, go to the OS's own store through `ICredentialStore`: Credential Manager on Windows (target `<app id>/<name>`, kept for this machine), the Secret Service on Linux (the user's default keyring). Never plain text. C# only: the page sends a secret to the app's own command, which stores it after its own checks, and never gets it back.

File `SqlTool/Connections/SavedConnectionsService.cs`:

```csharp
using Spar;

namespace SqlTool.Connections;

/// <summary>A connection the user saves: the password goes to the OS's store, the rest to the tool's own settings.</summary>
public sealed record SaveConnection(string Name, string Server, string User, string Password);

/// <summary>Why a connection could not be saved or used.</summary>
public closed record SavedConnectionError
{
    public sealed record NoPasswordStore(string Reason) : SavedConnectionError;

    public sealed record PasswordTooLong(int Limit) : SavedConnectionError;

    public sealed record NotSaved : SavedConnectionError;
}

/// <summary>Saved connections. Wire name: savedConnections.</summary>
[SparContract]
public sealed class SavedConnectionsService(ICredentialStore credentials)
{
    /// <summary>Saves the password under the connection's name; the page never reads it back.</summary>
    [SparCommand]
    public async Task<Result<SavedConnectionError>> SaveAsync(SaveConnection request, CancellationToken cancellationToken)
    {
        var stored = await credentials.SetAsync($"connection/{request.Name}", request.Password, cancellationToken);
        return stored switch
        {
            Success => Result.Success,
            CredentialError.TooLarge e => new SavedConnectionError.PasswordTooLong(e.Limit),
            CredentialError.Unavailable e => new SavedConnectionError.NoPasswordStore(e.Reason),
            CredentialError.NotFound => new SavedConnectionError.NotSaved(),
        };
    }

    /// <summary>Whether a password is saved for the connection, so the page can offer to connect without asking.</summary>
    [SparCommand]
    public async Task<bool> HasPasswordAsync(string name, CancellationToken cancellationToken)
    {
        var exists = await credentials.ExistsAsync($"connection/{name}", cancellationToken);
        return exists switch
        {
            bool saved => saved,
            CredentialError => false,
        };
    }

    /// <summary>Forgets the password; one that was never saved is no error.</summary>
    [SparCommand]
    public async Task<Result<SavedConnectionError>> ForgetAsync(string name, CancellationToken cancellationToken)
    {
        var deleted = await credentials.DeleteAsync($"connection/{name}", cancellationToken);
        return deleted switch
        {
            Success => Result.Success,
            CredentialError.Unavailable e => new SavedConnectionError.NoPasswordStore(e.Reason),
            CredentialError.TooLarge or CredentialError.NotFound => Result.Success,
        };
    }
}
```

- **The cases:** `NotFound`, `TooLarge` (above 2,560 bytes of UTF-8, Credential Manager's limit, applied on every OS so an app behaves the same) and `Unavailable` (on Linux no Secret Service, or a locked keyring that cannot ask to be unlocked). Deleting a secret that is not there succeeds.
- **Roaming** (Windows): `spar.Credentials(c => c.Roaming())` in `Program.cs` keeps each credential with the user's roaming profile, for a terminal-server farm whose users move between servers; without it a credential stays on the machine that saved it.
- **Tests:** under the test host the store keeps secrets in memory (`app.Credentials.Stored`), so a test never writes to the machine's Credential Manager or keyring; the real stores are checked in Windows Sandbox and in Spar's Linux CI image.

## 5. The generated TypeScript

**Decided:** results are opaque; `Call` is a `Promise` subclass (decision "TypeScript SDK: `Call` as a `Promise` subclass, `toStringTag`, Vue augmentation"); and the modules ship as `.js` + `.d.ts` + a `.d.ts.map` into the C# file.

The declarations the generator emits for `copyJobs`, as `@spar/app/copyJobs` (file `frontend-generated/copyJobs.d.ts` in the check):

```ts
// Generated by Spar from SqlTool/CopyJobs/CopyJobsService.cs. Do not edit.
import type { Call, CallOptions, Result, SubscribeOptions, Subscription } from "@spar/app/sdk";

export interface CopyRecipe {
  readonly source: string;
  readonly target: string;
  readonly tables: readonly string[];
}

export interface CopyPlan {
  readonly planId: string;
  readonly steps: readonly PlanStep[];
}

export interface PlanStep {
  readonly table: string;
  readonly estimatedRows: bigint;
}

export interface RunCopy {
  readonly planId: string;
  readonly truncateTarget: boolean;
}

export interface CopyReport {
  readonly tables: number;
  readonly rows: bigint;
  readonly elapsed: string;
}

export interface PreviewRow {
  readonly cells: readonly (string | null)[];
}

export interface CopyProgress {
  readonly jobId: string;
  readonly rows: bigint;
}

/** Why a copy could not be planned. (C# `PlanError`) */
export interface PlanError {
  /** The named connection is not configured. */
  readonly connectionNotFound: {
    readonly connectionId: string;
  };

  /** The table does not exist on that connection. */
  readonly tableMissing: {
    readonly connection: string;
    readonly table: string;
  };
}

/** Why a planned copy did not run to the end. (C# `RunError`) */
export interface RunError {
  readonly planOutdated: {
    readonly reason: string;
  };

  readonly targetNotEmpty: {
    readonly table: string;
    readonly rows: bigint;
  };

  /** Rows committed before the failure stay in the target. */
  readonly partiallyCopied: {
    readonly rowsCommitted: bigint;
    readonly reason: string;
  };
}

/** Plans, previews and runs table copies. Wire name: copyJobs. */
export declare const copyJobs: {
  /** Checks the recipe against both connections and estimates the work. */
  readonly plan: (request: CopyRecipe, options?: CallOptions) => Call<CopyPlan, PlanError>;

  /** Copies the planned tables. Cancelling stops between batches. */
  readonly run: (request: RunCopy, options?: CallOptions) => Call<CopyReport, RunError>;

  /** The first rows of each table, streamed as they are read. */
  readonly preview: (request: CopyRecipe, options?: CallOptions) => AsyncIterable<Result<PreviewRow>>;

  /** Live progress of a running copy. Observation only, so it may resume after a lost connection. */
  readonly progress: (jobId: string, options?: SubscribeOptions<"automatic">) => Subscription<CopyProgress>;

  /** Plain Task: success carries no value. */
  readonly cancelAll: (options?: CallOptions) => Call<void>;
};
```

- **The output reads as hand-written code** (plan section 2, "Generated code is readable"): a header naming the C# source file, one member per line, and the C# documentation carried over unchanged.
- **Errors are an interface map** (case name to payload) carrying the C# docs. That gives completions, hover docs, go-to-definition and "add missing properties" for the cases.
- **`Call<T, E>`** is a `Promise<Result<T, E>>` with `match` and `unwrap` (decided 2026-11-14). Awaiting it gives a `Result<T, E>` with the same two methods. Neither the call nor the result exposes the value any other way.
  - `then`, `catch` and `finally` return plain promises.
  - `Call` is exported as a type only.
  - `Result` reports itself as `[object SparResult]`, so Vue does not wrap it in a proxy.
  - Being a real `Promise`, a forgotten call is flagged by `no-floating-promises` without extra lint settings.
- **`SubscribeOptions`:** for an `Automatic` subscription the page may pass `resume: "manual"`. A `Manual` subscription accepts only `"manual"`.
- **`Subscription<T>`** is an `AsyncIterable` of `Result` items (decision "The `events` slot for subscriptions"; plan 2c). Their `match` takes:
  - `success`, for the app's values only;
  - `events`: `reset` is required, and `gap`, `reconnecting` and `resumed` are optional. A coalesced subscription has no `gap`;
  - the usual `errors` and `failure`. As for a call, `errors` is required when the subscription declares errors and absent when it declares none, as `progress` above.

## 6. Using it in the page

**Decided:** opaque results, the `Call` form (decision "TypeScript SDK: `Call` as a `Promise` subclass, `toStringTag`, Vue augmentation"), the interceptor and subscription resumption.

### 6.1 Calls, errors and failures

File `frontend/src/copy.ts`:

```ts
import { configureSpar, SparError } from "@spar/app/sdk";
import { copyJobs, type CopyRecipe } from "@spar/app/copyJobs";

declare function toast(text: string): void;

configureSpar({
  // The interceptor: failures that no call handled. It may notify, never retry or change a result.
  onFailure: e => toast(e.execution === "unknown" ? "Outcome unknown: check the target before retrying" : `The call failed (${e.code})`),
  // Every failure, handled or not, for logs and telemetry.
  observe: e => console.warn(e.kind, e.code, e.execution),
});

export async function planCopy(recipe: CopyRecipe, signal: AbortSignal): Promise<string | undefined> {
  // Every declared error must have a handler. `failure` by kind handles only the kinds it lists;
  // the others go to the interceptor and the result is undefined.
  return copyJobs.plan(recipe, { signal }).match({
    success: plan => `${plan.steps.length} tables, about ${plan.steps.reduce((n, s) => n + s.estimatedRows, 0n)} rows`,
    errors: {
      connectionNotFound: e => `Unknown connection ${e.connectionId}`,
      tableMissing: e => `${e.table} is missing on ${e.connection}`,
    },
    failure: {
      unavailable: () => "The backend is not ready yet",
    },
  });
}

export async function runCopy(planId: string): Promise<string> {
  // With a `failure` function, every outcome is handled here and nothing reaches the interceptor.
  return copyJobs.run({ planId, truncateTarget: false }).match({
    success: r => `Copied ${r.rows} rows in ${r.tables} tables`,
    errors: {
      planOutdated: e => `Plan outdated: ${e.reason}`,
      targetNotEmpty: e => `${e.table} already has ${e.rows} rows`,
      partiallyCopied: e => `Stopped after ${e.rowsCommitted} rows: ${e.reason}`,
    },
    failure: e => (e.execution === "notStarted" ? "Nothing was copied" : "Outcome unknown: check the target before retrying"),
  });
}

export async function cancelEverything(): Promise<void> {
  // unwrap: the value or an exception, for code that prefers exceptions. It never reaches the interceptor.
  try {
    await copyJobs.cancelAll().unwrap();
  } catch (e) {
    if (e instanceof SparError) toast(e.message);
  }
}
```

- A missing case, a misspelled case, a payload field that does not exist, or a forgotten `errors` object fails `tsc`. These were checked in `docs/evidence/ts-results` and are checked again in `tests/AppModel`.
- A command without declared errors has no `errors` property.
- `execution` tells the page whether the backend ran the command: `notStarted`, `stopped`, `completed` or `unknown`. Nothing is retried automatically.
- **Failures** (decided 2026-11-14, decision "`CallError` kinds") are `CallError` values with a `kind`, a `code` and an `execution`. The examples read only these three. A command that threw also has `correlation`, the id to quote to whoever reads the app's log, and in Development `detail`, which is for developers only. The kinds are `cancelled`, `unavailable`, `rejected`, `failed`, `transport`, `response` and `protocol`.
  - **Where a failure goes:**
    - A `failure` function handles every failure.
    - A `failure` object handles the kinds it lists. A misspelled kind fails `tsc`.
    - Everything else goes to `onFailure`, and `match` returns `undefined` for it.
  - **`unwrap`** throws `SparError` and never calls `onFailure`.
  - **`observe`** sees every failure, whoever handles it.
  - Neither hook can retry or change a result.

### 6.2 Streams and subscriptions in React

File `frontend/src/CopyProgressView.tsx`:

```tsx
import { useEffect, useState } from "react";
import { copyJobs } from "@spar/app/copyJobs";

export function CopyProgressView({ jobId }: { readonly jobId: string }) {
  const [rows, setRows] = useState(0n);
  const [status, setStatus] = useState("connecting");

  useEffect(() => {
    const abort = new AbortController();
    void (async () => {
      for await (const item of copyJobs.progress(jobId, { signal: abort.signal })) {
        item.match({
          success: p => { setRows(p.rows); setStatus("live"); },
          // Status events: reset is required (drop stale state); the others are optional.
          events: {
            reset: () => setRows(0n),
            gap: e => setStatus(`${e.dropped} updates skipped`),
            reconnecting: e => setStatus(`reconnecting (attempt ${e.attempt})`),
          },
          // Ignore the failure our own abort causes (StrictMode runs effects twice in development).
          failure: e => { if (!abort.signal.aborted) setStatus(`stopped (${e.code})`); },
        });
      }
    })();
    // Leaving the view aborts the subscription; the backend's token fires.
    return () => abort.abort();
  }, [jobId]);

  return <p aria-live="polite">{status}: {rows.toString()} rows</p>;
}
```

- **Streams** (`IAsyncEnumerable<T>`) become `AsyncIterable` of results, sent in batches with credits, so a slow page slows the producer. Every item is a `Result`, so a terminal failure arrives in-band and the loop cannot skip it. **Decided** (decision "Stream failures in-band; start-up exit codes; the start-up hook"). The alternative from `docs/evidence/wire-details`, an iterator that yields the items and then throws, was not chosen (plan 2c).
- **Subscriptions** use a bounded buffer that drops the oldest events, never blocking the producer. The page sees `gap` with the dropped count.
- **Resume:** after a lost connection an `Automatic` subscription reconnects with backoff and reports `reconnecting`, `reset` and `resumed`. One-shot streams are never resumed.

### 6.3 Start-up, readiness and theme

**Decided** (decision "M1d step 5's shape: the page side of start-up"; built in M1d step 5). The window shows at once in the colour `spar.Window(w => w.Background(light, dark))` declares (section 2), and the page agrees with it from its first frame: the Spar Vite plugin adds `<meta name="color-scheme" content="light dark">` and a small theme style in the same colours to every page, its hash in the Content Security Policy by itself.

File `frontend/index.html`, the optional splash:

```html
<body>
  <div id="spar-splash" role="status" aria-label="Loading the SQL tool">
    <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
    </svg>
  </div>
  <div id="root"></div>
</body>
```

File `frontend/src/main.tsx`:

```tsx
import { useEffect } from "react";
import { createRoot } from "react-dom/client";
import { connectSpar, hostChannel, setColorScheme } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { CopyProgressView } from "./CopyProgressView";

const connection = connectSpar(hostChannel(), fingerprint);

function Root() {
  // The first view has rendered: the splash goes, then the host reports ui-ready.
  useEffect(() => connection.ready(), []);

  return <CopyProgressView jobId="job-1" />;
}

createRoot(document.getElementById("root")!).render(<Root />);

// The app's own theme choice: the window takes it at once, and the next start shows it from the first frame.
export const useDark = () =>
  setColorScheme("dark").match({
    success: () => undefined,
    errors: { notSaved: () => console.warn("Dark until the app closes: the choice could not be saved.") },
  });
```

- **Readiness:** `connection.ready()` is the page's one way to report that it is ready, called once its first view has rendered (here the root's first effect; an app that waits for its first data calls it then). The host's `ui-ready`, which window tests, the black box (6.13) and tools wait for, follows it. Later calls in the same document do nothing. A page that never calls it never reports `ui-ready`.
- **Splash:** optional, static HTML in `index.html` inside the element with the id `spar-splash`; its content (the mark) is the app's. The Vite plugin adds its style: an opaque cover in the window colour from the first frame, whose content fades in 350 ms after the page's first frame through a CSS animation delay, so a fast start never shows it; `aria-busy="true"` on `<body>` while it is there; no fade under `prefers-reduced-motion`. `ready()` removes it, at once while the mark has not appeared, else once it has been visible 400 ms, with a 150 ms fade-out; the host hears `ready` once the removal has had a frame. The timing is the plugin's option: `spar({ splash: { delay: 350, minimumVisible: 400, fadeIn: 120, fadeOut: 150 } })`. Style the mark in the app's own stylesheet; no `style` attribute is needed or allowed.
- **Theme:** the window colours come from `spar.Window(…)` in C#, as constants the build reads (SPAR106 otherwise). `setColorScheme("light" | "dark" | "system")` sets the app's own choice: the window's colour, its title bar, the WebView's background and the page's `prefers-color-scheme` change together, as for a change of the system's theme, and the host saves the choice in the app's data folder for the next start. Its declared error `notSaved`: the window took the choice, but the next start shows the earlier one. In a browser on loopback the choice is saved for the window, and the browser keeps its own scheme. `fakeSpar()` answers it in page tests (6.10), success unless a test scripts `spar.on(setColorScheme).fail("notSaved")`, and `spar.calls(setColorScheme)` lists the choices.

### 6.4 Actions, context menus and shortcuts

**Decided** (2026-11-14). An action is declared once, with a label, an optional shortcut and what it runs. Menus reference the action itself, never a string, so a duplicate id or a misspelled action fails `tsc`.

File `frontend/src/actions.ts`:

```ts
import { actions, menus } from "@spar/app/sdk";

declare function copySelectedRow(): void;
declare function openSearch(): void;
declare function refresh(): void;
declare function redoLast(): void;

export const appActions = actions.define({
  copyRow: { label: "Copy row", shortcut: "Ctrl+C", run: () => copySelectedRow() },
  findTable: { label: "Find table…", shortcut: "Ctrl+K", run: () => openSearch() },
  // A different shortcut per OS, following each platform's convention.
  redo: { label: "Redo", shortcut: { windows: "Ctrl+Y", linux: "Ctrl+Shift+Z" }, run: () => redoLast() },
});

export function attachGridMenu(grid: HTMLElement): void {
  grid.addEventListener("contextmenu", async e => {
    e.preventDefault();
    // A native popup at the pointer: actions, a separator, and an inline item.
    await menus.show(e, [appActions.copyRow, appActions.findTable, "-", { label: "Refresh", run: refresh }]).match({
      success: () => {},
      errors: { dismissed: () => {} },
    });
  });
}
```

- **Duplicates:**
  - The same label twice is allowed. Development logs a warning when one menu shows it twice.
  - The same id inside one `define` is TS1117, and a misspelled action is a `tsc` error.
  - A second `define` of an existing id throws at once, naming both.
  - The same shortcut on two app-wide actions throws at the second definition. The check is made per OS.
- **Per-OS shortcuts** (user, 2026-11-14): a shortcut is a string, the same everywhere, or `{ windows, linux, default }`, because the platforms differ, for example Redo is Ctrl+Y on Windows and Ctrl+Shift+Z on Linux. Menus and tooltips show the one for the current OS. C# actions take the same form.
- **Scoped actions** (`actions.scoped`, disposed with their view) may shadow app-wide shortcuts while active; Development logs which one won.
- **Shortcuts:**
  - They work while the window has focus, on both hosts.
  - Plain editing keys (Ctrl+C, V, X, Z, A) go to an input while the user types.
  - The browser's own accelerators stay off.
- **WebView's default context menu** is off. Text fields keep a minimal native Cut, Copy, Paste and Select all unless the app replaces it.
- **C# actions,** declared in `SparApplication.Create` with a C# handler, share the same ids with the tray and page menus. A duplicate id fails at startup, and an analyzer reports constant ids at build time.
- **Not in the first version:** a native menu bar, and system-wide shortcuts.

### 6.5 Native dialogs

**Decided** (2026-11-14).
- **Browser dialogs:** `alert`, `confirm` and `prompt` from any library are shown as the OS's own message box, owned by the window, with the app's name as title. The host answers them through a deferral, so the window's thread never blocks.
- **New code** uses `dialogs.message`, a normal `Call` with named buttons.

File `frontend/src/confirmDelete.ts`:

```ts
import { dialogs } from "@spar/app/sdk";

export async function confirmDelete(name: string, remove: () => Promise<void>): Promise<void> {
  await dialogs.message({ title: "Delete recipe?", text: `"${name}" cannot be restored.`, buttons: ["Delete", "Cancel"], default: "Cancel" }).match({
    // The button is typed as "Delete" | "Cancel".
    success: async button => { if (button === "Delete") await remove(); },
    errors: { dismissed: () => {} },
  });
}
```

- **C#** gets the same through an injectable `IDialogs` (`MessageAsync` returns `Result<string, DialogError>`).
- **"Leave this page?"** (`beforeunload`) never shows a browser prompt. Closing goes through the app's two-stage close.
- **`prompt()`** becomes a small native input box. The documentation recommends a page form instead.

### 6.6 Opening things outside the app

**Decided** (2026-11-14).
- **Links:** an ordinary `<a href="https://…" target="_blank">` opens in the system browser by itself, because the click is the user action. Links built in code use `shell.openExternal`. Files and folders open through their grants, since the page never sees paths.

File `frontend/src/outside.ts`:

```ts
import { shell, type PickedFile, type PickedFolder } from "@spar/app/sdk";

declare function toast(text: string): void;

export async function openDocs(): Promise<void> {
  await shell.openExternal("https://learn.microsoft.com/sql/").match({
    success: () => {},
    errors: { notAllowed: e => toast(e.reason), failed: e => toast(e.reason) },
  });
}

export async function openExport(file: PickedFile, folder: PickedFolder): Promise<void> {
  await file.openWithDefaultApp().match({
    success: () => {},
    errors: { notAllowed: e => toast(e.reason), noHandler: () => toast("No program opens this file"), failed: e => toast(e.reason) },
  });
  await folder.reveal().unwrap();
}
```

- **Allowed schemes:** `http(s)`, `mailto` and `tel` by default (plan 3b), only after a user action and never with credentials in the URL. Every other new-window attempt stays blocked.
- **Opening files is allow-listed** (decided 2026-11-14):
  - nothing opens by default; the app declares its types in C# with `spar.Shell(s => s.AllowOpening(".csv", ".pdf"))`, as in `Program.cs`;
  - a type the OS reports as dangerous stays `notAllowed` even when declared. On Windows that is `AssocIsDangerous`, Windows' own list (`.bat`, `.reg`, …) plus types registered as always unsafe. On Linux it is executables, `.desktop` files, scripts and executable MIME types;
  - `AllowDangerous(".reg")` is the deliberate exception, with the risk documented;
  - `reveal()` only shows a folder, so it is not restricted.
- **C#** gets the same through an injectable `IShell`: `OpenExternalAsync`, `OpenAsync(grant)` and `RevealAsync(grant)`.

### 6.7 Secondary and modal windows

**Decided** (2026-11-14; decision "M3d's decide-first questions", items 3 and 4). Window kinds are declared in `Program.cs` with a route of the same frontend. The generator turns their names into two TypeScript unions, `WindowKind` for `windows.open` and `ModalKind` for the kinds declared `Modal()`, so a misspelled kind, or a modal opened as a plain window, fails `tsc`. Built in M3d: opening a window, its params, single instance, the allow-list, modals, the close order and `IWindows`; placement and capture protection come with M3d's later steps.

File `frontend/src/openWindows.ts`:

```ts
import { windows } from "@spar/app/windows";

declare function refresh(): void;
declare function toast(text: string): void;

export async function editSettings(): Promise<void> {
  // Waits until the modal closes; its own close button gives dismissed.
  await windows.showModal<{ readonly changed: boolean }>("settings", { tab: "connections" }).match({
    success: result => { if (result.changed) refresh(); },
    errors: { dismissed: () => {}, notOpened: e => toast(e.reason) },
  });
}

export function openQueryEditor(connectionId: string): void {
  void windows.open("queryEditor", { connectionId }).unwrap();
}

// Inside the query editor: the params its opener gave, null in a window opened without any.
export async function editorConnection(): Promise<string | undefined> {
  const params = await windows.params<{ readonly connectionId: string }>().unwrap();
  return params?.connectionId;
}

// Inside the settings window, when the user saves: it closes, and the opener's showModal gets the value.
export const saveAndClose = (): void => windows.closeWith({ changed: true });
```

- **Opening a window** answers once it is shown. A browser on loopback (development, browser tests) opens none of the app's windows: the call ends `notOpened`, with the reason, as it does while the app closes.
- **Params** are JSON the host keeps for the window, at most 64 KB, never in its address: a reload keeps them, and `windows.params()` reads them. Keep larger data in the backend and pass its id.
- **Modals** belong to the window that shows them: it takes no input while one is open, and the modal stays above it. The app's other windows stay usable, and a modal may show another.
  - `showModal` answers once the modal closes: with the value its page gave `closeWith` (JSON, at most 64 KB), or `dismissed` when it closed any other way. A single-instance modal that is already open is brought to the front and gives `notOpened`.
  - `closeWith` closes its own window at once, without asking its close guards, since its page decided. In a window that is not a modal it closes the window, and the value goes nowhere.
  - Aborting `showModal` ends only the wait: the modal stays open, and its result is dropped and logged, as it is when the opener's page reloads meanwhile.
- **Single-instance kinds** focus the open window instead of opening another.
- **Sharing data:** windows share the backend, so they share data through services and subscriptions. A modal also returns its result to the opener.
- **Closing a window** runs its own two-stage close: its page's guard, then `OnCloseRequested`, whose request names the window's kind.
  - Closing the main window closes the others first, the latest opened first, each through its own close. One that stays open keeps the app open.
  - A window with a modal open closes the modal first.
- **From C#,** the injectable `IWindows` holds the main window and every open window of the app's kinds (`Main`, `All`, `Opened`, `Closed`), and opens one by kind with the same params; a modal kind is shown only by a page. Each `IAppWindow` focuses, minimizes, maximizes, restores, sizes (in CSS pixels), titles and closes its window, through its guards. A command reaches the window of the page that called it as `ISparCall.Window`. The page has the same for its own window: `windows.focus()`, `minimize()`, `maximize()`, `restore()`, `setSize(width, height)`, `setTitle(title)` and `close()`.

File `SqlTool/Connections/QueryEditorWindows.cs`:

```csharp
using System.Text.Json.Nodes;
using Spar;

namespace SqlTool.Connections;

/// <summary>Opens a connection's query editor from C#, as the tray's menu does, and names it after the connection.</summary>
public sealed class QueryEditorWindows(IWindows windows)
{
    public async Task<bool> OpenAsync(string connectionId, string connectionName, CancellationToken cancellationToken)
    {
        // The same params a page passes to windows.open: a JSON object the editor's page reads with windows.params().
        var parameters = new JsonObject { ["connectionId"] = connectionId }.ToJsonString();
        var opened = await windows.OpenAsync("queryEditor", parameters, cancellationToken);
        return opened switch
        {
            IAppWindow editor => Named(editor, connectionName),
            WindowOpenError.NotOpened => false,
        };
    }

    // Closing every editor before a restore: each through its own guards, which may keep one open.
    public async Task<bool> CloseAllAsync()
    {
        var editors = windows.All.Where(window => window.Kind == "queryEditor").ToList();
        var results = await Task.WhenAll(editors.Select(editor => editor.CloseAsync()));
        return results.All(closed => closed);
    }

    private static bool Named(IAppWindow editor, string connectionName)
    {
        editor.SetTitle($"Query — {connectionName}");
        return true;
    }
}
```

- **A window's allow-list** (`Commands`) limits the contracts it may call, whole (`Allow<IConnections>()`) or by command (`Allow<IConnections>("list", "test")`), checked against the app's contracts when it starts; any other call is `rejected` / `forbidden-command` before it runs (decision "Per-window command allow-lists"). Spar's own page commands stay allowed.
- **Capture protection** (decided 2026-11-14): `ProtectFromCapture()` hides the settings window, which shows connection strings, from screenshots, recordings and screen sharing.
  - It needs Windows 10 2004 or later (`WDA_EXCLUDEFROMCAPTURE`).
  - At run time it is `IAppWindow.SetCaptureProtection` in C# (a window from `IWindows`) and `capture.protect(true)` on the page; both report whether it took effect.
  - Linux allows no app to block capture, so there `isAvailable` is false.
  - It reduces casual leaks and is not a security boundary.

### 6.8 Zoom and find in page

**Decided** (2026-11-14). Zoom is built in M3d: the limits, the factor remembered with the window's placement, and `zoom.get`, `zoom.set` and `zoom.onChange`; find in page comes with M3d's later steps.
- **Zoom** is on by default: Ctrl+wheel and Ctrl+plus, minus and 0, because people with low vision rely on them.
  - It is saved with each window's placement, and a window of the kind opens at the zoom its last one had.
  - `zoom.get()` is 1 until the host has told a zoomed page its factor, just after the page connects; `zoom.onChange` hears each change, and `zoom.set(factor)` changes it within the window's limits.
  - C# limits it per window (`Zoom(min: 0.5, max: 3)`, as the query editor in `Program.cs` does) or turns it off.
- **Find in page** is off by default, so an app's own Ctrl+F can be a scoped action.
  - `FindInPage()` turns on Ctrl+F with the SDK's find bar, the same on both hosts.
  - Without it, the page can drive the engine's search itself.

File `frontend/src/search.ts`:

```ts
import { find, zoom } from "@spar/app/sdk";

export function showZoom(label: HTMLElement): () => void {
  label.textContent = `${Math.round(zoom.get() * 100)} %`;
  return zoom.onChange(factor => { label.textContent = `${Math.round(factor * 100)} %`; });
}

export async function highlight(text: string, status: (message: string) => void): Promise<void> {
  await find.start(text, { matchCase: false }).match({
    success: count => status(count === 1 ? "1 match" : `${count} matches`),
    errors: { unavailable: e => status(e.reason) },
  });
}
```

- **`find.next()`, `find.previous()` and `find.stop()`** move through the matches and clear them.
- **To check in M1:** which WebView2 Runtime first has the find API, and whether its default find dialog can be suppressed.

### 6.9 A custom title bar

**Decided** (2026-11-14). With `.TitleBar(TitleBar.Custom)` in `Program.cs`, the page draws the title bar with plain markup and no layout code. It is the same on Windows and Linux and in any framework.

File `frontend/src/AppTitleBar.tsx`:

```tsx
export function AppTitleBar({ onSearch }: { readonly onSearch: (text: string) => void }) {
  return (
    // The whole header drags the window; buttons, links and inputs inside stay clickable.
    <header data-spar-titlebar className="titlebar">
      <span>SQL Tool</span>
      <input type="search" aria-label="Search connections" onChange={e => onSearch(e.target.value)} />
      {/* Empty: the SDK adds minimise, maximise and close, styled for the OS and themed by CSS variables. */}
      <div data-spar-caption />
    </header>
  );
}
```

- **The drag area:** the SDK gives `[data-spar-titlebar]` `app-region: drag` and its interactive children `no-drag`. `data-spar-no-drag` covers any other element.
  - Double-click to maximise comes from the host.
  - Right-click for the system menu comes from the host on Windows. On Linux, no window manager tested offered a window menu (plan 3, "Desktop integrations").
  - On Linux the injected script asks the host to start the move.
- **The buttons:**
  - An empty `data-spar-caption` gets the standard buttons, with accessible names and translatable labels.
  - An app that wants its own writes `<button data-spar-caption="close">`, and the same for `minimize` and `maximize`.
- **The layout** is posted to the host by the SDK whenever it changes. The host hit-tests from that cache (plan 3, "Title bar drawn by the page").
- **Window state:** the SDK sets `data-spar-window="maximized"` or `"normal"` on `<html>`, so CSS can swap the maximise and restore icon.

### 6.10 Page tests

**Decided** (decision "Page tests: @spar/app/testing"). Page code is tested in Node with Vitest, against a fake backend from `@spar/app/testing`. Each test scripts the commands it uses, then checks what the page made of the answers and what it sent. The real engine is covered by the browser tests of the end-to-end project.

File `frontend/src/copy.test.ts`:

```ts
import { expect, test } from "vitest";
import { copyJobs } from "@spar/app/copyJobs";
import { fakeSpar } from "@spar/app/testing";
import { planCopy, runCopy } from "./copy.js";

const recipe = { source: "prod", target: "test", tables: ["dbo.Orders"] };

test("a plan reports its tables and rows", async () => {
  // Each test makes its own fake backend, connected in place of the host.
  const spar = fakeSpar();
  spar.on(copyJobs.plan).succeed({ planId: "p1", steps: [{ table: "dbo.Orders", estimatedRows: 1200n }] });

  expect(await planCopy(recipe, new AbortController().signal)).toBe("1 tables, about 1200 rows");
  // What the page sent, read back from the wire as typed values.
  expect(spar.calls(copyJobs.plan)).toEqual([recipe]);
});

test("a declared error is handled by name", async () => {
  fakeSpar().on(copyJobs.run).fail("targetNotEmpty", { table: "dbo.Orders", rows: 3n });

  expect(await runCopy("p1")).toBe("dbo.Orders already has 3 rows");
});

test("a lost connection leaves the outcome unknown", async () => {
  fakeSpar().on(copyJobs.run).failWith({ kind: "transport", code: "lost", execution: "unknown" });

  expect(await runCopy("p1")).toBe("Outcome unknown: check the target before retrying");
});

test("an aborted plan ends as cancelled", async () => {
  const spar = fakeSpar();
  spar.on(copyJobs.plan).hang();
  const abort = new AbortController();

  const planned = planCopy(recipe, abort.signal);
  abort.abort();

  // planCopy handles only `unavailable` failures; `cancelled` goes to onFailure, and the result is undefined.
  expect(await planned).toBeUndefined();
});

test("an answer can depend on the request", async () => {
  fakeSpar().on(copyJobs.run).answer((request, reply) =>
    request.truncateTarget
      ? reply.succeed({ tables: 1, rows: 10n, elapsed: "00:00:01" })
      : reply.fail("targetNotEmpty", { table: "dbo.Orders", rows: 3n }));

  expect(await runCopy("p1")).toBe("dbo.Orders already has 3 rows");
});
```

- **Scripting:** `spar.on(<command>)` takes the generated command itself, so a misspelled command, a value of the wrong type or a case the command does not declare fails `tsc`. Each script replaces the last one for that command and holds for every later call.
  - `succeed(value)`: no argument for a command without a value.
  - `fail(case, payload)`: no payload for a case without one.
  - `failWith({ kind, code, execution })`: an infrastructure failure, as a host reports it.
  - `hang()`: the call never answers. Aborting it cancels it, and the fake backend confirms `cancelled` / `stopped`.
  - `answer((request, reply) => …)`: decides per call. It may wait, so a test can check what the page shows while a call is on its way.
- **On the wire:** the fake backend speaks the real bridge protocol to the page's own connection. Scripted values and the requests `spar.calls` returns cross the same generated encoders and decoders as with a real host, so `bigint`, bytes, enums and `null` behave as they will in the app. A value the encoder refuses (NaN, for example) ends the call as a real host would: `response` / `serialization`.
- **A command no test scripted** is refused (`rejected` / `unknown-command`), with a console error naming it.
- **`fakeSpar()` resets `configureSpar`'s hooks**, so every test starts from the defaults. A test of the hooks configures them after `fakeSpar()`.
- **Production bundles never contain it:** nothing in the app imports `@spar/app/testing`, and the build drops the generated code only it uses.

### 6.11 Closing a window

**Decided** (decision "Two-stage close"). Closing a window first asks: the page's guard, then the backend's, may save, keep the window open, or have Spar ask the user. The window closes only when both agree. The backend's guard is in `Program.cs` (section 2); it reads how many copies are running.

File `SqlTool/CopyJobs/RunningCopies.cs`:

```csharp
namespace SqlTool.CopyJobs;

/// <summary>How many copies are running, for the close guard in Program.cs.</summary>
public sealed class RunningCopies
{
    private int _running;

    public bool Any => Volatile.Read(ref _running) > 0;

    public void Started() => Interlocked.Increment(ref _running);

    public void Ended() => Interlocked.Decrement(ref _running);
}
```

File `frontend/src/closing.ts`:

```ts
import { windows } from "@spar/app/windows";

declare const editor: { readonly hasUnsavedChanges: boolean; save(): Promise<void> };

// The page answers first; the backend's guard in Program.cs is asked only if the page agrees.
export const stopGuarding = windows.onCloseRequested(async request => {
  if (!editor.hasUnsavedChanges) {
    return "close";
  }

  // When Windows shuts down, or tests or an agent close the app, nobody can answer: save, then close.
  if (request.reason === "sessionEnd" || request.automated) {
    await editor.save();
    return "close";
  }

  return { ask: "The query has unsaved changes. Close without saving?" };
});
```

- **Answers:** close, keep, or ask. An ask shows one native confirmation owned by the window, "Close anyway" or "Keep open", so the backend never needs UI of its own. A page may show its own dialog instead and answer close or keep.
- **Order:** the page's guard, then the backend's. Either one keeping the window open ends the request; the backend's guard is not asked after a page that keeps it.
- **What asks:** the close button, Alt+F4 or the taskbar (`CloseReason.User`); the app's own code and Quit in the tray menu (`CloseReason.App`); Windows logging off or shutting down (`CloseReason.SessionEnd`). With `.CloseToTray()`, the close button hides the window, which is not a close; Quit asks.
- **One request at a time:** a second click while a request is open joins it, and an answer from a page that has since navigated away is ignored.
- **Secondary windows** are asked before the main window: closing the main window asks each open secondary window, then itself, and keeps them all open if any answers keep.
- **Session end:** guards run with `sessionEnd` so they can save, the page's for at most 2 s and both together for at most 3 s; then the window closes and the app's services are disposed, and Spar returns to Windows 4 s after it was told the session ends, whatever is left (decision "Window tests: closing, session-end budget, test theme and any-character keys"). Guards cannot keep the window open or ask: an ask counts as close, and Spar never blocks Windows from logging off.
- **Automated mode,** when tests or an agent drive the app: guards still run, so saving on close is tested, with `automated` true, and an ask counts as close. The test host turns it on, and a test of a guard's prompt turns it off for that close and answers the question itself (6.12). Outside tests, the devtools agent session turns it on, and so does the environment variable `SPAR_AUTOMATED=1`, in Development and Test builds only: a Production build ignores it, so nothing outside a shipped app can make it skip its prompts.
- **A page that does not answer** within about 5 s, with no dialog of its own open (a modal `<dialog>` or a visible `aria-modal` element; a script `confirm()` blocks the page and does not count), or a page that is already hung, gets a native "Wait / Close anyway" dialog. Close anyway skips only the page's guard: the backend's guard still runs, then the window closes. Wait keeps the request open and asks again after about 20 s, then 40 s, 80 s and every 160 s (decision "What "Wait" does after the hung-page dialog").
- **After the decision** the window's teardown runs (plan section 3), including the page's closing work.
- **A shutdown that does not finish:** after about 10 s of closing (commands settling, the web view closing, the app's services being disposed), Spar shows a native "Keep waiting / Exit now" prompt, and again every 30 s after "Keep waiting". It goes away by itself when the shutdown finishes. "Exit now" ends the process at once with the fixed exit code 10, so scripts and installers can tell it from a clean exit (0); Spar never ends the process on its own, and in the automated mode it shows no prompt and keeps waiting.

### 6.12 Window tests

**Decided** (decision "Step 8's shape: window tests driven by Spar's own API"; built on Windows in M1b step 8a and on Linux in M1c step 7). A window test runs the app's own `Program.cs` in the test process with its real window: the app's own packed page in WebView2, shown offscreen, never activated and with no taskbar button, or on Linux in WebKitGTK on a private Xvfb that the test process starts for itself, so no test window ever shows on a desktop. On Linux the test machine needs Xvfb, a D-Bus daemon and the XTEST library beside WebKitGTK; its windows show one colour scheme at a time, since WebKitGTK follows the process's dark preference, and a key with no physical code reads `"Unidentified"` in `event.code`. `app.Page` drives the page with Spar's own API, which takes the method names of Microsoft's browser automation library, so a window journey reads like a browser journey of the same end-to-end project.

File `SqlTool.EndToEndTests/ConnectionsWindowTests.cs`:

```csharp
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace SqlTool.EndToEndTests;

// Window tests carry this trait, so they run as their own invocation: a native engine abort ends only that run.
[Trait("Surface", "Window")]
public sealed class ConnectionsWindowTests
{
    [Fact]
    public async Task Removing_a_connection_asks_first_and_takes_it_off_the_list()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);
        var page = app.Page;
        var connections = page.GetByRole(SparRole.List, new() { Name = "Connections" });

        await connections.GetByRole(SparRole.Listitem).Filter(new() { HasText = "Production" })
            .GetByRole(SparRole.Button, new() { Name = "Remove" }).ClickAsync();
        await page.GetByRole(SparRole.Alertdialog).GetByRole(SparRole.Button, new() { Name = "Remove" }).ClickAsync();

        await Expect(connections.GetByRole(SparRole.Listitem)).ToHaveCountAsync(1);
        await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Production removed");
        Assert.Empty(app.Problems);
        Assert.Empty(page.PageErrors);
    }
}
```

- **Finding elements:** by role and accessible name (`GetByRole`), label (`GetByLabel`), text (`GetByText`), placeholder, alt text and title, chained from one another, filtered (`Filter`) and picked (`First`, `Last`, `Nth`). There are no test ids: the page carries no markup for tests, and finding elements as assistive technology names them also checks that it names them. `Locator(css)` is there for structure that roles do not name.
- **Actions wait** until their element is attached, visible, stable, enabled (and editable, for text), scrolled into view and, for the pointer, the element that receives the press. An action needs exactly one element: a locator that finds several fails at once and lists them. Input is trusted: the page sees `isTrusted` events with user activation, sent through the window's own in-process DevTools session, with no port and no second process.
- **Assertions retry** until they hold, for 5 s unless `Timeout` says otherwise (actions wait 10 s); `Not` turns each one around. A failure names what was searched for and what was found at the last try, such as `no element matches GetByRole(Button, "Save"); near: button <button> "Cancel"; button <button> "Save draft"`.
- **Also there:** `EvaluateAsync`, page and element `ScreenshotAsync`, `WaitForURLAsync`, `WaitForUiReadyAsync`, `GotoAsync`, `ReloadAsync`, `Keyboard` and `Mouse`, and what the page reported: `ConsoleMessages`, `PageErrors` and `Dialogs` (dismissed unless `AcceptDialogs`). `app.Problems` lists every warning and error Spar logged for the app: refused or dropped bridge messages, CSP violations and page errors.
- **Isolation:** each window has an InPrivate profile of its own on one browser per test process, so nothing a test stores reaches the next one, and the window's renderer must have exited when the test ends.
- **Light by default:** `o.ColorScheme` is `SparColorScheme.Light` unless a test asks for `Dark` or `System`, so the page's `prefers-color-scheme`, the window's own colours, and so every screenshot, are the same on every machine.
- **Keys:** `PressAsync` takes a key name (`"Enter"`, `"Control+End"`) or any one character; typing goes one character at a time, never half an emoji. A character the US keyboard has no key for (`è`, `€`, `中`) still sends `keydown` and `keyup`, with `event.key` the character and an empty `event.code`. Dead keys, other layouts and input-method composition are not modelled.
- **Running them:** `dotnet test --project SqlTool.EndToEndTests --filter-trait "Surface=Window"`, and in the NativeAOT lane, which leaves out the browser tests. They never take the focus, so they can run while someone uses the computer.
- **The trait is checked** (decision "M1d step 4's shape: tests in the template"). A test that starts the Window surface without `[Trait("Surface", "Window")]`, on the method, its class or the assembly, fails at once, before anything starts: `This test starts the Window surface but has no [Trait("Surface", "Window")]. Add the trait to the test, its class or its assembly, …`. A browser test needs `[Trait("Surface", "Browser")]` the same way. Without the trait, a window test would run in the fast tests' invocation, where a native engine abort takes every result of that run with it.

**Closing the window** (decision "Window tests: closing, session-end budget, test theme and any-character keys"). `app.Window.CloseAsync()` closes the window as its close button does, through its close request (6.11): the page's guard, then the backend's. A window test never shows a dialog: by default the close is automated, so an ask counts as close; a test of a guard's prompt turns that off and gives the answer.

File `SqlTool.EndToEndTests/ClosingWindowTests.cs`:

```csharp
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace SqlTool.EndToEndTests;

[Trait("Surface", "Window")]
public sealed class ClosingWindowTests
{
    [Fact]
    public async Task Closing_with_an_unsaved_query_asks_first_and_tests_close_without_asking()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);
        var query = app.Page.GetByRole(SparRole.Textbox, new() { Name = "Query" });
        await query.FillAsync("select name from sys.databases");

        // Testing the guard's prompt: the test answers in place of the native confirmation.
        var kept = await app.Window.CloseAsync(new()
        {
            Automated = false,
            Answer = CloseAnswer.KeepOpen,
        });
        Assert.Equal(CloseOutcome.Kept, kept.Outcome);
        Assert.Equal("The query has unsaved changes. Close without saving?", kept.Question);
        await Expect(query).ToHaveValueAsync("select name from sys.databases");

        // Automated, as tests and agents close the app: the guard saves, and the window closes.
        var result = await app.Window.CloseAsync();
        Assert.Equal(CloseOutcome.Closed, result.Outcome);
    }
}
```

- **The result:** `Outcome` is `Closed` once the window and its renderer are gone, after which `app.Page` can no longer be driven, or `Kept`; `Question` is the last question a guard asked (also one that counted as close), null when none did; `PageDidNotAnswer` says the page did not answer in time and "Wait / Close anyway" was answered.
- **The options:** `Automated` (true by default); `Answer`, `CloseAnswer.CloseAnyway` or `KeepOpen`, required when `Automated` is false, and given to every question of that close (`KeepOpen` is "Wait" for a page that does not answer); `Reason`, `CloseReason.User` by default, or `App` and `SessionEnd` to test those guards (a session end's rules, without Windows' own message); `Timeout`, 30 s by default.
- **One request at a time:** a close while another is open joins it and gets its result.

### 6.13 Black-box tests

**Decided** (decision "Step 8b's shape: the black-box fixture"; built on Windows in M1b step 8b and on Linux in M1c step 7, decision "The Linux black box: AT-SPI over GDBus; the dialog's title; SIGTERM as a session end"). A black-box test launches the app's exact Production executable, the one users get, with nothing replaced: no test host, no devtools, no service overrides. It is for what only the shipped executable shows: that it starts and closes cleanly, which files it writes, what happens across processes (a second launch, a start-up failure and its exit code). Everything else belongs to the faster levels (6.10, 6.12).

The test project asks for it in its project file, and the build then names each app the project tests in a generated `Projects` class and publishes that app's Production NativeAOT executable beside the tests:

```xml
<SparBlackBoxTests>true</SparBlackBoxTests>
```

File `SqlTool.EndToEndTests/BlackBoxTests.cs`:

```csharp
using Spar;
using Spar.Testing;

namespace SqlTool.EndToEndTests;

// Black-box tests launch processes and take seconds: they run as their own invocation, like window tests.
[Trait("Surface", "BlackBox")]
public sealed class BlackBoxTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_shipped_executable_starts_and_closes_cleanly()
    {
        await using var app = await SparBlackBox.LaunchAsync(Projects.SqlTool, cancellationToken: Token);

        await app.Window.GetByRole(SparRole.List, new() { Name = "Connections" }).WaitForAsync();
        await app.Window.GetByRole(SparRole.Button, new() { Name = "Add connection" }).InvokeAsync();
        await app.Window.GetByRole(SparRole.Dialog, new() { Name = "Add connection" }).WaitForAsync();

        Assert.Equal(0, await app.CloseAsync(Token));
        Assert.True(File.Exists(app.LogFile));
        Assert.True(await app.WaitForProcessTreeExitAsync());
    }

    [Fact]
    public async Task A_second_launch_hands_its_recipe_to_the_running_app()
    {
        await using var first = await SparBlackBox.LaunchAsync(Projects.SqlTool, cancellationToken: Token);

        await using var second = await SparBlackBox.LaunchAsync(Projects.SqlTool, o =>
        {
            o.DataRoot = first.DataRoot;
            o.Arguments.Add("--recipe");
            o.Arguments.Add("nightly-copy.json");
            o.WaitForReady = false;
        }, Token);

        Assert.Equal(0, await second.WaitForExitAsync(Token));
    }

    [Fact]
    public async Task A_relaunch_the_running_app_never_answers_says_so_and_exits_8()
    {
        await using var first = await SparBlackBox.LaunchAsync(Projects.SqlTool, cancellationToken: Token);
        Assert.Equal(0, await first.CloseAsync(Token));

        // The lock of an instance that serves nobody: the next launch waits for it, then gives up.
        var lockFile = Path.Combine(first.AppFolder, "instance.lock");
        await using var held = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var relaunch = await SparBlackBox.LaunchAsync(Projects.SqlTool, o =>
        {
            o.DataRoot = first.DataRoot;
            o.WaitForReady = false;
        }, Token);

        var dialog = await relaunch.WaitForDialogAsync(cancellationToken: Token);
        Assert.Contains("SqlTool can't start", dialog.Text, StringComparison.Ordinal);
        await dialog.PressAsync("Close");
        Assert.Equal(8, await relaunch.WaitForExitAsync(Token));
    }
}
```

- **The data root:** each launch gets a disposable root of its own under `artifacts/` (beside the tests elsewhere), and the app's per-user folders go there through the environment only, since a Production app takes no switch for its folders (decision "M1b's remaining questions without a spike"): on Windows `LOCALAPPDATA`, `APPDATA`, `TEMP` and `TMP` point into it; on Linux `HOME`, `XDG_DATA_HOME` (what `app.LocalAppData` names), `XDG_CONFIG_HOME`, `XDG_CACHE_HOME`, `XDG_STATE_HOME` and `TMPDIR` do, with `XDG_RUNTIME_DIR` in `/dev/shm`, the root under `artifacts/linux-homes/black-box/`. `app.AppFolder`, `DataFolder`, `LogsFolder` and `LogFile` are the folders a Production build uses there. `o.DataRoot = first.DataRoot` starts a second instance on the same data, as a second launch by the same user would; the first launch's handle keeps the root and deletes it, unless `KeepData`. The app never gets a `SPAR_*` variable or an inherited browser override (`WEBVIEW2_*` on Windows; `WEBKIT_*` and `JSC_*` on Linux, which `o.Environment` may still set, to check the app ignores them); `o.Environment` adds the app's own.
- **Ready** means the host's `ui-ready` line in the app's log file (the page connected its bridge and called `ready()`, 6.3) and the accessibility API finding the app's window, within `ReadyTimeout` (30 s). The line is `<time> INFO  Spar.Host  [<run id>] ui-ready at <ms> ms`, a format Spar keeps stable; an app that moves its logs folder or raises `Spar.Host` above `Information` has none: it launches with `o.WaitForReady = false` and waits for an element of its page. `WaitForReady = false` also returns at once for a launch that hands over or fails.
- **The window** is read and driven through the platform's accessibility API, never through input: UI Automation on Windows, AT-SPI on Linux (at-spi2-core's accessibility bus, through a private D-Bus session of each launch). `app.Window.TitleAsync()`, and locators named as in the page API (`GetByRole` with `Name`, `Exact`, `NameRegex` and `Disabled`, `GetByText`, `First`, `Last`, `Nth`) with `WaitForAsync`, `CountAsync`, `IsVisibleAsync`, `NameAsync`, `AllNamesAsync`, `InputValueAsync` and `InvokeAsync`, a button's press through the Invoke pattern (on Linux AT-SPI's default action), which returns once the page has taken the press: on Windows when the browser confirms it (UI Automation's Invoked event, or the element gone from the tree when the press removed it), after an app's first action has waited until its page settled in the accessibility tree; a press the browser does not confirm within the timeout fails, and is never pressed again. The page's elements come with their ARIA roles and accessible names, as the browser exposes them to assistive technology (on Linux WebKit's computed ARIA role; a text element is one whose own text matches). An action needs one element: a locator that finds several fails at once.
- **Native messages** (a start-up failure, a failed relaunch, a close question): `WaitForDialogAsync()` reads the first one the app shows, with its `Title`, `Text` and `Buttons`, and `PressAsync(name)` answers it. On Linux the message is GTK's alert dialog, which has no title: `Title` is the app's name there too, put in by the fixture, and a press shows on the button for about 250 ms before GTK answers it.
- **Closing and exit codes:** `CloseAsync()` closes the window as its close button does, so the guards of 6.11 run, and returns the exit code; `WaitForExitAsync()` waits for an exit the app makes on its own; both wait `ExitTimeout` (15 s). `Kill()` ends the app and everything it started. The black box's session end of the OS (Spar's own tests) is `WM_ENDSESSION` on Windows and `SIGTERM` on Linux, where the app answers `SIGTERM` and `SIGINT` with the same session-end close (6.11).
- **Nothing is left behind:** the app runs in a job object (Windows) or is started with `KillOnParentExit` (Linux), which ends it, the browser's processes included, when the test process ends, however it ends. Disposal ends what is still running (an app asked to exit gets `ExitTimeout` first), waits until every process of the app is gone and deletes the root. A cleanup that cannot finish, or a browser process that outlives a clean exit, fails the test.
- **Nothing shows:** on Windows the app runs on a desktop of the test process's own, never on the monitor, so it never takes the focus or input of the person at the computer. On a CI service (the `CI` or `TF_BUILD` variable set), where no one is at the screen, it shows on the runner's own desktop without taking the focus, because a second desktop there cannot always host the browser. Checks that need real input or focus run in Windows Sandbox. On Linux every launch shows on a private Xvfb of the test process, and needs `Xvfb`, `dbus-daemon` and at-spi2-core; without them a black-box test skips, saying what is missing, and fails when `CI=true`.
- **The build:** for each `SparTestedApplication`, `Projects.<App>` carries the app's name, `AppId`, `ProjectDirectory` and `ExecutablePath`, the Production executable published incrementally into the test project's `obj/` and copied beside the tests, so the path also works from a publish of the test project (the NativeAOT lane). `<SparBlackBoxPublish>false</SparBlackBoxPublish>` keeps the class and skips the publish, for a pipeline that puts the executable there itself.
- **Running them:** `dotnet test --project SqlTool.EndToEndTests --filter-trait "Surface=BlackBox"`, and in the NativeAOT lane.

### 6.14 Backend updates while developing

**Decided** (plan section 4, "Hot Reload signal"; decision "M1d step 3's shape: dotnet watch and Hot Reload"). Under `dotnet watch`, or an IDE's Hot Reload, a C# edit that Hot Reload can apply patches the running backend without a restart. In a Debug build the page is told: after each applied edit, `onBackendUpdate` handlers run, and Spar's development overlay shows a small toast, "Backend updated: CopyJobsService (revision 3)", which a click dismisses. A production build has neither: `onBackendUpdate` does nothing there, and the bundler removes it and the toast.

File `frontend/src/backendUpdates.ts`:

```ts
import { configureSpar, onBackendUpdate, type BackendUpdate } from "@spar/app/sdk";
import { copyJobs } from "@spar/app/copyJobs";

declare function toast(text: string): void;
declare function showPlan(steps: number): void;

configureSpar({
  onFailure: e => toast(`The call failed (${e.code})`),
  // The page shows its own notice below, so Spar's development toast is turned off.
  backendUpdateToast: false,
});

// Debug builds only: after each C# edit Hot Reload applied, ask the backend again, so the view shows what the new code
// returns. Returns the function that stops listening (a React effect's cleanup, for example).
export const stopListening = onBackendUpdate((update: BackendUpdate) => {
  toast(`Revision ${update.revision}: ${update.types.join(", ")}`);
  void copyJobs.plan({ source: "prod", target: "test", tables: ["dbo.Orders"] }).match({
    success: plan => showPlan(plan.steps.length),
    errors: { connectionNotFound: () => undefined, tableMissing: () => undefined },
  });
});
```

- **`BackendUpdate`:** `revision` counts the updates applied to this run of the backend, from 1; `time` is when the backend applied it (ISO 8601); `types` are the full names of the types the edit changed (a method's state machine or a lambda counts as the type it belongs to).
- **Each `configureSpar` call sets every setting,** those left out to their defaults: `backendUpdateToast` is `true` unless the call says `false`, as `onFailure` goes back to the default when a call leaves it out.
- **Limits:** the event can arrive after the patched code already ran once (a call that started meanwhile). Edits Hot Reload cannot apply are reported only in `dotnet watch`'s terminal, which restarts the app for them.
- **Contract edits always restart:** an edit that changes what Spar generates for the contracts (a command's name, request, result or limit, a DTO's or an error case's properties, a wire name, the page security in `Program.cs`) is never patched. A Development build's generated code makes such an edit one that Hot Reload cannot apply, so `dotnet watch` rebuilds the app, which regenerates the TypeScript client, and starts it again; in the window the page loads afresh, and on loopback the open page reloads itself (section 4). A command's body is patched as usual.
- **In page tests,** `spar.backendUpdate({ types })` from `fakeSpar()` sends the event as a Debug host does, and `fakeSpar()` turns the toast back on, as it resets the failure hooks.

### 6.15 Scenes

**Decided** (decision "M2c's decide-first questions"; built in M2c). A scene is a state of the app worth a look, written as C# in the end-to-end test project: a static method marked `[SparScene("name")]` that starts the app through the scene and brings its page to that state, as a window test does. `dotnet spar snap name` captures it into `obj/spar-snap/name/`: `page.png`, the accessibility tree (`tree.txt` and `tree.json`, as `spar tree` shows it), the bridge calls (`calls.json`) and the problems Spar, the page and its console reported (`problems.txt`). `dotnet test` runs every scene as a window test of its own, so a scene that no longer reaches its state fails like any test.

File `SqlTool.EndToEndTests/Scenes.cs`:

```csharp
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace SqlTool.EndToEndTests;

public static class Scenes
{
    // spar snap connections: the list of saved connections.
    [SparScene("connections")]
    public static async Task Connections(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        await Expect(app.Page.GetByRole(SparRole.List, new() { Name = "Connections" })).ToBeVisibleAsync();
    }

    // spar snap remove-connection: the question before a connection is removed.
    [SparScene("remove-connection")]
    public static async Task RemoveConnection(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        await app.Page.GetByRole(SparRole.List, new() { Name = "Connections" })
            .GetByRole(SparRole.Listitem).Filter(new() { HasText = "Production" })
            .GetByRole(SparRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Expect(app.Page.GetByRole(SparRole.Alertdialog)).ToBeVisibleAsync();
    }
}
```

- **The same picture every run** on one machine and OS: the page is 1280 × 800 CSS pixels at a scale of 1 whatever the monitor's (`scene.Size` changes it), in the light theme, rendered in software, with reduced motion; the capture waits until the page has been idle for 200 ms (no bridge call, no animation, no `setTimeout` due within 10 s, fonts loaded), finishes or stops its animations, hides the caret and moves the pointer to a corner. Across machines, or between Windows and Linux, the fonts and rendering differ.
- **A fixed clock:** the backend's `TimeProvider` and the page's `Date` start at 15 January 2026, 09:30 UTC (`scene.Time` changes it) and run in real time from there, so timers and timeouts work and a page's "edited 5 minutes ago" reads the same every run. Text that counts seconds can still differ between runs.
- **Options and data:** `scene.StartAsync<Program>(o => o.Services(…))` takes a window test's options, so a scene replaces services or fills data as a test does.
- **Names:** lower case letters, digits and hyphens, once per project; a scene method is `static`, returns `Task` and takes one `SparScene`. The build reports a scene it cannot run (SPAR110 to SPAR112).
- **The command:** `dotnet spar snap --list` lists the scenes, `dotnet spar snap name…` or `--all` captures them (`--out` for another folder, `--no-build` to use the last build, `--json`), from the app's folder or the end-to-end project's.

### 6.16 Grouping calls in a trace

**Decided** (decision "M2d's decide-first questions", item 3; built in M2d). With telemetry on (3.18), every call is a span of the page's, the parent of the backend's span of the same call. `traced(name, run)` makes one span around `run`, and the calls `run` starts before its first `await` become its children, so an interaction reads as one trace. Without telemetry it only runs `run`.

File `frontend/src/planning.ts`:

```ts
import { traced } from "@spar/app/sdk";
import { copyJobs, type CopyRecipe } from "@spar/app/copyJobs";

// The "Compare plans" button: both plans in one trace, since both calls start inside `traced`, before any `await`.
export function comparePlans(first: CopyRecipe, second: CopyRecipe) {
  return traced("compare plans", () => Promise.all([copyJobs.plan(first), copyJobs.plan(second)]));
}
```

- **No context across `await`:** the page has no context propagation (no `zone.js`), so a call started after `run`'s first `await` begins a trace of its own; start the calls first and await them together, as above.
- **The span ends** when `run` returns, or when the promise it returns settles; a throw or a rejection marks it as an error.
- **No click spans:** the page records no user interactions by itself; `traced` is how an interaction gets its name.

### 6.17 Binary data in the page

**Decided** (decision "M3b's decide-first questions", item 2; built in M3b). A `SparBlob` in a result arrives as an object whose `url` is its address on the app's origin; `bytes()` and `blob()` read it and reject when it was released or its document went; `release()` lets the backend drop it. A resource is an object with `url(request)` alone (3.21): its address goes in an `<img>`, and the browser loads it as often as it likes.

File `frontend/src/reports.ts`:

```ts
import { reports } from "@spar/app/reports";

/** The server's activity over the last hour, drawn by the backend at each load of the image. */
export function activityChart(server: string): HTMLImageElement {
  const image = new Image();
  image.alt = `Activity of ${server} over the last hour`;
  image.src = reports.activityChart.url({ server, minutes: 60 });
  return image;
}

/** A finished copy's rows as text, for the preview pane; the handle goes as soon as they are read. */
export async function copyRows(jobId: string): Promise<string> {
  const file = (await reports.exportCopy(jobId)).unwrap();
  try {
    return await (await file.blob()).text();
  } finally {
    await file.release();
  }
}
```

- **Release what the page no longer shows:** a handle holds its data on the backend until then, or until the document goes; an image that renders again reads the same address.
- **Below about 64 KB** a `byte[]` in the result is as fast and needs no release.
- **Page tests** reply with `spar.blob(bytes, "text/csv", "rows.csv")`, whose bytes the fake keeps; a resource's address is built as on the host, and nothing serves it in a page test.

## 7. Still open in this model

- **Contracts and wire:**
- **Configuration:**
  - the type names of the configuration builders behind `spar.Window(…)` and the other calls of section 2 not built yet (the check's stand-ins call them `SparBuilder` and `WindowOptions`). The calls themselves are the ones the decisions show, as marked in the `Program.cs` example. `CspBuilder`, `CspReports`, `PermissionsBuilder` and `Permission` are Spar's own since M1b.
- **Tests:** the shape of the fast test project and the in-process test factory (decision "In-process test factory", adoption open); for window tests (6.12), how scenes and a fixed clock are declared; an app's own "close this window" API (`CloseReason.App` from app code), which nothing calls yet.

Decided since this list was first written, and shown above:
- streams with declared errors (decision "Streams may declare errors");
- the `events` slot for subscriptions (decision "The `events` slot for subscriptions");
- the committed contract snapshot (decision "The contract snapshot is committed");
- `Call` as a `Promise` subclass (decision "TypeScript SDK: `Call` as a `Promise` subclass, `toStringTag`, Vue augmentation");
- the `Program.cs` shape (decision "`Program.cs` shape confirmed");
- the declaration of permissions (decision "Camera, microphone and other permissions");
- readiness through `connection.ready()` alone, the splash and `setColorScheme` (decision "M1d step 5's shape: the page side of start-up");
- the name of a subscription that keeps only its newest value, `LatestOnly = true`, and streams and subscriptions built through real Spar (decisions "M3a's decide-first questions" and "M3a: streams and subscriptions").

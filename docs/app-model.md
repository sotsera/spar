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
  SqlTool.csproj            <Project Sdk="Spar.Sdk">, with <SparPlatforms>windows;linux</SparPlatforms>
  Program.cs
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
global.json                 pins the SDK and Spar.Sdk (msbuild-sdks)
```

- The target hosts come from `SparPlatforms` (both by default); `Spar.Sdk` adds the host packages itself.
- The frontend is found through `SparFrontendPath`, by default `frontend/`.
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

## 2. `Program.cs`

**Decided** (user, 2026-11-15; decision "`Program.cs` shape confirmed"). `Program.cs` holds exactly one `SparApplication.Create` and one `Run`, directly in the top-level statements; anything else is a diagnostic (plan section 2, "`Program.cs`").

- **Read by the build, so constants only:** the declarations the build needs sit directly in the `Create` configuration with constant values, so the build reads them without running code. They are:
  - the CSP (plan 3b);
  - the permissions (plan 3b);
  - the window kinds, whose names become a TypeScript union in `@spar/app/windows` (6.7);
  - the window colours (plan 3d).
- **Run at start-up, so ordinary values are fine:** every other configuration call runs when the app starts and may use run-time values. The example does so with `spar.Folders(f => f.Logs(Path.Combine(AppContext.BaseDirectory, "logs")))` and with the tray menu's lambda.

File `SqlTool/Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Spar;
using SqlTool.Connections;
using SqlTool.CopyJobs;
using SqlTool.Monitoring;

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

    // The strict CSP is the default; each addition is explicit (plan 3b). Decided.
    spar.ContentSecurity(csp => csp.FontSrc("https://fonts.gstatic.com"));

    // Powerful features are denied unless declared (plan 3b). Decided.
    spar.Permissions(p => p.Allow(Permission.ClipboardRead));

    // File types the app may open with their default program; nothing opens by default (6.6). Decided.
    spar.Shell(s => s.AllowOpening(".csv", ".pdf"));

    // Where data, logs and crash records go; each has a per-user default (3.13). Decided.
    // Here logs sit next to the executable, as in a portable install.
    spar.Folders(f => f.Logs(Path.Combine(AppContext.BaseDirectory, "logs")));

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

    // Services: Microsoft DI, one scope per command call.
    spar.Services.AddSingleton<IConnectionStore, FileConnectionStore>();
    spar.Services.AddScoped<IConnections, ConnectionsApi>();
    spar.Services.AddSingleton<IMonitor, InstanceMonitor>();
    spar.Services.AddSingleton<RunningCopies>();
});

return app.Run();
```

- **Class contracts** are registered by the generated code.
- **Interface contracts** need their implementation registered, as `IConnections` above, whether the app declares them or a library it lists does (3.4). An app that registers none does not start: the error names the contract and its interface.
- **Overrides of security defaults** (plan 3c) go in the same configuration. Spar logs each one at startup, and `spar doctor` lists them.

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

    /// <summary>Copies the planned tables. Cancelling stops between batches.</summary>
    [SparCommand]
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
- **`ISparCall`** gives the token, the call id and the names to the contract and to any scoped service of the call. A singleton cannot depend on it (`ValidateScopes`).
- **Wire types (decided 2026-11-14):**
  - `long` is a decimal string on the wire and `bigint` in TypeScript.
  - `TimeSpan` is a string in the constant format `[-][d.]hh:mm:ss[.fffffff]`, so `elapsed` below is a `string`.

### 3.3 An interface contract

Every method of a `[SparContract]` interface is a command; `[SparCommand]` there only overrides a name. The implementation comes from DI and exposes only the interface's members.

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

- **Drag and drop** (decided 2026-11-14):
  - Off by default, and the navigation lock stops the browser from opening a dropped file.
  - While a valid drag hovers, the SDK sets `data-spar-drop="over"` on the area, so CSS can highlight it.
  - Page-internal drag and drop, such as sortable lists, is unaffected.
  - How each host gets the dropped files (WebView2's `postMessageWithAdditionalObjects`, a GTK drop target on WebKitGTK) is checked in M3.
- **The dialog's cases:** `dismissed` (not `cancelled`, which is the `CallError` of an aborted call), `accessDenied`, `unavailable` and `busy`. The app's own error types above follow the same rule: their case for a closed dialog is `Dismissed`.
- **Read and write cases:** `notFound`, `accessDenied`, `inUse`, `tooLarge`, `grantExpired`, and for folders `outsideFolder` and `readOnlyGrant`.
- **Folder grants** declare `Read` (the default) or `ReadWrite`. They offer list, read, safe write, create folder and delete (write access only). The host checks the final resolved path, so links leaving the folder are refused. Remembering a folder across restarts is not in the first version.
- **Grants** are opaque on the wire, tied to the window that asked, revoked when it closes, and limited in number, with an optional expiry.
- **Saves** follow the same pattern. They write to a temporary file next to the target, then replace it.

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

- **The cases:** `Busy` (another program holds the clipboard, after a short retry), `TooLarge`, `Unavailable`. `GetTextAsync` reads in C#, as a deliberate call.
- **Focus:** clipboard calls fail without a focused window, so the real copy and paste is checked in Windows Sandbox or a Linux VM.

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
    [SparSubscription(Coalesce = true)]
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

**Decided** (2026-11-14).
- **Logs:** Spar logs through `ILogger`. In Production it writes rotating files, by default 5 files of 10 MB, at a level that configuration can raise without a new build.
- **Page errors:** console errors, uncaught exceptions and unhandled rejections reach the same log, grouped with flood limits.
- **Crashes:** an unhandled .NET exception writes a crash record before the process ends.
- **Folders:** every folder has a per-user default and can be set in `Program.cs` (`spar.Folders(…)`, as above). Test runs move them all under their own data root.

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

## 4. On the wire

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

- A missing case, a misspelled case, a payload field that does not exist, or a forgotten `errors` object fails `tsc`. These were checked in `spikes/ts-results` and are checked again in `tests/AppModel`.
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

- **Streams** (`IAsyncEnumerable<T>`) become `AsyncIterable` of results, sent in batches with credits, so a slow page slows the producer. Every item is a `Result`, so a terminal failure arrives in-band and the loop cannot skip it. **Decided** (decision "Stream failures in-band; start-up exit codes; the start-up hook"). The alternative from `spikes/wire-details`, an iterator that yields the items and then throws, was not chosen (plan 2c).
- **Subscriptions** use a bounded buffer that drops the oldest events, never blocking the producer. The page sees `gap` with the dropped count.
- **Resume:** after a lost connection an `Automatic` subscription reconnects with backoff and reports `reconnecting`, `reset` and `resumed`. One-shot streams are never resumed.

### 6.3 Start-up, readiness and theme

File `frontend/src/main.tsx`:

```tsx
import { createRoot } from "react-dom/client";
import { ready, setColorScheme } from "@spar/app/sdk";
import { CopyProgressView } from "./CopyProgressView";

createRoot(document.getElementById("root")!).render(<CopyProgressView jobId="job-1" />);

// Manual splash mode only: by default the SDK reports ui-ready, and removes the splash, by itself.
ready();

// The app's own theme choice; the host saves it and applies it before the next start.
export const useDark = () => setColorScheme("dark");
```

- **Splash** (Decided): static HTML in `index.html`. It never shows on a fast start, and it is removed at `ui-ready`, or at `ready()` in manual mode.
- **Theme** (Decided): the window colours come from `spar.Window(…)` in C#. The SDK's `setColorScheme("light" | "dark" | "system")` stores the app's choice.
- **Open:** the exact SDK export names, `ready` against `spar.ready()`.

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

**Decided** (2026-11-14). Window kinds are declared in `Program.cs` with a route of the same frontend. The generator turns their names into a TypeScript union, so a misspelled kind fails `tsc`.

File `frontend/src/openWindows.ts`:

```ts
import { windows } from "@spar/app/windows";

declare function refresh(): void;

export async function editSettings(): Promise<void> {
  // Waits until the modal closes; its own close button gives dismissed.
  await windows.showModal<{ readonly changed: boolean }>("settings", { tab: "connections" }).match({
    success: result => { if (result.changed) refresh(); },
    errors: { dismissed: () => {} },
  });
}

export function openQueryEditor(connectionId: string): void {
  void windows.open("queryEditor", { connectionId }).unwrap();
}

// Inside the settings window, when the user saves:
export const saveAndClose = (): void => windows.closeWith({ changed: true });
```

- **Modals** disable their owner while open and stay above it.
- **Single-instance kinds** focus the open window instead of opening another.
- **Sharing data:** windows share the backend, so they share data through services and subscriptions. A modal also returns its result to the opener.
- **Closing the main window** closes its secondary windows first, through the two-stage close.
- **A window's allow-list** (`Commands`) limits the contracts it may call (decision "Per-window command allow-lists").
- **Capture protection** (decided 2026-11-14): `ProtectFromCapture()` hides the settings window, which shows connection strings, from screenshots, recordings and screen sharing.
  - It needs Windows 10 2004 or later (`WDA_EXCLUDEFROMCAPTURE`).
  - At run time it is `IWindows.SetCaptureProtection` in C# and `capture.protect(true)` on the page; both report whether it took effect.
  - Linux allows no app to block capture, so there `isAvailable` is false.
  - It reduces casual leaks and is not a security boundary.

### 6.8 Zoom and find in page

**Decided** (2026-11-14).
- **Zoom** is on by default: Ctrl+wheel and Ctrl+plus, minus and 0, because people with low vision rely on them.
  - It is saved with each window's placement.
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
- **Session end:** guards run with `sessionEnd` and a short deadline, about 5 s, so they can save. They cannot keep the window open or ask: an ask counts as close, and Spar never blocks Windows from logging off.
- **Automated mode,** when tests or an agent drive the app: guards still run, so saving on close is tested, with `automated` true, and an ask counts as close. The test host turns it on, and a test of a guard's prompt turns it off for that close. Outside tests, the devtools agent session turns it on, and so does the environment variable `SPAR_AUTOMATED=1`, in Development and Test builds only: a Production build ignores it, so nothing outside a shipped app can make it skip its prompts.
- **A page that does not answer** within about 5 s, with no dialog of its own open, is treated as a hung page (plan section 3, "Hung pages").
- **After the decision** the window's teardown runs (plan section 3), including the page's closing work.

## 7. Still open in this model

- **Contracts and wire:**
- **Configuration:**
  - the type names of the configuration builders behind `spar.Window(…)`, `spar.ContentSecurity(…)`, `spar.Permissions(…)` and the other calls of section 2 (the check's stand-ins call them `SparBuilder`, `WindowOptions`, `CspBuilder` and `PermissionsBuilder`). The calls themselves are the ones the decisions show, as marked in the `Program.cs` example.
- **Page SDK:** the exact export names, `ready` against `spar.ready()` (6.3).
- **Tests:** the shape of the fast test project and the in-process test factory (decision "In-process test factory", adoption open).

Decided since this list was first written, and shown above:
- streams with declared errors (decision "Streams may declare errors");
- the `events` slot for subscriptions (decision "The `events` slot for subscriptions");
- the committed contract snapshot (decision "The contract snapshot is committed");
- `Call` as a `Promise` subclass (decision "TypeScript SDK: `Call` as a `Promise` subclass, `toStringTag`, Vue augmentation");
- the `Program.cs` shape (decision "`Program.cs` shape confirmed");
- the declaration of permissions (decision "Camera, microphone and other permissions").

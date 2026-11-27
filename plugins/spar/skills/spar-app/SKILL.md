---
name: spar-app
description: Explains how a Spar desktop app is written, covering the project layout, Program.cs with the window and the page security declared in C#, contracts with their Result values and declared errors, the generated TypeScript client, the page's start and the app's log. Use it when writing or changing a Spar app's contracts, Program.cs, or page code that calls the backend.
---

# Write a Spar app

A Spar app is one .NET project: a C# backend and a web page shown in a native window (WebView2 on Windows, WebKitGTK on Linux). The page calls the backend through **contracts**, C# classes or interfaces marked `[SparContract]`. The build generates a typed TypeScript client for each one.

Other skills cover the rest: `spar-dev-loop` (running the app, devtools, `spar dev`, `spar reload`, `spar snap`, `spar doctor`), `spar-testing` (tests) and `spar-shipping` (publishing, telemetry).

## The project

```text
MyApp/
  MyApp.csproj                  <Project Sdk="Spar.Sdk">, SparAppId, SparPlatforms
  Program.cs                    the one SparApplication.Create and Run
  Greetings/GreetingService.cs  a contract; one folder per feature
  MyApp.contracts.snapshot      written by the build; commit it
  Properties/launchSettings.json  the launch profiles Desktop and Loopback
  frontend/                     an ordinary Vite + TypeScript project
    package.json                "@spar/app": "file:../obj/spar/client"
    vite.config.ts              Spar's Vite plugin
    index.html
    src/
  obj/spar/client/              the generated client (the page's @spar/app package); never edit it
```

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
```

- `SparAppId` is required and checked by the build. It names the per-user folders: `%LOCALAPPDATA%\<app id>` on Windows and the XDG folders on Linux. A Debug build adds `.Development` to the id, so it never shares data with an installed copy. Replace `com.example` with a domain you own before you ship.
- `SparPlatforms` defaults to both. `Spar.Sdk` adds the host packages itself, so do not reference them.
- The frontend folder is `frontend/` unless `SparFrontendPath` names another one. `dotnet spar init frontend` wires a Vite frontend into an app that has none (`--framework react` or `vue` scaffolds it first).
- To serve contracts declared in a library, reference it as usual and also list its assembly name: `<SparContracts Include="MyApp.Contracts" />`. A referenced library that is not listed adds nothing the page can call.

The page depends on the generated client through a `file:` dependency. The build writes it before the page builds:

<!-- source: src/Spar.Templates/content/spar-app/MyApp/frontend/package.json -->
```json
  "dependencies": {
    "@spar/app": "file:../obj/spar/client",
    "react": "19.3.1",
    "react-dom": "19.3.1"
  },
```

Every Spar page uses the Vite plugin. It applies the page security that `Program.cs` declares to the bundle, adds the theme style in the window's colours, and styles the optional splash:

<!-- source: src/Spar.Templates/content/spar-app/MyApp/frontend/vite.config.ts -->
```ts
import react from "@vitejs/plugin-react";
import spar from "@spar/app/vite";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react(), spar()],
});
```

`spar({ splash: { delay, minimumVisible, fadeIn, fadeOut } })` changes the splash timing (in ms; the defaults are 350, 400, 120 and 150). Commit `frontend/package-lock.json`, because the build installs exactly what it lists.

## `Program.cs`

`Program.cs` holds exactly one `SparApplication.Create` and one `Run`, directly in the top-level statements:

<!-- source: src/Spar.Templates/content/spar-app/MyApp/Program.cs -->
```csharp
using Spar;

var app = SparApplication.Create(args, spar =>
{
    // The page's own background colours (frontend/src/styles.css), so the window shows them from its first frame.
    spar.Window(w => w
        .Background(light: "#ffffff", dark: "#1d1f24")
        .Size(900, 600));
});

return app.Run();
```

Services use Microsoft.Extensions.DependencyInjection through `spar.Services`. Each command call gets its own scope:

<!-- source: samples/Notebook/Program.cs -->
```csharp
    spar.Window(w => w
        .Background(light: "#ffffff", dark: "#1d1f24")
        .Size(1100, 720)
        .MinSize(640, 420));

    // A page still being written keeps the window until the user decides (docs/app-model.md 6.11).
    spar.OnCloseRequested((request, services) => services.GetRequiredService<IPageStore>().IsWriting
        ? CloseDecision.Ask("A page is still being saved. Close anyway?")
        : CloseDecision.Close);

    spar.Services.AddSingleton(TimeProvider.System);
    spar.Services.AddSingleton<IPageStore, MarkdownPageStore>();
```

**Some declarations are read by the build, so they must be constants.** They are the window colours (`Background(light, dark)`, as `#rgb` or `#rrggbb`; SPAR106 when the build cannot read them), `spar.ContentSecurity(…)` and `spar.Permissions(…)`. Write each as its own statement directly in `Create`, with literal values. A call in a condition or a helper is SPAR101, a value that is not a constant is SPAR102, and a shape the build cannot read is SPAR103. `Size` and `MinSize` may be computed. Every other call runs at start-up and may use run-time values.

Class contracts are registered by the generated code. For an interface contract, register its implementation yourself, for example `spar.Services.AddScoped<IConnections, ConnectionsApi>()`. Without one, the app does not start, and the error names the contract.

### Page security

The page starts with a strict Content Security Policy, and every powerful browser feature is denied. Loosen a default only where the page needs it:

- **Sources:** `spar.ContentSecurity(csp => csp.ImgSrc("data:").FontSrc("https://fonts.gstatic.com"))`. The directive methods are `FontSrc`, `ImgSrc`, `ConnectSrc`, `MediaSrc`, `ScriptSrc`, `StyleSrc` and `FrameSrc`. They take hosts and schemes only, and `FrameSrc` also takes `'self'`. A keyword there is SPAR104, and its message names the method to use instead.
- **Risky opt-ins**, each documented with its risk and a safer alternative: `AllowInlineStyles()`, `AllowEval()`, `AllowWebAssembly()`, `StyleNonce()`, `WithoutTrustedTypes()` and `WithoutIntegrity()`. Prefer the alternative. For example, styles added from script should use constructed stylesheets. Under Trusted Types, pass a worker's URL through a policy (`trustedTypes.createPolicy(…)`) before `new Worker(url)`.
- **Violation reports:** `csp.Reporting(listener: CspReports.Always, endpoint: CspReports.DevelopmentOnly)`. Each channel takes `Always` (the default), `DevelopmentOnly` or `Off`. Violations go to the app's log, and in Development each one says which C# call would allow it.
- **Permissions:** `spar.Permissions(p => p.Allow(Permission.ClipboardRead))`. The values are `Camera`, `Microphone`, `Geolocation`, `ClipboardRead`, `Sensors`, `LocalFonts`, `Midi`, `WindowManagement` and `Autoplay`. A declared feature is granted without a prompt, and everything else is denied.
- **The browser's own defaults** stay locked unless `spar.Browser(b => …)` opts out by name: `AllowDevTools()`, `AllowContextMenu()`, `AllowDownloads()`, `AllowPasswordSaving()`, `AllowAcceleratorKeys()`, `AllowHistoryNavigation()`, `AllowPagePicker()`, `AllowBrowserEnvironmentOverrides()` and, on Linux, `HardwareAcceleration(…)`. Every window shares one profile, so each opt-in applies to the whole app.

Spar logs every override at start-up, and `dotnet spar doctor` lists them.

### Other configuration

- `spar.Folders(f => f.Data(…).Logs(…).Profile(…))` moves folders away from their per-user defaults. `spar.ResolveFolders()` returns the folders the services will get, for things `Program.cs` builds itself. Call `Folders` first, because it throws after `ResolveFolders()`. In app code, inject `SparFolders` (`DataRoot`, `Data`, `Logs`, `Cache`, `Profile`) and never hard-code a path.
- `spar.OnStartupProblem(problem => …)` shows your own message for a missing or broken prerequisite. Return `true` only when you showed one. The exit code is fixed by the problem's kind (3 to 9).
- `spar.OnPageUnresponsive((hang, services, ct) => …)` observes a hung page, for example to keep a record. Spar's own "Wait / Reload page" dialog still asks the user.
- `spar.OnRelaunch((relaunch, services) => …)` receives the arguments of a second launch. Spar has already focused the running window.
- `spar.Diagnostics(d => d.KeepStallProbe())` keeps the UI-thread stall probe, which only logs, in a Production build.
- `spar.Logging(…)` sets log levels (see "Logging" below). `spar.Telemetry()` is the OpenTelemetry layer (see `spar-shipping`).

## Contracts

A contract is a `[SparContract]` **sealed class** whose `[SparCommand]` methods the page can call, or an **interface** whose methods are all commands. A class is created from the call's DI scope for each call, so inject what it needs:

<!-- source: samples/Notebook/Pages/PagesService.cs -->
```csharp
/// <summary>The notebook's pages: create, read, list, save and delete, and the changes every window follows.</summary>
[SparContract]
public sealed class PagesService(IPageStore store, PageImageFiles images, PageChanges changes, TimeProvider time)
{
    // …
    [SparCommand]
    public async Task<Result<Page, PageError>> GetAsync(PageRef request, CancellationToken cancellationToken)
    {
        var page = await store.FindAsync(request.Id, cancellationToken);
        return page is null ? new PageError.NotFound(request.Id) : page;
    }

    [SparCommand]
    public Task<IReadOnlyList<PageSummary>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);
    // …
    [SparCommand]
    public async Task<Result<PageError>> DeleteAsync(PageRef request, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(request.Id, cancellationToken);
        if (deleted)
        {
            images.DeleteAll(request.Id);
            changes.Publish(new PageChange(request.Id, PageChangeKind.Deleted, Revision: 0));
        }

        return deleted ? Result.Success : new PageError.NotFound(request.Id);
    }
```

- **Shape:** a command takes zero or one request, then an optional `CancellationToken`. The request is a positional record or a scalar such as `string name`. A list or an array goes inside a record.
- **Returns:** `Task`, `Task<T>`, `Task<Result<T, TError>>`, `Task<Result<TError>>`, or their `ValueTask` forms; or a stream (see "Streams and subscriptions" below). A command is never synchronous. If it has nothing to await, return `Task.FromResult(…)`.
- **Conversions:** `return page;` and `return new PageError.NotFound(id);` both convert to the `Result` implicitly. A command with errors but no value returns `Result.Success`.
- **Class methods without `[SparCommand]`** stay C#-only. On an interface contract, `[SparCommand("name")]` only overrides a command's name.
- **`ISparCall`** can be injected into the contract or into any scoped service of the call. It gives the call's token, id, names and `Caller` (`Page`, `Test` or `Devtools`). A singleton cannot depend on it.

### Streams and subscriptions

A command that returns `IAsyncEnumerable<T>` is a **stream**: the page reads its items as they come, and a slow page slows the producer. `IAsyncEnumerable<Result<T, TError>>` ends with its first declared error. With `[SparSubscription]` it is a **subscription**: the producer never waits, and a page that falls behind loses the oldest values (`Capacity`, 256 by default) and sees a `gap`; `LatestOnly = true` keeps only the newest value. A subscription subscribes again by itself after a lost connection unless it is `Resume = SubscriptionResume.Manual`, which one with side effects must be:

<!-- source: samples/Notebook/Pages/PagesService.cs -->
```csharp
    /// <summary>
    /// Every page created, saved or deleted from now on, by any window: the list and the open page follow it. Observation
    /// only, so it resumes by itself after a lost connection, with a reset that has the page read the list again.
    /// </summary>
    [SparCommand]
    [SparSubscription]
    public IAsyncEnumerable<PageChange> ChangesAsync(CancellationToken cancellationToken) => changes.WatchAsync(cancellationToken);
```

In the page, every item is a `Result`, read with `match`; a subscription's `events.reset` is required, and the abort of the signal stops it:

<!-- source: samples/Notebook/frontend/src/notebook.ts -->
```ts
  useEffect(() => {
    const live = new AbortController();
    void (async () => {
      for await (const item of pages.changes({ signal: live.signal })) {
        item.match({
          success: change => {
            void refresh();
            void follow(change);
          },
          events: { reset: () => void refresh() },
          // Our own abort when the notebook goes away; any other end leaves the list as it is until the next action.
          failure: () => undefined,
        });
      }
    })();
    return () => live.abort();
  }, [refresh, follow]);
```

- **Failures are in-band:** the last item of a stream that failed is its failure, so a `for await` loop cannot miss it. Leaving the loop early (`break`) cancels the stream and waits for the backend to confirm.
- **Tests:** the typed test client returns an `IAsyncEnumerable` of the values, of `Result`s, or of `SparSubscriptionItem`s (a value, an error or a `SparSubscriptionEvent`); `app.DropChannel()` and `app.RestoreChannel()` test a resume. Page tests script one with `spar.stream(pages.changes).push(…)`.

### Settings, binary data, files and secrets

Settings records (`spar.Settings(defaults)`, `ISparSettings<T>`), the installation file beside the executable, and binary data from the backend (`SparBlob` handles and `[SparResource]` addresses) are in [references/settings-and-binary-data.md](references/settings-and-binary-data.md). Above about 64 KB, never return a `byte[]`.

Files the user chooses or drops (grants, dialogs, drop areas), the backend's clipboard and credentials are in [references/files-clipboard-credentials.md](references/files-clipboard-credentials.md). The page never sees a path, and a secret never goes in settings.

### Requests, results and errors

Requests and results are positional records. Errors are C# 15 `closed record` hierarchies with nested sealed cases:

<!-- source: samples/Notebook/Pages/PageTypes.cs -->
```csharp
/// <summary>A new page. Without Markdown it starts with its title as a heading.</summary>
public sealed record CreatePage(string Title, string? Markdown);
// …
/// <summary>Why a page operation did not happen.</summary>
public closed record PageError
{
    /// <summary>Another page already has this title (titles name the page's file).</summary>
    public sealed record TitleTaken(string Title) : PageError;
// …
    /// <summary>The page changed since the editor loaded it; saving now would overwrite that change.</summary>
    public sealed record ChangedElsewhere(long CurrentRevision) : PageError;
}
```

- **Wire types:** records, primitives, nullable values, arrays and `IReadOnlyList<T>`, nested records, enums and `closed record` errors. In TypeScript, `long` becomes `bigint`, `DateTimeOffset` and `TimeSpan` become strings, `string?` becomes `string | null`, and `IReadOnlyList<T>` becomes `readonly T[]`.
- **Documentation comments** carry over as JSDoc on the generated client, so write them for the page's author.
- **The page must handle every case of a command's error type.** Give a command its own error type when its shared one has cases that cannot happen for it. A case cannot be shared between two error types, so declare it in each.
- Two types with the same name in one contract need `[SparName("…")]`.

### Reading a `Result` in C#

`Result` is Spar's own type. Never declare it. Read it with one exhaustive `switch` over the success type and every error case, with no default arm. A missing case is SPAR055, an error even when warnings are not errors:

<!-- source: samples/Notebook.Tests/PagesTests.cs -->
```csharp
    private static Page Value(Result<Page, PageError> result) => result switch
    {
        Page page => page,
        PageError error => throw new Xunit.Sdk.XunitException($"Expected a page, got {error}."),
    };
```

`IsSuccess`, `IsError`, `TryGetValue` and `TryGetError` are also there. `Value` and `Error` throw when the result holds the other state. **Never create a `default` Result**, whether with `default`, `new()`, an array created without values, `FirstOrDefault`, or a field left unassigned: every member throws on it, and SPAR050–SPAR054 report these. A member that may be empty is declared `Result<…>?`.

### Names

| C# | TypeScript |
|---|---|
| class `PagesService` | `pages`, imported from `@spar/app/pages` |
| class `GreetingService` | `greeting` |
| interface `IConnections` | `connections` |
| method `CreateAsync` | `create` |
| `[SparCommand("forget")] RemoveAsync` | `forget` |
| error case `PageError.TitleTaken` | `titleTaken` |

Classes keep their name, interfaces drop one leading `I`, and both drop a trailing `Service` and `Async`. The result is in acronym-aware camelCase (`IOStatsService` becomes `ioStats`). Names are ASCII. Two contracts whose names differ only in case are an error, and so is a name that `@spar/app` reserves (`sdk`, `vite`, `windows`) or a JavaScript reserved word. Give such a contract a different class name or `[SparContract("name")]`.

### The contract snapshot

The build writes `<Project>.contracts.snapshot` beside the project: the contracts as the page sees them. **Commit it with the change that updates it.** A CI build (`ContinuousIntegrationBuild=true`) fails with SPAR100 when it is missing or out of date, so a review shows every change the page will see.

<!-- source: samples/Notebook/Notebook.contracts.snapshot -->
```text
# The contracts of Notebook as pages see them. Written by the build; commit it with the change.
# CI builds fail with SPAR100 when it is out of date.

fingerprint aeccc8efeda54c88

contract images (Notebook.Pages.ImagesService)
  add(Notebook.Pages.AddImage) -> Notebook.Pages.ImageRef | Notebook.Pages.ImageError
  image(Notebook.Pages.ImageRef) -> resource of Spar.SparBlob?
```

## Calling the backend from the page

Each contract is a module, `import { pages, type Page } from "@spar/app/pages"`, and the SDK is `@spar/app/sdk`. A command returns a `Call<T, E>`: a `Promise` of an opaque `Result` that has two methods, `match` and `unwrap`. **You cannot read the value any other way.**

<!-- source: src/Spar.Templates/content/spar-app/MyApp/frontend/src/greet.ts -->
```ts
import { greeting } from "@spar/app/greeting";

/** What the page shows once the backend has answered for `name`: its greeting, or why there is none. */
export function greet(name: string): Promise<string> {
  return greeting.greet(name).match({
    success: result => result.message,
    errors: {
      nameMissing: () => "Type a name first.",
    },
    failure: error => `The app did not answer (${error.code}).`,
  });
}
```

- **`errors`** has one handler per declared case, and `tsc` fails on a missing or misspelled case. A command without declared errors has no `errors` property:

<!-- source: samples/Notebook/frontend/src/notebook.ts -->
```ts
  const refresh = useCallback(async () => {
    await pages.list().match({
      success: list => setSummaries([...list].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))),
    });
  }, []);
```

- **`failure`** handles infrastructure failures, which are never declared errors. As a function, it handles every failure. As an object by kind (`{ unavailable: … }`), it handles only the kinds it lists. Failures that nothing handles go to the app-wide `onFailure`, and `match` returns `undefined` for them.
- **`unwrap()`** returns the value or throws `SparError` (exported from `@spar/app/sdk`). It never calls `onFailure`. Use it only where exceptions suit the code.
- **Cancel** a call with `{ signal }` as its last argument (for example `pages.get({ id }, { signal })`). Aborting ends the wait, but the call reports `cancelled` only when the backend confirms it.

<!-- source: samples/Notebook/frontend/src/notebook.ts -->
```ts
    const message = await pages.create({ title, markdown: null }).match({
      // …
      errors: {
        titleTaken: error => `There is already a page called “${error.title}”.`,
        invalidTitle: error => error.reason,
        notFound: () => "The page could not be created.",
        changedElsewhere: () => "The page could not be created.",
      },
      failure: () => "The notebook could not be reached. Try again in a moment.",
    });
```

A **`CallError`** has `contract`, `command`, `kind`, `code` and `execution`. When the command threw, it also has `correlation`, the id to quote from the app's log. In Development it also has `detail`, which is for developers only and never for users. The kinds are `cancelled`, `unavailable`, `rejected`, `failed`, `transport`, `response` and `protocol`. `execution` is `notStarted`, `stopped`, `completed` or `unknown`. **Nothing is ever retried for you.** When the outcome is `unknown`, tell the user to check before trying again.

Set the app-wide hooks once with `configureSpar`. `onFailure` gets the failures that no call handled, and `observe` sees every failure. Neither can retry a call or change a result:

<!-- source: samples/Notebook/frontend/src/spar.ts -->
```ts
export function startSpar(onFailure: (error: CallError) => void): { ready(): void } {
  configureSpar({
    onFailure: error => {
      const call = `${error.contract}.${error.command}`;
      console.error(`Notebook: ${call} failed: ${error.kind} / ${error.code} (${error.execution})`);
      onFailure(error);
    },
  });
  return connectSpar(hostChannel(), fingerprint);
}
```

<!-- source: samples/Notebook/frontend/src/spar.ts -->
```ts
export function describeFailure(error: CallError): string {
  switch (error.kind) {
    case "unavailable":
    case "transport":
      return error.execution === "notStarted"
        ? "The notebook is not reachable right now. Nothing was changed."
        : "The connection to the notebook was lost. Your last change may not have been saved.";
    case "cancelled":
      return "The action was cancelled.";
    default:
      return `Something went wrong (${error.code}). Your notes are safe on disk.`;
  }
}
```

## The page's start

Connect once, before rendering, then report readiness after the first view has rendered:

<!-- source: src/Spar.Templates/content/spar-app/MyApp/frontend/src/main.tsx -->
```tsx
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { StrictMode, useEffect } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App.js";
import "./styles.css";

// Connects the page to the app's backend: through the window in the app, through loopback in browser tests.
const connection = connectSpar(hostChannel(), fingerprint);

function Root() {
  // The page is usable once its first render is on screen: the splash, if index.html has one, goes, then ui-ready.
  useEffect(() => connection.ready(), []);

  return <App />;
}
```

- `hostChannel()` picks the window's bridge, or loopback when the app is served to a browser (`dotnet run -lp Loopback`). `fingerprint` ties the page to the contracts it was built against.
- **`connection.ready()`** is the page's only way to say it is usable. The host's `ui-ready`, which tests and tools wait for, follows it. Call it once the first view is on screen, or once the first data has arrived if the page waits for it. A page that never calls it never reports ready.
- **The splash** is optional static HTML in `index.html`, in an element with the id `spar-splash`. The plugin turns it into a cover in the window colour and fades its content in only when the start is slow. `ready()` removes it. Style its content in your stylesheet, without `style` attributes:

<!-- source: samples/Notebook/frontend/index.html -->
```html
    <div id="spar-splash" role="status" aria-label="Opening the notebook">
      <svg class="splash-mark" width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="currentColor"
        stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <rect x="5" y="3" width="14" height="18" rx="2" />
        <path d="M9 3v18" />
        <path d="M12.5 8h3.5M12.5 11.5h3.5M12.5 15h2.5" />
      </svg>
      <p class="splash-name">Notebook</p>
    </div>
    <div id="root"></div>
```

- **Theme:** keep the page's background colours equal to `spar.Window(w => w.Background(light, dark))`, so the window and the page match from the first frame. Follow `prefers-color-scheme` in CSS. `setColorScheme("light" | "dark" | "system")` from `@spar/app/sdk` sets the app's own choice. The window, its title bar and the page change together, and the next start remembers the choice. Its declared error `notSaved` means the choice applies now but was not saved for the next start.
- **Backend updates while developing:** after Hot Reload changes C# code, `onBackendUpdate` lets the page fetch its data again. A production build never calls it:

<!-- source: samples/Notebook/frontend/src/notebook.ts -->
```ts
  useEffect(() => onBackendUpdate(() => void refresh()), [refresh]);
```

## Closing the window

Closing asks the page's guard first, then the backend's guard (`spar.OnCloseRequested` in `Program.cs`, shown above). The window closes only when both agree. Each guard answers close, keep, or ask, and an ask shows one native confirmation. Register the page's guard from `@spar/app/windows`:

<!-- source: samples/Notebook/frontend/src/notebook.ts -->
```ts
  useEffect(() => windows.onCloseRequested(async request => answerClose(request, await flushDraft())), [flushDraft]);
```

<!-- source: samples/Notebook/frontend/src/notebook.ts -->
```ts
export function answerClose(request: CloseRequest, saved: SaveState): CloseAnswer {
  if (saved.kind === "saved" || request.automated || request.reason === "sessionEnd") {
    return "close";
  }
```

When the OS session ends, or when tests or an agent close the app (`request.automated`), nobody can answer. In those cases, save and close: an ask counts as close. At session end, the guards together get at most about 3 s. The backend's guard runs off the UI thread and may be async: `(request, services, cancellationToken) => ValueTask<CloseDecision>`.

## Logging

Inject `ILogger<T>` as usual. One file, `logs/app.log` in the app's per-user folder, gets Spar's lines and every logger of the app's services from the host's first moment:

<!-- source: samples/Notebook/Pages/MarkdownPageStore.cs -->
```csharp
public sealed class MarkdownPageStore(SparFolders folders, ILogger<MarkdownPageStore> logger) : IPageStore
{
    private const string Fence = "---";

    private static readonly Action<ILogger, string, Exception?> Skipped = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, "PageSkipped"),
        "Skipped {File}: not a Notebook page (it needs a front matter with title, revision and updated)");

    private readonly string _folder = Path.Combine(folders.Data, "pages");
```

- **Levels:** Information and above in Production, and Debug and above in Development and Test builds. Change them with `spar.Logging(l => l.Level(LogLevel.Information).Level("MyApp.Greetings", LogLevel.Debug))`. A category level also applies to every category it is a prefix of. `l.WithoutFile()` drops the file, but then support has no local log to ask for.
- **On a user's machine,** a `logging.json` in the per-user folder, such as `{ "level": "Debug" }`, overrides the levels at start-up without a new build.
- **The page's errors,** that is `console.error`, uncaught exceptions and unhandled rejections, reach the same log under `Spar.Page`, grouped and rate-limited. So log a failure only the page saw with `console.error`, as `startSpar` does above.
- Spar reads no `appsettings.json`. An app that wants one adds Microsoft.Extensions.Configuration itself. See [Logging in .NET](https://learn.microsoft.com/dotnet/core/extensions/logging).
- Keep secrets out of log lines, because users send the log to support.

## Rules

- **Results are opaque.** In TypeScript, use only `match` or `unwrap`. In C#, use one exhaustive `switch` with no default arm.
- **Never create a `default` Result.** Return a value, an error case or `Result.Success`.
- **Handle every declared error by name.** Leave `failure` out only when `onFailure` should handle the failure.
- **Never block the window's UI thread.** Commands and guards already run off it. Keep them async all the way (no `.Result`, `.Wait()` or `Thread.Sleep`), and use async I/O. Hooks and guards should return promptly.
- **Take a `CancellationToken`** in every command that does I/O or waits, and pass it on. The token fires when the page aborts the call, the page goes away, or the app shuts down. A command that ignores it ends as "outcome unknown", never as "cancelled". Cancellation does not prove the work stopped.
- **Never replay a command automatically**, on the page or in C#. Let the user decide, using `execution`.
- **Keep the declarations the build reads constant** (window colours, `ContentSecurity`, `Permissions`), each directly in `Create`.
- **Loosen a security default only by its named option**, and only where the page needs it.
- **Commit** `<Project>.contracts.snapshot` and `frontend/package-lock.json`. Never edit `obj/spar/client`.

## Commands

Run these from the app's folder:

```sh
dotnet build                 # builds, generates the client, type-checks the page, updates the snapshot
dotnet run                   # the window (launch profile Desktop)
dotnet run -lp Loopback      # the same app served to a browser, no window (Debug only)
dotnet watch                 # page edits in place, C# method bodies by Hot Reload, contract changes by restart
dotnet test                  # the C# tests (see spar-testing)
dotnet spar doctor           # checks the machine, the project and the last build
```

For `dotnet spar dev`, `reload`, the devtools commands and `snap`, see `spar-dev-loop`. For publishing, see `spar-shipping`.

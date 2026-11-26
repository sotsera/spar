---
name: spar-testing
description: Tests a Spar desktop app at every level, from fast command tests through the generated C# clients and page tests with Vitest to browser, window and black-box tests and scenes for spar snap. Use when writing, running or fixing the tests of a Spar app, or when choosing which kind of test a behaviour needs.
---

# Testing a Spar app

A Spar app is tested from its own `Program.cs`. The tests start the real app inside the test process, with its real composition, dispatch and serializers, and change only what each test needs. Only the black-box tests launch the shipped executable.

The examples come from the `dotnet new spar-app` template, where the app is called `MyApp` (the template puts your app's name in its place), and from the Notebook sample. For contracts, `Result` values and `Program.cs` see `spar-app`. For `spar dev` and the devtools see `spar-dev-loop`. For publishing and CI see `spar-shipping`.

## The test projects

Keep this shape; do not add test projects per feature:

| Project | What runs there | How it runs |
|---|---|---|
| `MyApp.Tests` | Command tests: commands through the generated typed clients, no page. | Every `dotnet test`. |
| `MyApp/frontend` | Page tests: the page's code in Node with Vitest, against a fake backend. | `npm test` in `frontend/`. |
| `MyApp.EndToEndTests` (optional) | `Browser/`, `Window/` and `BlackBox/` tests, and scenes. | One `dotnet test` per `Surface` trait. |

Choose the cheapest level that shows the behaviour:

- A rule of the backend gets a command test.
- What the page makes of an answer gets a page test.
- A journey through the real page gets a browser or window test.
- Only what the shipped executable alone shows (a clean start and close, the files it writes, a second launch, a start-up failure and its exit code) gets a black-box test.

Each test project names the app it tests:

<!-- source: src/Spar.Templates/content/spar-app/MyApp.Tests/MyApp.Tests.csproj -->
```xml
<ItemGroup>
  <SparTestedApplication Include="..\MyApp\MyApp.csproj" />
</ItemGroup>
```

The end-to-end project also turns on the black-box publish and the Browser surface. It keeps Playwright and the browser tests out of the NativeAOT lane, where Playwright cannot run:

<!-- source: src/Spar.Templates/content/spar-app/MyApp.EndToEndTests/MyApp.EndToEndTests.csproj -->
```xml
  <!-- The black-box tests: the build publishes the app's Production executable beside the tests (Projects.MyApp). -->
  <SparBlackBoxTests>true</SparBlackBoxTests>
<!-- … -->
<!-- The Browser surface, which Spar.Sdk adds, and Playwright to drive Chromium. -->
<PropertyGroup Condition="'$(SparTestLane)' != 'Aot'">
  <SparBrowserTests>true</SparBrowserTests>
</PropertyGroup>

<ItemGroup Condition="'$(SparTestLane)' != 'Aot'">
  <PackageReference Include="Microsoft.Playwright" Version="1.64.1" />
</ItemGroup>

<ItemGroup Condition="'$(SparTestLane)' == 'Aot'">
  <Compile Remove="Browser\**" />
</ItemGroup>
```

Both test projects use xUnit v3 on Microsoft.Testing.Platform (`global.json` names the runner). They have `[assembly: CaptureConsole]` in `TestSettings.cs`, so what the app writes to the console shows up in each test's own output. Warnings are errors, so xUnit1051 requires you to pass `TestContext.Current.CancellationToken` wherever a call takes a token. Results go to each test project's `obj/test-results/`.

## The test host: `SparTestApp`

`SparTestApp.StartAsync<Program>(configure, token)` runs the app's top-level statements on a thread of their own, inside the test process. `SparApplication.Create` sees the test's session, and `Run` builds the app and waits until the test disposes it. Always write `await using var app = …`.

- **A data root per app:** each app gets a fresh, disposable root of its own. It lives under the system's temporary folder, or under an `artifacts/` folder beside `global.json` when the repository has one. `app.Folders` names its folders (`Data`, `DataRoot`). Disposal deletes the root unless you set `o.KeepData = true`. The real per-user folders are never touched, so two apps in one test stay apart.
- **Service overrides** are ordinary DI. `o.Services(s => …)` runs after the app's registrations and Spar's defaults, so `Replace` wins. `o.Time(timeProvider)` replaces the app's clock and Spar's own.
- **External dependencies are blocked by default.** A service the app registers with `services.AddExternal<TService, TImplementation>()` (a database, a remote API, a file share) throws `SparExternalDependencyException` when a test resolves it without replacing it. Replace it with a fake in `o.Services`, or opt in to the real one with `o.AllowExternal<TService>()`.
- **What to inspect:**
  - `app.Services`: the app's own provider, for checking backend state directly.
  - `app.Log`: what Spar logged, such as a command that threw or a refused call.
  - `app.Problems`: what the bridge refused or dropped, CSP violations and page errors. A clean run has none, so end every page-driving test with `Assert.Empty(app.Problems)`.
- **`o.Surface`** picks what sits in front of the backend: `None` (the default, commands only), or `Browser` or `Window` (end-to-end project only).

Never add test-only code paths, switches or configuration files to the app. Override services from the test instead.

## Command tests

The app's Test build, which the test project references, has a typed C# client for each contract. The client is named after the contract without its trailing `Service` (or an interface's leading `I`): `GreetingService` becomes `GreetingClient`, `PagesService` becomes `PagesClient`. Pass it the app. Each command is an `…Async` method that returns the command's `Result` (`result.Value`, or the declared error in `result.Error`) through the real dispatch and serializers. An infrastructure failure throws `SparCallFailedException`.

<!-- source: src/Spar.Templates/content/spar-app/MyApp.Tests/GreetingTests.cs -->
```csharp
using MyApp.Greetings;
using Spar.Testing;

namespace MyApp.Tests;

/// <summary>Command tests: the app's own Program.cs in this process, the command through the generated typed client.</summary>
public sealed class GreetingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_name_is_greeted_without_its_spaces()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        var result = await new GreetingClient(app).GreetAsync("  Ada ", Token);

        Assert.Equal(new Greeting("Hello, Ada!"), result.Value);
    }
// …
}
```

The Notebook pins the clock with a service override in one helper. Its tests then read the files a command wrote under `app.Folders.Data`:

<!-- source: samples/Notebook.Tests/PagesTests.cs -->
```csharp
private static Task<SparTestApp> StartAsync(TimeProvider? time = null) =>
    SparTestApp.StartAsync<Program>(
        options => options.Services(services => services.Replace(ServiceDescriptor.Singleton(time ?? new FixedTime(Morning)))),
        Token);
```

To check something the typed client cannot send, such as a malformed request, call the real dispatch with raw JSON through `app.InvokeAsync("pages", "create", bytes, token)`. It returns a `SparOutcome`, whose `Failure` has a `Kind`, a `Code` and an `Execution`, such as `rejected` / `invalid-request` / `notStarted`.

## Page tests

Page tests run the page's code in Node with Vitest, against `fakeSpar()` from `@spar/app/testing`. That module is part of the generated client, so build the app once before the first `npm test`. Each test creates its own fake and scripts the commands it uses. It then checks what the page made of the answers and what the page sent:

<!-- source: src/Spar.Templates/content/spar-app/MyApp/frontend/src/greet.test.ts -->
```ts
import { greeting } from "@spar/app/greeting";
import { fakeSpar } from "@spar/app/testing";
import { expect, test } from "vitest";
import { greet } from "./greet.js";

test("the backend's greeting is shown as it is", async () => {
  const spar = fakeSpar();
  spar.on(greeting.greet).succeed({ message: "Hello, Ada!" });

  expect(await greet("Ada")).toBe("Hello, Ada!");
  expect(spar.calls(greeting.greet)).toEqual(["Ada"]);
});

test("a missing name asks for one", async () => {
  fakeSpar().on(greeting.greet).fail("nameMissing");

  expect(await greet(" ")).toBe("Type a name first.");
});

test("a lost connection says the app did not answer", async () => {
  fakeSpar().on(greeting.greet).failWith({ kind: "transport", code: "lost", execution: "unknown" });

  expect(await greet("Ada")).toBe("The app did not answer (lost).");
});
```

- **Scripting:** `spar.on(command)` takes the generated command itself, so a wrong error case or value type fails `tsc`. The options are:
  - `succeed(value)`
  - `fail(case, payload)`
  - `failWith({ kind, code, execution })`
  - `hang()`: the call never answers, and aborting it cancels it.
  - `answer((request, reply) => …)`: decides per call. The handler may wait, so a test can check what the page shows while a call is on its way.

  Each script replaces the previous one for that command.
- **Real wire:** scripted values and `spar.calls(command)` go through the generated encoders and decoders, so `bigint`, bytes, enums and `null` behave as they do in the app.
- **A command nobody scripted** is refused (`rejected` / `unknown-command`), with a console error that names it.
- **Also there:** `spar.requestClose({ automated, reason })` asks the page's close guards, and `spar.backendUpdate({ types })` sends a Hot Reload notice.
- `fakeSpar()` resets `configureSpar`'s hooks. Production bundles never contain `@spar/app/testing`.

React hooks need a DOM. The Notebook's `vitest.config.ts` adds the React plugin, `environment: "happy-dom"` and `.tsx` files to the template's config, and tests its hooks inside `act`:

<!-- source: samples/Notebook/frontend/src/notebook.test.tsx -->
```tsx
test("closing the window saves the draft first, then lets it close", async () => {
  spar.on(pages.save).succeed({ ...welcome, markdown: "# Before closing", revision: 4n });
  await start();
  await openWelcome();

  act(() => notebook.edit("# Before closing"));
  let answer: unknown;
  await act(async () => {
    answer = await spar.requestClose();
  });

  expect(answer).toBe("close");
  expect(spar.calls(pages.save)).toEqual([{ id: "welcome", markdown: "# Before closing", baseRevision: 3n }]);
});
```

`dotnet test` does not run page tests; run `npm test` in `frontend/`.

## Browser tests

In a browser test, the app's own packed page runs in headless Chromium, driven by Playwright. It talks to the real backend in the test process over a loopback host. `o.Page()` serves the packed page, and `app.PageUrl()` is the address to open (its fragment carries the session token). The class carries `[Trait("Surface", "Browser")]`:

<!-- source: src/Spar.Templates/content/spar-app/MyApp.EndToEndTests/Browser/GreetingBrowserTests.cs -->
```csharp
[Trait("Surface", "Browser")]
public sealed class GreetingBrowserTests(SharedBrowser shared) : IClassFixture<SharedBrowser>
{
    [Fact]
    public async Task The_page_greets_a_name_through_the_backend()
    {
        await using var app = await SparTestApp.StartAsync<Program>(o =>
        {
            o.Surface = SparTestSurface.Browser;
            o.Page();
        }, TestContext.Current.CancellationToken);
        await using var context = await shared.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.PageUrl().ToString());
        await page.GetByLabel("Name").FillAsync("Ada");
        await page.GetByRole(AriaRole.Button, new() { Name = "Greet" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Hello, Ada!");
        Assert.Empty(app.Problems);
    }
}
```

- `SharedBrowser` (in `Browser/`) is a class fixture that starts one Chromium, and each test opens its own context. Because it is a class fixture, a run of only the window or black-box tests starts no browser.
- Besides `app.Problems`, collect the page's console errors and `PageError`s and assert there are none, as the Notebook's `NotebookPage.AssertClean()` does. Linux's headless Chromium logs one harmless warning about `bluetooth` in Spar's `Permissions-Policy`. Accept that message and no other.
- Playwright needs its Chromium once per machine. Build the project, then run its install script. Use `linux-x64` in the path on Linux, where `--with-deps` also installs the system libraries:

```sh
dotnet build MyApp.EndToEndTests
pwsh MyApp.EndToEndTests/bin/Development/Debug/net11.0/win-x64/playwright.ps1 install chromium
```

## Window tests

A window test opens the app's real window in the test process, with its own packed page. On Windows the page is in WebView2, shown offscreen, never activated and with no taskbar button. On Linux it is in WebKitGTK, on a private Xvfb that the test process starts for itself. Drive the page with `app.Page`, which uses Playwright's method names on Spar's own types (`SparPage`, `SparLocator`, `SparRole`, and `Expect` from `SparAssertions`):

<!-- source: src/Spar.Templates/content/spar-app/MyApp.EndToEndTests/Window/GreetingWindowTests.cs -->
```csharp
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace MyApp.EndToEndTests;
// …
[Trait("Surface", "Window")]
public sealed class GreetingWindowTests
{
    [Fact]
    public async Task The_window_greets_a_name()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);
        var page = app.Page;

        await page.GetByLabel("Name").FillAsync("Ada");
        await page.GetByRole(SparRole.Button, new() { Name = "Greet" }).ClickAsync();

        await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Hello, Ada!");
        Assert.Empty(app.Problems);
        Assert.Empty(page.PageErrors);
    }
}
```

- **Locators:** `GetByRole` (with `Name`, `NameRegex`, `Exact`, `Level`, `Checked`, `Pressed`), `GetByLabel`, `GetByText`, `GetByPlaceholder`, `GetByAltText` and `GetByTitle`. Chain them, and narrow them with `Filter`, `First`, `Last` and `Nth`. There are no test ids: find elements the way assistive technology names them, which also checks that they are named. Use `Locator(css)` only for structure that roles do not name.
- **Actions wait** up to 10 s until their element is attached, visible, stable, enabled and receives the pointer. An action needs exactly one element. A locator that matches several fails and lists them, so pick one with `First`. Input is trusted (`isTrusted`, with user activation). Send keys with `PressAsync("Control+End")` and `Keyboard.TypeAsync`.
- **Assertions retry** for 5 s unless you pass `Timeout`, and `Not` inverts them. A failure names what was searched for and what was found.
- **Also there:** `EvaluateAsync`, `ScreenshotAsync`, `ReloadAsync`, `GotoAsync`, `WaitForURLAsync`, `WaitForUiReadyAsync`, `Keyboard`, `Mouse`, `ConsoleMessages`, `PageErrors` and `Dialogs`. Windows use the light theme by default (`o.ColorScheme`), so screenshots look the same from machine to machine.
- **The trait is checked.** A test that starts the Window or Browser surface without the matching `Surface` trait fails at once and names the trait to add. The trait can sit on the method, its class or the assembly. It keeps a native engine crash from taking down the fast tests' whole run.

Put the start and the page's landmarks in a helper, as the Notebook does, and skip on OSes that have no Window surface:

<!-- source: samples/Notebook.EndToEndTests/Window/NotebookWindow.cs -->
```csharp
public static async Task<NotebookWindow> OpenAsync(Action<SparTestOptions>? configure = null)
{
    // The Window surface runs on Windows and Linux; elsewhere each journey is a skip. A machine without the window's
    // prerequisites (on Linux Xvfb, D-Bus, XTEST, WebKitGTK and user namespaces) fails the journey with the reason.
    Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "The Window surface runs on Windows and Linux.");
    var app = await SparTestApp.StartAsync<Program>(options =>
    {
        options.Surface = SparTestSurface.Window;
        configure?.Invoke(options);
    }, TestContext.Current.CancellationToken);
    return new NotebookWindow(app);
}
```

Visible content does not mean the page has finished starting. Before a `ReloadAsync`, wait for the splash to go and for `ready`. Otherwise the host ignores the old page's late `ready` with a warning, which ends up in `app.Problems`:

<!-- source: samples/Notebook.EndToEndTests/Window/NotebookWindow.cs -->
```csharp
public async Task WaitForReadyAsync()
{
    await Expect(Page.Locator("#spar-splash")).ToHaveCountAsync(0);
    await Page.WaitForUiReadyAsync();
}
```

**Closing:** `app.Window.CloseAsync()` closes the window the way its close button does, so the page's and the backend's close guards run. By default the close is automated, and a guard's question counts as "close". To test the question itself, turn that off and give the answer:

<!-- source: samples/Notebook.EndToEndTests/Window/CloseJourneyTests.cs -->
```csharp
var kept = await notebook.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.KeepOpen });

Assert.Equal((CloseOutcome.Kept, LostQuestion), (kept.Outcome, kept.Question));
await Expect(notebook.Editor).ToHaveValueAsync("# Draft\n\nlast thoughts");
await notebook.ScreenshotAsync("close-kept");

var closed = await notebook.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.CloseAnyway });

Assert.Equal((CloseOutcome.Closed, LostQuestion), (closed.Outcome, closed.Question));
```

## Black-box tests

A black-box test launches the app's exact Production executable with nothing replaced: no test host, no devtools, no service overrides. With `<SparBlackBoxTests>true</SparBlackBoxTests>`, the build publishes that executable once, beside the tests, and generates `Projects.MyApp` (with `AppId`, `ExecutablePath` and `ProjectDirectory`). The window is read and driven through the OS accessibility API (UI Automation on Windows, AT-SPI on Linux), never through input:

<!-- source: src/Spar.Templates/content/spar-app/MyApp.EndToEndTests/BlackBox/LaunchTests.cs -->
```csharp
[Trait("Surface", "BlackBox")]
public sealed class LaunchTests
{
    [Fact]
    public async Task The_published_app_answers_its_page_and_closes_cleanly()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await SparBlackBox.LaunchAsync(Projects.MyApp, cancellationToken: token);

        await app.Window.GetByRole(SparRole.Heading, new() { Name = "MyApp" }).WaitForAsync();
        await app.Window.GetByRole(SparRole.Button, new() { Name = "Greet" }).InvokeAsync();
        await app.Window.GetByText("Type a name first.").WaitForAsync();

        Assert.Equal(0, await app.CloseAsync(token));
        Assert.True(await app.WaitForProcessTreeExitAsync());
    }
}
```

- **Data roots:** each launch gets a disposable root, and the app's per-user folders are redirected into it through the environment (`LOCALAPPDATA`, `APPDATA` and `TEMP` on Windows, `HOME` and `XDG_*` on Linux). `app.DataRoot`, `AppFolder`, `DataFolder`, `LogsFolder` and `LogFile` are the folders the Production build uses there. Disposal ends whatever is still running, waits for every process to exit and deletes the root unless you set `o.KeepData = true`.
- **Ready** means the host's `ui-ready` line is in the app's log and the window has been found, within 30 s. For a launch that hands over to a running instance or fails, set `o.WaitForReady = false`.
- **The window:** `TitleAsync()`, and locators (`GetByRole` with `Name`, `Exact`, `NameRegex` and `Disabled`; `GetByText`; `First`, `Last` and `Nth`) with `WaitForAsync`, `CountAsync`, `IsVisibleAsync`, `NameAsync`, `AllNamesAsync`, `InputValueAsync` and `InvokeAsync`. An action needs exactly one element.
- **Exit codes:** `CloseAsync()` closes the app the way its close button does and returns the exit code. `WaitForExitAsync()` waits for an exit the app makes on its own, and `Kill()` ends it. `StandardError` holds what the app printed. Read a native message (a start-up failure, a failed relaunch) with `WaitForDialogAsync()`, which gives its `Title`, `Text` and `Buttons`, and answer it with `PressAsync(name)`.

A second launch on the same data, as when the same user starts the app again:

<!-- source: samples/Notebook.EndToEndTests/BlackBox/BlackBoxTests.cs -->
```csharp
[Fact]
public async Task A_second_launch_hands_over_to_the_running_instance_and_exits_0()
{
    SkipOffDesktop();
    await using var first = await SparBlackBox.LaunchAsync(Projects.Notebook, cancellationToken: Token);

    await using var second = await SparBlackBox.LaunchAsync(Projects.Notebook, o =>
    {
        o.DataRoot = first.DataRoot;
        o.Arguments.Add("--from-test");
        o.WaitForReady = false;
    }, Token);

    Assert.Equal(0, await second.WaitForExitAsync(Token));
    Assert.Contains("handed to the running instance", second.StandardError, StringComparison.Ordinal);
    await WaitForLogAsync(first, OperatingSystem.IsWindows() ? "relaunch with 1 arguments: window shown" : "Relaunch with 1 arguments: the window was presented");
    Assert.Equal(0, await first.CloseAsync(Token));
}
```

Keep black-box tests few: smoke checks and what spans processes. Everything else belongs to the faster levels.

## Scenes

A scene is a state of the app worth looking at. You write it in the end-to-end project as a static method marked `[SparScene("name")]`, which starts the app through the scene and brings the page to that state with the window-test API. `dotnet spar snap name` captures it into `obj/spar-snap/name/`: `page.png`, the accessibility tree, the bridge calls and the problems. `dotnet test` also runs every scene as a window test (trait `Surface=Window`, CoreCLR lane only), so a scene that no longer reaches its state fails:

<!-- source: samples/Notebook.EndToEndTests/Window/NotebookScenes.cs -->
```csharp
/// <summary>The new-page dialog with a title typed in.</summary>
[SparScene("new-page-dialog")]
public static async Task NewPageDialog(SparScene scene)
{
    var app = await scene.StartAsync<Program>();
    await app.Page.GetByRole(SparRole.Button, new() { Name = "New page" }).First.ClickAsync();
    var dialog = app.Page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
    await dialog.GetByLabel("Title").FillAsync("Groceries");
}
```

- A scene's name uses lower-case letters, digits and hyphens, once per project. The method is `static`, returns `Task` and takes one `SparScene`, and the build reports one that breaks these rules (SPAR110 to SPAR112). Inside a test class, make it `internal`.
- `scene.StartAsync<Program>(o => o.Services(…))` takes a window test's options. `scene.Time` (by default 15 January 2026, 09:30 UTC, then running in real time) and `scene.Size` (by default 1280 × 800) make the picture the same on every run on one machine.
- `dotnet spar snap --list` lists the scenes, and `dotnet spar snap <name>...` or `--all` captures them (`--no-build`, `--out` and `--json` are also there). Run `dotnet tool restore` once first. More in `spar-dev-loop`.

## Running the tests

From the solution's folder:

```sh
dotnet test --project MyApp.Tests
cd MyApp/frontend && npm test && cd ../..
dotnet test --project MyApp.EndToEndTests --filter-trait Surface=Browser
dotnet test --project MyApp.EndToEndTests --filter-trait Surface=Window
dotnet test --project MyApp.EndToEndTests --filter-trait Surface=BlackBox
```

Run each end-to-end kind as its own invocation, never all together and never mixed with the fast tests. Add `--report-xunit-trx` for TRX files in `obj/test-results/`.

**The NativeAOT lane** runs the same tests published with NativeAOT, as the shipped app runs:

```sh
dotnet publish MyApp.Tests -c Release -p:SparTestLane=Aot -o MyApp.Tests/bin/aot-tests
MyApp.Tests/bin/aot-tests/MyApp.Tests
```

On Windows the runner is `MyApp.Tests.exe`. `MyApp.EndToEndTests` publishes the same way for its window and black-box tests; browser tests and scenes stay out of this lane. The published runner exits 0 even when it ran no test, so check that its output reports a non-zero `Total:`, as the template's CI workflow does.

**On Linux**, window and black-box tests start their own private X server, D-Bus session and accessibility bus. The machine needs:

```sh
sudo apt install xvfb xauth dbus-daemon at-spi2-core
```

It also needs WebKitGTK and the unprivileged user namespaces that WebKitGTK's sandbox uses. Without them a black-box test skips and says what is missing, or fails when `CI=true`. GitHub's Ubuntu runner refuses those user namespaces, so the template's CI runs window and black-box tests on Windows only (see `spar-shipping`).

## Rules

- **Ask before you run window or black-box tests on a machine someone is using.** They are built never to take the person's focus (offscreen windows and a private desktop on Windows, a private Xvfb on Linux), but they start real browser engines and processes. On a CI runner (`CI` or `TF_BUILD` set), the black-box app shows on the runner's own desktop. Command and page tests are always safe to run.
- Do not drive the user's real desktop with simulated input to test the app. The test APIs send trusted input to the test's own window.
- Start every app with `await using` and pass `TestContext.Current.CancellationToken`. End page-driving tests with no `app.Problems` and no page errors.
- Replace external dependencies in tests. Use `AllowExternal` only in a test that is meant to reach the real resource.
- Start a fresh app in each test, so nothing one test stores reaches the next.

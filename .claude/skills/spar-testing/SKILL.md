---
name: spar-testing
description: "Write or change Spar's test host and tests: SparTestApp running an app's own Program.cs in the test process, service overrides and the safety defaults, the generated test entry point and typed test clients, the CoreCLR and NativeAOT lanes, test variants and results, and the C# side of command dispatch that tests drive."
---

# Test Spar apps and Spar itself

The design is plan section 5 (`docs/design/testing.md`); the mechanics are the decisions "Test strategy: the app runs inside the test process" and "Test host mechanics: the generated entry point". Contract and generator rules are in `spar-contract-generation`, build rules in `spar-msbuild`.

## The test host

- **A test runs the app's own `Program.cs` in the test process:** `await using var app = await SparTestApp.StartAsync<Program>(o => o.Services(s => …), TestContext.Current.CancellationToken);`. Never launch the app's executable for anything but the black-box level, and never add test code or a test configuration file to the app.
- **How it reaches the app:** in the `Test` variant only, Spar's generator makes `Program` public and an `ISparEntryPoint`, calling the top-level method through an unsafe accessor. The test sets its session in an `AsyncLocal` and runs the entry point on a thread of its own; `SparApplication.Create` reads the session behind the feature switch `Spar.TestHost.IsSupported`, and `Run` builds the app, hands it over, and waits for disposal.
- **Overrides are ordinary DI:** `options.Services(s => s.Replace(…))` runs after the app's registrations and Spar's defaults. Fakes are objects the test keeps and inspects; `app.Services` reaches the backend directly.
- **The Browser surface** (`o.Surface = SparTestSurface.Browser`) is its own package, `src/Spar.Testing.Browser`, the only one that references the loopback host (`src/Spar.Loopback`); only end-to-end projects reference it, so `Spar.Testing` and the NativeAOT lane carry no web server (decision "Loopback transport accepted; Browser surface in its own package"). Open `app.PageUrl()` (its fragment carries the token) in a Playwright context, one shared browser per run and a context per test. `o.Page()` serves the app's own packed page, as users get it; `o.Page(folder, prefix)` serves a test page that is not the app's. Assert `app.Problems` is empty: it lists what the bridge refused or dropped and the page's CSP violations. Playwright stays out of Spar's packages. Every page evaluation gets a deadline, because a call the host dropped is never answered.
- **Typed test clients are generated** per contract in the `Test` variant (`new PagesClient(app)`). They send the same request bytes a page sends and read the outcome with the app's own serializers, so a command test covers serialization and dispatch. An infrastructure failure throws `SparCallFailedException`; a raw call is `app.InvokeAsync(contract, command, bytes, token)`, which returns the `SparOutcome`.

## Safe by default

- **One data root per app instance,** under `artifacts/test-host/<run>/app-NNN/` in this repository, from which every folder derives (the app's own folder choices included). It is deleted after the test unless `KeepData` is set. Web engine data goes under `artifacts/webview2-profiles/` or `artifacts/linux-homes/` (`AGENTS.md`).
- **External dependencies:** an app registers real resources with `AddExternal<TService, TImpl>()`; in a test host, resolving one that the test neither replaced nor allowed (`AllowExternal<T>()`) fails and names it.
- **Spar's outward services** (clipboard, credentials, shell, dialogs, tray, notifications, power) are fakes in a test host, registered behind the feature switch so Production does not contain them.
- **Spar keeps no process-wide mutable state** and never uses `Console`, environment variables or the working directory for per-app values: several apps run at once in one test process. A new static field is reviewed, and the static-state test lists the allowed ones.

## Projects, variants and lanes

- **A test project** sets `<IsTestProject>true</IsTestProject>` and is a `Spar.Sdk` project (in this repository: it imports `src/Spar.Sdk/Sdk/Sdk.props` and `Sdk.targets`). Spar.Sdk adds `xunit.v3`, the test library, and `SparVariant=Test` on its project references, and turns the test-host switch on.
- **Each variant builds into `obj/<variant>/` and `bin/<variant>/`;** restore output stays in `obj/`. Never share one variant's output with another.
- **The NativeAOT lane is the same project:** `dotnet publish <tests> -c Release -p:SparTestLane=Aot` restores `xunit.v3.aot` into `obj/aot/` and publishes with NativeAOT. Run the published executable. Browser tests stay on CoreCLR (Playwright cannot run under NativeAOT); `xunit.v3.aot` allows no generic test methods or `params`, and prints no stack traces.
- **Results** go to `artifacts/test-results/<project>/` (decision "M1a names"); no `TestResults/` folder is left in the working directory.

## Dispatch, as tests see it

- **Outcomes** are a value, a declared error, or a failure with the `CallError` fields: `rejected`/`invalid-request` or `unknown-command` (`notStarted`) before the command runs; `failed`/`unknown` when it throws; `cancelled`/`stopped` only when it ended on its own call token; `response`/`serialization` (`completed`) when its result cannot be written. Nothing is retried.
- **Each call has its own DI scope** with `ISparCall` filled before the contract is resolved, and the command's `TimeoutSeconds` folded into its token.

## Verify

- Run the tests on CoreCLR and publish and run the NativeAOT lane; both must pass, with no warnings.
- After a change to the test host or the generator's Test-variant output, publish the app in Release and check the production boundary: the binary contains none of `SparTestSession`, `ISparEntryPoint`, the generated clients or the fakes (type names are UTF-8 in the image, string literals UTF-16).
- Window tests (M1b, M1c) run as their own invocation: a native engine abort ends the whole test process.
- The evidence for these rules is in `spikes/test-host/RESULTS.md`, `spikes/entry-accessor/RESULTS.md` and `docs/pitfalls.md` ("Build, MSBuild and packages", "Tooling and environment").

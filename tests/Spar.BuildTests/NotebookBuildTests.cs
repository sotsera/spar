using System.Text;

namespace Spar.BuildTests;

/// <summary>
/// The Notebook sample built from a clean copy: what a fresh clone's first build produces, what an unchanged second
/// build runs, and what the Production executable carries. The builds share the copy's <c>obj/</c> folders and its
/// restore, so they stay in this one class, whose tests xUnit runs one at a time.
/// </summary>
public sealed class NotebookBuildTests(Workspace workspace) : IDisposable
{
    private const string Tests = "samples/Notebook.Tests/Notebook.Tests.csproj";

    private const string App = "samples/Notebook/Notebook.csproj";

    /// <summary>The Spar targets with inputs and outputs; an unchanged build skips every one.</summary>
    private static readonly string[] IncrementalSteps =
    [
        "SparClient", "SparFrontendInstall", "SparFrontendTypeCheck", "SparFrontendBuild", "SparFrontendPack",
    ];

    /// <summary>Plan section 5: the no-op build of the Notebook sample.</summary>
    private static readonly TimeSpan NoOpBudget = TimeSpan.FromSeconds(1.5);

    private static Task<BuildResult>? s_first;

    [Fact]
    public async Task A_clean_copy_builds_the_app_with_its_page_and_client_and_changes_no_authored_file()
    {
        var first = await FirstBuildAsync();

        Assert.True(first.Exit == 0, first.Output);
        Assert.Equal(IncrementalSteps.Order(StringComparer.Ordinal), first.Ran.Intersect(IncrementalSteps));

        var app = Directory.GetFiles(Path.Combine(workspace.Root, "samples", "Notebook.Tests", "bin"), "Notebook.dll", SearchOption.AllDirectories);
        var assembly = Assert.Single(app);
        var image = File.ReadAllBytes(assembly);
        Assert.True(Contains(image, "Spar.Frontend", Encoding.UTF8), "The app assembly has no packed page.");
        Assert.True(Contains(image, "Spar.ThirdPartyNotices", Encoding.UTF8), "The app assembly has no notices.");

        var client = Path.Combine(workspace.Root, "samples", "Notebook", "obj", "spar", "client");
        Assert.True(File.Exists(Path.Combine(client, "package.json")), "No generated client under obj/spar/client.");

        AssertAuthoredFilesUntouched();
    }

    [Fact]
    public async Task An_unchanged_build_runs_no_Spar_step()
    {
        var first = await FirstBuildAsync();
        Assert.True(first.Exit == 0, first.Output);

        var second = await Dotnet.RunAsync(workspace, "notebook-tests-noop", "build", Tests);

        Assert.True(second.Exit == 0, second.Output);
        Assert.Empty(second.Ran);
        foreach (var step in IncrementalSteps)
        {
            Assert.True(second.SparTargets.ContainsKey(step), $"The unchanged build's log does not mention {step}.");
        }

        // The budget is the app's own no-op build (plan section 5), measured after a first Debug build of it. Times
        // vary with the machine, so they are recorded in timings.txt beside the logs, not asserted.
        var appFirst = await Dotnet.RunAsync(workspace, "notebook-debug-first", "build", App);
        Assert.True(appFirst.Exit == 0, appFirst.Output);
        var appNoOp = await Dotnet.RunAsync(workspace, "notebook-debug-noop", "build", App);
        Assert.True(appNoOp.Exit == 0, appNoOp.Output);
        Assert.Empty(appNoOp.Ran);
        TestContext.Current.SendDiagnosticMessage(
            $"No-op build of the Notebook: {appNoOp.Elapsed.TotalSeconds:0.00} s (budget {NoOpBudget.TotalSeconds} s).");

        AssertAuthoredFilesUntouched();
    }

    [Fact]
    public async Task The_production_executable_carries_its_page_and_no_test_or_loopback_code()
    {
        // A publish installs the frontend and writes the client, so the clean copy's first build goes before it.
        await FirstBuildAsync();

        var publish = Path.Combine(workspace.Publish, "Notebook");
        var result = await Dotnet.RunAsync(workspace, "notebook-publish", "publish", App, "-c", "Release", "-o", publish);
        Assert.True(result.Exit == 0, result.Output);

        var name = OperatingSystem.IsWindows() ? "Notebook.exe" : "Notebook";
        var image = File.ReadAllBytes(Path.Combine(publish, name));
        Assert.True(Contains(image, "SPARFE", Encoding.ASCII), "The executable carries no packed page.");

        // The build's identity, read from the file as spar doctor reads it: Production, the repository's revision and state.
        var identity = await BuildIdentityExpectation.OfAsync(workspace, "Production");
        identity.Check(Spar.Tool.Identity.BuildIdentityReader.Read(Path.Combine(publish, name)).Identity);

        var found = ProductionBoundary.Find(image);
        Assert.True(found.Count == 0, $"The Production executable contains: {string.Join(", ", found)}.");

        // The control: the same search finds the markers in the assemblies that carry them.
        var loopbackProject = Path.Combine("src", "Spar.Loopback", "Spar.Loopback.csproj");
        var built = await Dotnet.RunAsync(workspace, "loopback", "build", loopbackProject);
        Assert.True(built.Exit == 0, built.Output);
        var loopbackOutput = Path.Combine(workspace.Root, "src", "Spar.Loopback", "bin");
        var loopback = File.ReadAllBytes(Assert.Single(Directory.GetFiles(loopbackOutput, "Spar.Loopback.dll", SearchOption.AllDirectories)));
        var testOutput = Path.Combine(workspace.Root, "samples", "Notebook.Tests", "bin");
        var runtime = File.ReadAllBytes(Assert.Single(Directory.GetFiles(testOutput, "Spar.dll", SearchOption.AllDirectories)));
        Assert.True(Contains(loopback, "LoopbackHost", Encoding.UTF8), "The control search missed LoopbackHost.");
        Assert.True(Contains(loopback, "/_spar/channel", Encoding.Unicode), "The control search missed /_spar/channel.");
        Assert.True(Contains(loopback, "SparLoopbackHost", Encoding.UTF8), "The control search missed SparLoopbackHost.");
        Assert.True(Contains(loopback, "spar-loopback.json", Encoding.Unicode), "The control search missed spar-loopback.json.");
        Assert.True(Contains(runtime, "SparTestSession", Encoding.UTF8), "The control search missed SparTestSession.");
        Assert.True(Contains(runtime, "DevServerProxy", Encoding.UTF8), "The control search missed DevServerProxy.");
        Assert.True(Contains(runtime, "spar-dev-server.json", Encoding.Unicode), "The control search missed spar-dev-server.json.");
        Assert.True(Contains(runtime, "BackendUpdates", Encoding.UTF8), "The control search missed BackendUpdates.");
        Assert.True(Contains(runtime, "ConsoleInterrupt", Encoding.UTF8), "The control search missed ConsoleInterrupt.");
        Assert.True(Contains(runtime, "Attached to the parent's console", Encoding.Unicode), "The control search missed the console line.");
        Assert.True(Contains(loopback, "Hot Reload applied revision", Encoding.Unicode), "The control search missed the Hot Reload line.");
        Assert.True(Contains(loopback, "backend-updated", Encoding.Unicode), "The control search missed backend-updated.");
        Assert.True(Contains(runtime, "DevtoolsServer", Encoding.UTF8), "The control search missed DevtoolsServer.");
        Assert.True(Contains(runtime, "spar-devtools-", Encoding.Unicode), "The control search missed spar-devtools-.");
        if (OperatingSystem.IsWindows())
        {
            var windowsOutput = Path.Combine(workspace.Root, "src", "Spar.Windows", "bin");
            var windowsHost = File.ReadAllBytes(Directory.GetFiles(windowsOutput, "Spar.Windows.dll", SearchOption.AllDirectories)[0]);
            Assert.True(Contains(windowsHost, "SPAR_DEV_WINDOW", Encoding.Unicode), "The control search missed SPAR_DEV_WINDOW.");
        }

        // The DevTools Protocol in Production: exactly the two calls that keep the page's file picker closed; the
        // control is the Windows host's assembly, whose Development-only input trigger names the Input domain. A Linux
        // image has no Windows host and, until its own host (M1c), makes no protocol call at all.
        string[] expected = OperatingSystem.IsWindows() ? [.. ProductionBoundary.AllowedDevToolsMethods] : [];
        Assert.Equal(expected, ProductionBoundary.DevToolsMethods(image));
        if (OperatingSystem.IsWindows())
        {
            var host = File.ReadAllBytes(Directory.GetFiles(testOutput, "Spar.Windows.dll", SearchOption.AllDirectories)[0]);
            Assert.Contains("Input.dispatchMouseEvent", ProductionBoundary.DevToolsMethods(host));

            // The Development build's console comes from the protocol's events: in the host's assembly, never in Production.
            Assert.Contains("Runtime.consoleAPICalled", ProductionBoundary.DevToolsMethods(host));
            Assert.Contains("Runtime.enable", ProductionBoundary.DevToolsMethods(host));
        }

        // The page is packed compressed, so its bundle is searched before packing. `PageError` stands for the generated
        // error writers: the Notebook's page reads its errors, and only page tests write them.
        var app = Path.Combine(workspace.Root, "samples", "Notebook");
        var page = Assert.Single(Directory.GetDirectories(Path.Combine(app, "obj", "Production"), "frontend", SearchOption.AllDirectories));
        string[] pageMarkers = [.. ProductionBoundary.PageMarkers, "\"PageError\""];
        var inPage = ProductionBoundary.FindInPage(page, pageMarkers);
        Assert.True(inPage.Count == 0, $"The Production page contains: {string.Join(", ", inPage)}.");
        var generated = ProductionBoundary.FindInPage(Path.Combine(app, "obj", "spar", "client"), pageMarkers);
        Assert.True(generated.Count == pageMarkers.Length, $"The control search found only {string.Join(", ", generated)} in the generated package.");

        AssertAuthoredFilesUntouched();
    }

    /// <summary>Tells the workspace how this test ended, so a run with a failure keeps its copy.</summary>
    public void Dispose() =>
        workspace.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    /// <summary>The first build of the copy, shared by the tests that need it, whichever runs first.</summary>
    private Task<BuildResult> FirstBuildAsync() =>
        s_first ??= Dotnet.RunAsync(workspace, "notebook-tests-first", "build", Tests);

    private void AssertAuthoredFilesUntouched()
    {
        var now = workspace.HashAuthored();
        var changed = workspace.Authored.Where(f => now.GetValueOrDefault(f.Key) != f.Value).Select(f => f.Key);
        var added = now.Keys.Except(workspace.Authored.Keys);
        var problems = changed.Select(f => $"changed or removed: {f}").Concat(added.Select(f => $"added: {f}")).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static bool Contains(byte[] image, string text, Encoding encoding) => ProductionBoundary.Contains(image, text, encoding);
}

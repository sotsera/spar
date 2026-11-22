using System.Text.Json;

namespace Spar.PackageTests;

/// <summary>
/// The spar tool from its package (plan, M1d step 6b): the new app's <c>dotnet tool restore</c> from its local tool
/// manifest, <c>dotnet spar doctor --json</c> clean on this machine and naming what is broken, the build identity read
/// from the consumer's published NativeAOT executable and from its Debug build (outside any git work tree, so without a
/// revision), and <c>dotnet spar init frontend</c> wiring a frontend made by the pinned scaffolder into a new app that
/// then builds. The CLI's home folder is the run's (<see cref="Consumer.ToolEnvironment"/>).
/// </summary>
[Collection(Consumer.MainCollection)]
public sealed class ToolTests(Consumer consumer) : IDisposable
{
    private const string App = Consumer.App;

    /// <summary>Doctor's checks of a window's prerequisites on Linux (<see cref="MachineLacksADesktop"/>).</summary>
    private static readonly string[] DesktopChecks = ["linux-gtk4", "linux-webkitgtk", "linux-sandbox-tools"];

    [Fact]
    public async Task The_tool_restores_from_the_manifest_and_doctor_finds_nothing_to_fix_in_the_new_app()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);

        var (restored, restoreOutput) = await consumer.ToolInAsync(consumer.Root, "", "tool", "restore");
        var (_, version) = await consumer.ToolInAsync(consumer.Root, App, "spar", "--version");
        var (exit, output) = await consumer.ToolInAsync(consumer.Root, App, "spar", "doctor", "--json");

        Assert.True(restored == 0, restoreOutput);
        Assert.StartsWith(consumer.First, version.Trim(), StringComparison.Ordinal);

        // The WebView2 loader comes in the package: a loader elsewhere on PATH (the Windows SDK's Performance Toolkit has
        // one) would hide a package without it.
        using (var package = System.IO.Compression.ZipFile.OpenRead(Path.Combine(consumer.Feed, $"Spar.Tool.{consumer.First}.nupkg")))
        {
            var entries = package.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("tools/net11.0/any/runtimes/win-x64/native/WebView2Loader.dll", entries);
            Assert.Contains("tools/net11.0/any/runtimes/win-arm64/native/WebView2Loader.dll", entries);
        }

        var report = Json(output);
        var problems = Problems(report).Where(problem => !MachineLacksADesktop(problem)).ToList();
        Assert.True(problems.Count == 0, $"doctor found:\n{string.Join("\n", problems)}\n{output}");
        Assert.True(exit == 0 || MachineLacksADesktop(), output);
        Assert.Equal(Path.Combine(consumer.Root, App, $"{App}.csproj"), report.GetProperty("project").GetString());

        // The newest build of the app under its bin/ (the fast tests' Test variant, or a later Debug or Release build of
        // another test); outside a git work tree it has no revision and no tree state.
        var identity = report.GetProperty("identity");
        Assert.Contains(identity.GetProperty("variant").GetString(), new[] { "Test", "Development", "Production" });
        Assert.Equal(JsonValueKind.Null, identity.GetProperty("revision").ValueKind);
        Assert.Equal(JsonValueKind.Null, identity.GetProperty("dirty").ValueKind);
        Assert.Equal(SnapshotFingerprint(), identity.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task Doctor_names_a_removed_node_modules_with_its_fix_and_exit_code_1()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        Assert.Equal(0, (await consumer.ToolInAsync(consumer.Root, "", "tool", "restore")).Exit);
        var modules = Path.Combine(consumer.Root, App, "frontend", "node_modules");
        var away = modules + ".away";
        Directory.Move(modules, away);
        try
        {
            var (exit, output) = await consumer.ToolInAsync(consumer.Root, App, "spar", "doctor", "--json");

            var finding = Findings(Json(output)).Single(f => f.GetProperty("id").GetString() == "frontend-node-modules");
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Contains("npm ci --ignore-scripts", finding.GetProperty("fix").GetString(), StringComparison.Ordinal);
            Assert.True(exit == 1 || (MachineLacksADesktop() && exit == 2), output);
        }
        finally
        {
            Directory.Move(away, modules);
        }
    }

    [Fact]
    public async Task The_identity_is_read_from_the_published_NativeAOT_executable_and_from_the_Debug_build()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        Assert.Equal(0, (await consumer.ToolInAsync(consumer.Root, "", "tool", "restore")).Exit);
        var (published, publishOutput, executable) = await consumer.PublishedAsync();
        Assert.True(published == 0, publishOutput);
        var (built, buildOutput) = await consumer.DotnetAsync(App, "build", "-nologo", "-v:m");
        Assert.True(built == 0, buildOutput);
        var apphost = Directory.GetFiles(Path.Combine(consumer.Root, App, "bin", "Development"), OperatingSystem.IsWindows() ? $"{App}.exe" : App, SearchOption.AllDirectories).Single();

        var (_, fromExecutable) = await consumer.ToolInAsync(consumer.Root, App, "spar", "doctor", "--json", "--app", executable);
        var (_, fromDebug) = await consumer.ToolInAsync(consumer.Root, App, "spar", "doctor", "--json", "--app", apphost);

        var production = Json(fromExecutable).GetProperty("identity");
        Assert.Equal(executable, production.GetProperty("file").GetString());
        Assert.Equal("Production", production.GetProperty("variant").GetString());
        Assert.Equal(JsonValueKind.Null, production.GetProperty("revision").ValueKind);
        Assert.Equal(SnapshotFingerprint(), production.GetProperty("fingerprint").GetString());

        // The Debug build's executable is the apphost: the identity is in the app's DLL beside it.
        var debug = Json(fromDebug).GetProperty("identity");
        Assert.Equal(Path.ChangeExtension(apphost, null) + ".dll", debug.GetProperty("file").GetString());
        Assert.Equal("Development", debug.GetProperty("variant").GetString());
        Assert.Equal(SnapshotFingerprint(), debug.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task Init_frontend_wires_a_frontend_made_by_the_pinned_scaffolder_and_the_app_builds()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var folder = Path.Combine(consumer.RunRoot, "Init Äpp");
        var (created, createOutput) = await consumer.NewAppAsync(folder, "-n", "Pages", "--no-end-to-end-tests");
        Assert.True(created == 0, createOutput);
        ConsumerFiles.WriteNugetConfig(folder, consumer.Feed);
        var frontend = Path.Combine(folder, "Pages", "frontend");
        Directory.Delete(frontend, recursive: true);
        Assert.Equal(0, (await consumer.ToolInAsync(folder, "", "tool", "restore")).Exit);

        // No terminal to ask in and no --yes: nothing is written, and the scaffolder does not run.
        var (notAsked, notAskedOutput) = await consumer.ToolInAsync(folder, "Pages", "spar", "init", "frontend", "--framework", "react");
        Assert.True(notAsked == 1, notAskedOutput);
        Assert.False(Directory.Exists(frontend));

        var (wired, wiredOutput) = await consumer.ToolInAsync(folder, "Pages", "spar", "init", "frontend", "--framework", "react", "--yes");

        Assert.True(wired == 0, wiredOutput);
        Assert.Contains("create-vite 9.3.4", wiredOutput, StringComparison.Ordinal);
        Assert.Contains("\"@spar/app\": \"file:../obj/spar/client\"", File.ReadAllText(Path.Combine(frontend, "package.json")), StringComparison.Ordinal);
        Assert.Contains("spar()", File.ReadAllText(Path.Combine(frontend, "vite.config.ts")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(frontend, "package-lock.json")), "No lockfile was written.");
        Assert.False(Directory.Exists(Path.Combine(folder, "Pages", "obj", "spar", "init")) && Directory.EnumerateFileSystemEntries(Path.Combine(folder, "Pages", "obj", "spar", "init")).Any());

        var (built, buildOutput) = await consumer.DotnetInAsync(folder, "Pages", "build", "-nologo", "-v:m");
        Assert.True(built == 0, buildOutput);

        // Wired as doctor checks it; Vite's scaffold's tsconfig.json only references others, which doctor points out.
        var (_, doctorOutput) = await consumer.ToolInAsync(folder, "Pages", "spar", "doctor", "--json");
        var findings = Findings(Json(doctorOutput)).ToDictionary(f => f.GetProperty("id").GetString()!, f => f.GetProperty("severity").GetString(), StringComparer.Ordinal);
        Assert.Equal("info", findings["frontend-vite-plugin"]);
        Assert.Equal("info", findings["frontend-spar-dependency"]);
        Assert.Equal("info", findings["frontend-node-modules"]);
        Assert.Equal("warning", findings["frontend-type-check"]);
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    /// <summary>The JSON object doctor wrote (the command's output holds it alone).</summary>
    private static JsonElement Json(string output)
    {
        var start = output.IndexOf('{', StringComparison.Ordinal);
        var end = output.LastIndexOf('}');
        Assert.True(start >= 0 && end > start, $"No JSON in:\n{output}");
        return JsonDocument.Parse(output[start..(end + 1)]).RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Findings(JsonElement report) => report.GetProperty("findings").EnumerateArray();

    /// <summary>Every warning and error, as "id: message".</summary>
    private static IEnumerable<string> Problems(JsonElement report) =>
        Findings(report).Where(f => f.GetProperty("severity").GetString() != "info")
            .Select(f => $"{f.GetProperty("id").GetString()}: {f.GetProperty("message").GetString()}");

    /// <summary>
    /// A Linux machine without WebKitGTK 6.0 or GTK 4 (CI's hosted runner), where doctor rightly reports them; the
    /// window and black-box tests skip there for the same reason (TemplateTests).
    /// </summary>
    /// <summary>
    /// A Linux machine without WebKitGTK 6.0 (CI's package-test runner) has no window: doctor's errors for the window's
    /// libraries and for WebKitGTK's sandbox tools are then right, and the only ones the test accepts.
    /// </summary>
    private static bool MachineLacksADesktop(string problem = "") =>
        OperatingSystem.IsLinux() && (problem.Length == 0 || DesktopChecks.Any(id => problem.StartsWith(id + ":", StringComparison.Ordinal)))
        && !Libraries().Any(folder => File.Exists(Path.Combine(folder, "libwebkitgtk-6.0.so.4")));

    private static IEnumerable<string> Libraries() =>
        [.. (Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries),
            "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib"];

    /// <summary>The fingerprint in the app's contract snapshot, which its build wrote.</summary>
    private string SnapshotFingerprint() =>
        File.ReadAllLines(Path.Combine(consumer.Root, App, $"{App}.contracts.snapshot"))
            .Single(line => line.StartsWith("fingerprint ", StringComparison.Ordinal))["fingerprint ".Length..];
}

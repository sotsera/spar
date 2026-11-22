namespace Spar.PackageTests;

/// <summary>
/// The app template's tests, run as a new app from it runs them, from Spar's packages alone (plan section 5, "Template
/// defaults"; decision "M1d step 4's shape: tests in the template"): the fast tests (the first build of
/// <see cref="PackageTests"/>) with their results under <c>obj/</c>, their NativeAOT lane published and run, the page
/// tests through <c>npm test</c>, and the end-to-end project's browser, window and black-box tests, each kind as its own
/// invocation; and the trait check failing a window test that lacks its trait.
/// </summary>
[Collection(Consumer.MainCollection)]
public sealed class TemplateTests(Consumer consumer) : IDisposable
{
    private const string App = Consumer.App;

    private static readonly string FastTests = Path.Combine($"{App}.Tests", $"{App}.Tests.csproj");

    private static readonly string EndToEndTests = Path.Combine($"{App}.EndToEndTests", $"{App}.EndToEndTests.csproj");

    [Fact]
    public void The_templates_Playwright_is_the_repositorys()
    {
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(ConsumerFiles.Template(consumer.Repository), "MyApp.EndToEndTests", "MyApp.EndToEndTests.csproj"));
        var reference = project.Descendants("PackageReference").Single(p => (string?)p.Attribute("Include") == "Microsoft.Playwright");

        Assert.Equal(consumer.PackageVersion("Microsoft.Playwright"), (string?)reference.Attribute("Version"));
    }

    [Fact]
    public async Task The_fast_tests_write_their_results_under_obj_and_leave_no_TestResults_folder()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);

        Assert.True(Directory.Exists(Path.Combine(consumer.Root, $"{App}.Tests", "obj", "test-results")), "No results folder under the test project's obj/.");
        Assert.Empty(TestResultsFolders());
    }

    [Fact]
    public async Task The_fast_tests_NativeAOT_lane_publishes_and_passes()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var publish = Path.Combine(consumer.RunRoot, "aot-tests");

        var (exit, output) = await consumer.DotnetAsync("", "publish", FastTests, "-c", "Release", "-p:SparTestLane=Aot", "-o", publish, "-nologo");

        Assert.True(exit == 0, output);
        var executable = Path.Combine(publish, OperatingSystem.IsWindows() ? $"{App}.Tests.exe" : $"{App}.Tests");
        var (ran, said) = await Tool.RunAsync(publish, executable, [], consumer.Environment);
        Assert.True(ran == 0, said);
        Assert.Contains("Total: 2, Errors: 0, Failed: 0, Skipped: 0", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_tests_pass_with_npm_test()
    {
        // The first build installed the frontend's packages and wrote the generated @spar/app the tests import.
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);

        var (exit, output) = await Tool.RunAsync(Path.Combine(consumer.Root, App, "frontend"), Npm(), ["test"], consumer.Environment);

        Assert.True(exit == 0, output);
        Assert.Contains("3 passed", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_browser_tests_pass_as_their_own_invocation()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);

        var (exit, output) = await consumer.DotnetAsync("", "test", "--project", EndToEndTests, "--filter-trait", "Surface=Browser");

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 1", output, StringComparison.Ordinal);
        Assert.Empty(TestResultsFolders());
    }

    [Fact]
    public async Task The_window_tests_pass_as_their_own_invocation()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        SkipWithoutLinuxDesktop();

        var (exit, output) = await consumer.DotnetAsync("", "test", "--project", EndToEndTests, "--filter-trait", "Surface=Window");

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_black_box_tests_publish_the_app_once_and_launch_it()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        SkipWithoutLinuxDesktop();

        var (exit, output) = await consumer.DotnetAsync("", "test", "--project", EndToEndTests, "--filter-trait", "Surface=BlackBox");

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 1", output, StringComparison.Ordinal);
        var name = OperatingSystem.IsWindows() ? $"{App}.exe" : App;
        var beside = Directory.GetFiles(Path.Combine(consumer.Root, $"{App}.EndToEndTests", "bin"), name, SearchOption.AllDirectories);
        var published = Assert.Single(beside, path => path.Contains(Path.Combine("spar-apps", App), StringComparison.Ordinal));
        var written = File.GetLastWriteTimeUtc(published);

        // An unchanged build publishes nothing again: the executable beside the tests is the same file.
        var (rebuilt, again) = await consumer.DotnetAsync("", "build", EndToEndTests, "-nologo", "-v:m");

        Assert.True(rebuilt == 0, again);
        Assert.DoesNotContain("Generating native code", again, StringComparison.Ordinal);
        Assert.Equal(written, File.GetLastWriteTimeUtc(published));
    }

    [Fact]
    public async Task A_window_test_without_its_trait_fails_at_its_start_naming_the_trait()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var untagged = Path.Combine(consumer.Root, $"{App}.EndToEndTests", "Window", "UntaggedWindowTests.cs");
        File.WriteAllText(untagged, $$"""
            using Spar.Testing;

            namespace {{App}}.EndToEndTests;

            public sealed class UntaggedWindowTests
            {
                [Fact]
                public async Task A_window_test_that_forgot_its_trait()
                {
                    await using var app = await SparTestApp.StartAsync<Program>(
                        o => o.Surface = SparTestSurface.Window,
                        TestContext.Current.CancellationToken);
                }
            }

            """);
        try
        {
            var (exit, output) = await consumer.DotnetAsync("", "test", "--project", EndToEndTests,
                "--filter-class", $"{App}.EndToEndTests.UntaggedWindowTests");

            // Refused before anything starts, so on every machine, with or without a desktop.
            Assert.NotEqual(0, exit);
            Assert.Contains("failed: 1", output, StringComparison.Ordinal);
            Assert.Contains("This test starts the Window surface but has no [Trait(\"Surface\", \"Window\")].", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(untagged);
        }
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    /// <summary>The <c>TestResults</c> folders <c>dotnet test</c> would leave in the consumer's folder or its projects' folders.</summary>
    private IReadOnlyList<string> TestResultsFolders() =>
        [.. Directory.EnumerateDirectories(consumer.Root, "TestResults", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1 })];

    /// <summary>
    /// npm's full path from PATH. On Windows it is <c>npm.cmd</c>, which started by its bare name looks for npm's own
    /// scripts in the working directory's <c>node_modules</c> instead of beside itself.
    /// </summary>
    private static string Npm()
    {
        var name = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return path.Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException($"{name} is not on PATH: the page tests need Node.js.");
    }

    /// <summary>
    /// On Linux, skips a window or black-box run on a machine that cannot show a test window: the Linux host needs
    /// WebKitGTK 6.0, and the test host and the black box start their own Xvfb and D-Bus daemon. The hosted CI runner
    /// of the package tests has none of them; Spar's own window tests run in its Linux CI image instead.
    /// </summary>
    private static void SkipWithoutLinuxDesktop()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
        foreach (var tool in new[] { "Xvfb", "dbus-daemon" })
        {
            Assert.SkipUnless(path.Any(folder => File.Exists(Path.Combine(folder, tool))), $"{tool} is not on PATH.");
        }

        string[] libraries = [.. (Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries),
            "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib"];
        Assert.SkipUnless(libraries.Any(folder => File.Exists(Path.Combine(folder, "libwebkitgtk-6.0.so.4"))), "WebKitGTK 6.0 is not installed.");
    }
}

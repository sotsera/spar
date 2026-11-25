using System.Text.RegularExpressions;

namespace Spar.PackageTests;

/// <summary>
/// The new app's CI workflow (<c>.github/workflows/ci.yml</c>, decision "M1d exit's shape"): its runners and steps follow
/// the template's options, its actions are pinned as the repository's are, and its commands pass from packages. GitHub
/// runs the workflow only after a push, so the main consumer runs each step's <c>run</c> block here, in order, with the
/// workflow's <c>env</c>, through bash as the workflow's <c>shell: bash</c> does, the way <see cref="GuideSnippets"/>
/// builds the publishing guide's blocks. Two steps are not run here, each named in <see cref="NotRunHere"/>.
/// </summary>
[Collection(Consumer.MainCollection)]
public sealed partial class WorkflowTests(Consumer consumer) : IDisposable
{
    private const string App = Consumer.App;

    private const string Job = "build-test";

    /// <summary>The steps a local run leaves out, and why.</summary>
    private static readonly Dictionary<string, string> NotRunHere = new(StringComparer.Ordinal)
    {
        ["NativeAOT's build tools (Linux)"] = "it installs system packages with sudo; this machine has them",
        ["Chromium for the browser tests"] = "it downloads Chromium into the user's own folders; the package tests' browser test already needs it installed",
    };

    private static string WorkflowOf(string root) => Path.Combine(root, ".github", "workflows", "ci.yml");

    [Fact]
    public void The_new_apps_workflow_runs_on_both_OSes_with_every_kind_of_test_and_the_publish()
    {
        var workflow = Workflow.Read(WorkflowOf(consumer.Root));

        Assert.Equal(["windows-latest", "ubuntu-latest"], workflow.MatrixOs(Job));
        Assert.Equal(
            [
                "actions/checkout", "actions/setup-dotnet", "actions/setup-node", "NativeAOT's build tools (Linux)", "Restore",
                "Build", "Fast tests", "Page tests", "Fast tests, NativeAOT", "Chromium for the browser tests", "Browser tests",
                "Window tests (Windows)", "Black-box tests (Windows)", "Publish", "Test results", "The published app",
            ],
            workflow.Steps(Job).Select(Label));
        Assert.Equal("true", workflow.Env["ContinuousIntegrationBuild"]);
        Assert.DoesNotContain("MyApp", File.ReadAllText(WorkflowOf(consumer.Root)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_workflows_actions_are_pinned_to_the_commits_the_repositorys_own_workflow_uses()
    {
        var repository = Workflow.Read(Path.Combine(consumer.Repository, ".github", "workflows", "ci.yml"));
        var pinned = repository.Steps(Job).Where(step => step.Uses is not null)
            .Select(step => step.Uses!).Distinct(StringComparer.Ordinal)
            .ToDictionary(uses => uses.Split('@')[0], uses => uses.Split('@')[1], StringComparer.Ordinal);
        var text = File.ReadAllLines(WorkflowOf(consumer.Root));

        var used = Workflow.Read(WorkflowOf(consumer.Root)).Steps(Job).Where(step => step.Uses is not null).ToList();

        Assert.NotEmpty(used);
        foreach (var step in used)
        {
            var (action, commit) = (step.Uses!.Split('@')[0], step.Uses.Split('@')[1]);
            Assert.Matches(Sha(), commit);
            Assert.True(pinned.TryGetValue(action, out var theirs), $"The repository's workflow does not use {action}.");
            Assert.Equal(theirs, commit);

            // Each pin keeps its release beside it, as a comment.
            Assert.Contains(text, line => line.Contains($"uses: {step.Uses} # v", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task The_workflow_follows_the_template_options()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var fast = Path.Combine(consumer.RunRoot, "Workflow Fast");
        var windows = Path.Combine(consumer.RunRoot, "Workflow Windows");
        var linux = Path.Combine(consumer.RunRoot, "Workflow Linux");

        foreach (var (folder, options) in new[]
        {
            (fast, new[] { "--no-end-to-end-tests" }),
            (windows, ["--platforms", "windows"]),
            (linux, ["--platforms", "linux"]),
        })
        {
            var (created, output) = await consumer.NewAppAsync(folder, ["-n", App, .. options]);
            Assert.True(created == 0, output);
        }

        string[] common = ["Restore", "Build", "Fast tests", "Page tests", "Fast tests, NativeAOT", "Publish"];
        string[] endToEnd = ["Chromium for the browser tests", "Browser tests", "Window tests (Windows)", "Black-box tests (Windows)"];

        // Without the end-to-end project: both OSes, and no step names it.
        var withoutEndToEnd = Workflow.Read(WorkflowOf(fast));
        Assert.Equal(["windows-latest", "ubuntu-latest"], withoutEndToEnd.MatrixOs(Job));
        Assert.Empty(withoutEndToEnd.Steps(Job).Select(Label).Intersect(endToEnd));
        Assert.Subset(withoutEndToEnd.Steps(Job).Select(Label).ToHashSet(), common.ToHashSet());
        Assert.DoesNotContain("EndToEndTests", File.ReadAllText(WorkflowOf(fast)), StringComparison.Ordinal);

        // Windows only: its runner, with every end-to-end step.
        var windowsOnly = Workflow.Read(WorkflowOf(windows));
        Assert.Equal(["windows-latest"], windowsOnly.MatrixOs(Job));
        Assert.Subset(windowsOnly.Steps(Job).Select(Label).ToHashSet(), endToEnd.Concat(common).ToHashSet());

        // Linux only: its runner and the browser tests; the Windows-only steps are gone, not only skipped.
        var linuxOnly = Workflow.Read(WorkflowOf(linux));
        Assert.Equal(["ubuntu-latest"], linuxOnly.MatrixOs(Job));
        Assert.DoesNotContain(linuxOnly.Steps(Job), step => step.If?.Contains("'Windows'", StringComparison.Ordinal) == true);
        Assert.Subset(linuxOnly.Steps(Job).Select(Label).ToHashSet(), common.Concat(endToEnd[..2]).ToHashSet());
        Assert.Empty(linuxOnly.Steps(Job).Select(Label).Intersect(endToEnd[2..]));

        // Every variant reads as the same YAML subset, with nothing of the template's conditions left.
        foreach (var folder in new[] { fast, windows, linux })
        {
            Assert.DoesNotContain("#if", File.ReadAllText(WorkflowOf(folder)), StringComparison.Ordinal);
            Assert.DoesNotContain("#endif", File.ReadAllText(WorkflowOf(folder)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_workflows_commands_pass_from_packages_on_this_OS()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var workflow = Workflow.Read(WorkflowOf(consumer.Root));
        var os = OperatingSystem.IsWindows() ? "Windows" : "Linux";

        // The workflow's env, and what a runner sets that the commands read. CI is left unset: with it the black box
        // shows the app's window on this desktop (as on a CI runner) instead of a hidden one.
        var environment = new Dictionary<string, string>(consumer.Environment);
        foreach (var (name, value) in workflow.Env)
        {
            environment[name] = value;
        }

        environment["RUNNER_OS"] = os;
        var bash = Bash();
        var ran = new List<string>();
        foreach (var step in workflow.Steps(Job))
        {
            if (step.Uses is not null || !Applies(step, os))
            {
                continue;
            }

            if (NotRunHere.ContainsKey(step.Name ?? ""))
            {
                continue;
            }

            Assert.False(step.Run is null, $"The step '{step}' has neither uses nor run.");
            var script = Path.Combine(consumer.RunRoot, $"workflow-step-{ran.Count + 1}.sh");
            File.WriteAllText(script, step.Run.ReplaceLineEndings("\n"));
            var folder = step.WorkingDirectory is null ? consumer.Root : Path.Combine(consumer.Root, step.WorkingDirectory);

            // GitHub's own command line for shell: bash.
            var (exit, output) = await Tool.RunAsync(folder, bash, ["--noprofile", "--norc", "-eo", "pipefail", script], environment);

            Assert.True(exit == 0, $"The workflow's step '{step}' failed:\n{output}");
            ran.Add(step.Name!);
        }

        string[] expected = OperatingSystem.IsWindows()
            ? ["Restore", "Build", "Fast tests", "Page tests", "Fast tests, NativeAOT", "Browser tests", "Window tests (Windows)", "Black-box tests (Windows)", "Publish"]
            : ["Restore", "Build", "Fast tests", "Page tests", "Fast tests, NativeAOT", "Browser tests", "Publish"];
        Assert.Equal(expected, ran);
        Assert.Subset(workflow.Steps(Job).Select(Label).ToHashSet(), NotRunHere.Keys.ToHashSet());
        consumer.Measure($"workflow steps run on {os}: {string.Join(", ", ran)}");

        // What the two upload steps collect: the TRX reports of every test step, and the executable without symbols.
        var reports = Directory.GetFiles(Path.Combine(consumer.Root, $"{App}.Tests", "obj", "test-results"), "*.trx");
        Assert.True(reports.Length >= 2, $"The fast tests left {reports.Length} TRX reports: one per lane expected.");
        Assert.Contains(reports, report => Path.GetFileName(report).StartsWith("aot-", StringComparison.Ordinal));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(consumer.Root, $"{App}.EndToEndTests", "obj", "test-results"), "*.trx"));
        var publish = Assert.Single(
            Directory.GetDirectories(Path.Combine(consumer.Root, App, "bin", "Production", "Release", "net11.0"), "publish", SearchOption.AllDirectories),
            folder => Path.GetFileName(Path.GetDirectoryName(folder)!) is "win-x64" or "linux-x64");
        Assert.True(File.Exists(Path.Combine(publish, OperatingSystem.IsWindows() ? $"{App}.exe" : App)), $"No executable in {publish}.");
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    /// <summary>A step's name, or for an unnamed action step the action without its pin.</summary>
    private static string Label(WorkflowStep step) => step.Name ?? step.Uses?.Split('@')[0] ?? throw new InvalidOperationException("A step without a name runs commands.");

    /// <summary>The step's condition on this OS: none, <c>always()</c>, or the runner's OS; any other condition fails the test.</summary>
    private static bool Applies(WorkflowStep step, string os) => step.If switch
    {
        null or "always()" => true,
        "runner.os == 'Windows'" or "runner.os == 'Linux'" => step.If.EndsWith($"'{os}'", StringComparison.Ordinal),
        _ => throw new InvalidOperationException($"The step '{step}' has a condition this test does not read: {step.If}"),
    };

    /// <summary>
    /// bash: on Linux from PATH; on Windows Git for Windows' own, beside <c>git.exe</c>, as GitHub's Windows runner uses
    /// it for <c>shell: bash</c> (the <c>bash.exe</c> of System32 starts WSL instead).
    /// </summary>
    private static string Bash()
    {
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (!OperatingSystem.IsWindows())
        {
            return path.Select(folder => Path.Combine(folder, "bash")).FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("bash is not on PATH.");
        }

        foreach (var git in path.Select(folder => Path.Combine(folder, "git.exe")).Where(File.Exists))
        {
            var root = Path.GetDirectoryName(Path.GetDirectoryName(git)!)!;
            foreach (var bash in new[] { Path.Combine(root, "bin", "bash.exe"), Path.Combine(root, "usr", "bin", "bash.exe") })
            {
                if (File.Exists(bash))
                {
                    return bash;
                }
            }
        }

        throw new InvalidOperationException("Git for Windows' bash was not found beside git.exe on PATH: the workflow's steps use shell: bash.");
    }

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex Sha();
}

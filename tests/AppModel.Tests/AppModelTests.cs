using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AppModel.Tests;

/// <summary>
/// The documentation check of <c>docs/app-model.md</c> (plan section 5, test type 17): the examples compile as the
/// document shows them. The blocks are copied verbatim into <c>tests/AppModel</c>, which compiles them against
/// stand-ins for the Spar API the document describes: Spar's design, including the parts not built yet. Each step of
/// the check is one test, so a failure names the step.
/// </summary>
public sealed partial class AppModelTests
{
    private static readonly string Check = FindCheck();

    [Theory]
    [InlineData("verbatim", "33 blocks, 0 differ")]
    [InlineData("contracts", "no rule broken")]
    [InlineData("check:ts7", null)]
    [InlineData("check:ts6", null)]
    [InlineData("bad:ts7", "all 21 mistakes rejected as expected")]
    [InlineData("bad:ts6", "all 21 mistakes rejected as expected")]
    [InlineData("routing", null)]
    public async Task The_documents_TypeScript_and_its_rules_check(string script, string? expected)
    {
        RequirePackages();

        var (exit, output) = await RunAsync("npm", "run", script);

        Assert.True(exit == 0, output);
        Assert.DoesNotContain(output.Split('\n'), line => line.StartsWith("FAIL ", StringComparison.Ordinal));
        if (expected is not null)
        {
            Assert.Contains(expected, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_documents_CSharp_builds_and_its_Program_runs()
    {
        var (built, buildOutput) = await RunAsync("dotnet", "build", "AppModelCheck.slnx", "-nologo", "-nr:false", "-v:q");
        Assert.True(built == 0, buildOutput);

        var (ran, runOutput) = await RunAsync("dotnet", "run", "--project", "SqlTool", "--no-build");
        Assert.True(ran == 0, runOutput);
    }

    /// <summary>
    /// The document's app through real Spar: its SDK, analyzer and generator, with stand-ins (<c>tests/AppModel/Planned</c>)
    /// only for what Spar does not have yet. The contracts it serves are those Spar can build today; the others join as
    /// their features land (streams and subscriptions in M3).
    /// </summary>
    [Fact]
    public async Task The_documents_app_builds_with_real_Spar()
    {
        var (exit, output) = await RunAsync("dotnet", "build", Path.Combine("Real", "SqlTool.Real.csproj"), "-nologo", "-nr:false", "-v:n");

        Assert.True(exit == 0, output);
        var snapshot = File.ReadAllText(Path.Combine(Check, "Real", "SqlTool.Real.contracts.snapshot"));
        var contracts = snapshot.Split('\n').Where(line => line.StartsWith("contract ", StringComparison.Ordinal)).Select(line => line.Split(' ')[1]);
        Assert.Equal(["connections", "crashReport", "script"], contracts);
    }

    [Fact]
    public async Task A_switch_missing_a_case_fails_with_CS8509()
    {
        var (exit, output) = await RunAsync("dotnet", "build", Path.Combine("SqlTool", "SqlTool.csproj"), "-nologo", "-nr:false", "-p:SparCheckMissingCase=true");

        Assert.NotEqual(0, exit);
        var codes = ErrorCode().Matches(output).Select(m => m.Groups["code"].Value).Distinct().ToList();
        Assert.Equal(["CS8509"], codes);
        Assert.Contains("ExhaustiveMissingCase.cs", output, StringComparison.Ordinal);
        Assert.Contains("FileDialogMissingCase.cs", output, StringComparison.Ordinal);
    }

    /// <summary>The check's npm packages (TypeScript 6 and 7, React's types), which CI installs with <c>npm ci</c>.</summary>
    private static void RequirePackages()
    {
        var typescript = Path.Combine(Check, "node_modules", "typescript6", "package.json");
        Assert.True(File.Exists(typescript), $"tests/AppModel has no npm packages: run 'npm ci --ignore-scripts' in {Check}.");
    }

    private static async Task<(int Exit, string Output)> RunAsync(string tool, params string[] arguments)
    {
        // npm is a script on Windows (npm.cmd), so it starts through the command interpreter there.
        var windowsScript = OperatingSystem.IsWindows() && tool == "npm";
        var start = new ProcessStartInfo(windowsScript ? "cmd.exe" : tool)
        {
            WorkingDirectory = Check,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (windowsScript)
        {
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("npm");
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The builds start cold and leave no MSBuild server behind.
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false";

        using var process = Process.Start(start)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, line) => Append(output, line.Data);
        process.ErrorDataReceived += (_, line) => Append(output, line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(5));
        process.WaitForExit();
        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(line);
        }
    }

    private static string FindCheck()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "AppModel");
            if (File.Exists(Path.Combine(candidate, "AppModelCheck.slnx")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("tests/AppModel was not found above the test's output folder.");
    }

    [GeneratedRegex(@"error (?<code>[A-Z]+\d+):")]
    private static partial Regex ErrorCode();
}

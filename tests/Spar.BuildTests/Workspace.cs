using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

[assembly: AssemblyFixture(typeof(Spar.BuildTests.Workspace))]

// Every test builds in the one copy, and two builds of the same projects at once would collide in their obj/ folders.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Spar.BuildTests;

/// <summary>
/// A clean copy of what the builds read, as a fresh clone would have it: the files git lists (tracked, or untracked
/// and not ignored) at the root and under <see cref="Copied"/>, so no <c>bin/</c>, <c>obj/</c> or <c>node_modules/</c>.
/// Made once per run under <c>artifacts/build-tests/&lt;run&gt;/</c>, with the TypeScript SDK installed and built
/// there, as CI does first. Starting a run deletes the earlier runs; a run whose tests all pass deletes its copy and
/// keeps its logs, and one with a failure keeps both for inspection.
/// </summary>
public sealed class Workspace : IAsyncLifetime
{
    /// <summary>The top-level folders the builds read; <c>spikes/</c>, <c>docs/</c> and the tool folders are left out.</summary>
    private static readonly string[] Copied = ["src", "samples", "tests", "eng"];

    /// <summary>Folders builds and installs write, which git must ignore everywhere.</summary>
    private static readonly string[] Generated = ["bin", "obj", "node_modules"];

    private static readonly string Run = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}";

    private int _passed;
    private int _failed;

    public string Repository { get; } = FindRepository();

    /// <summary>Where every run's folder goes.</summary>
    public string Runs => Path.Combine(Repository, "artifacts", "build-tests");

    /// <summary>The copy's root.</summary>
    public string Root => Path.Combine(Runs, Run, "repo");

    /// <summary>Where each build's binary log, its replay and <c>timings.txt</c> go; kept after the run.</summary>
    public string Logs => Path.Combine(Runs, Run, "logs");

    /// <summary>Where publications go; deleted with the copy.</summary>
    public string Publish => Path.Combine(Runs, Run, "publish");

    /// <summary>Every authored file of the copy with its SHA-256, taken right after copying.</summary>
    public IReadOnlyDictionary<string, string> Authored { get; private set; } = new Dictionary<string, string>();

    public async ValueTask InitializeAsync()
    {
        DeleteEarlierRuns();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);

        var listed = await Tool.RunAsync(Repository, "git", "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        Assert.True(listed.Exit == 0, listed.Output);
        var files = listed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        RefuseUnignoredOutput(files);

        foreach (var relative in files.Where(IsCopied))
        {
            var source = Path.Combine(Repository, relative);
            if (!File.Exists(source))
            {
                continue; // Deleted in the working tree but not yet staged.
            }

            var target = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }

        Authored = HashAuthored();

        var client = Path.Combine(Root, "src", "Spar.Client");
        var install = await Tool.RunAsync(client, "npm", "ci", "--ignore-scripts", "--no-audit", "--no-fund");
        Assert.True(install.Exit == 0, install.Output);
        var build = await Tool.RunAsync(client, "npm", "run", "build");
        Assert.True(build.Exit == 0, build.Output);
    }

    public ValueTask DisposeAsync()
    {
        // Only a run with a pass and no failure removes its copy: a failed or interrupted run is the one to look at.
        if (_passed > 0 && _failed == 0)
        {
            TryDelete(Path.Combine(Runs, Run, "repo"));
            TryDelete(Publish);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Called by each test as it ends, with whether it passed.</summary>
    public void Report(bool passed)
    {
        if (passed)
        {
            Interlocked.Increment(ref _passed);
        }
        else
        {
            Interlocked.Increment(ref _failed);
        }
    }

    /// <summary>The authored files' hashes now, keyed as <see cref="Authored"/> is.</summary>
    public Dictionary<string, string> HashAuthored()
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (!IsGenerated(relative))
            {
                hashes[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            }
        }

        return hashes;
    }

    /// <summary>A file at the root, or under one of the folders the builds read.</summary>
    private static bool IsCopied(string relative)
    {
        var slash = relative.IndexOf('/');
        return slash < 0 || Copied.Contains(relative[..slash], StringComparer.Ordinal);
    }

    private static bool IsGenerated(string relative) =>
        relative.Split('/').Any(segment => Generated.Contains(segment, StringComparer.Ordinal));

    /// <summary>
    /// Fails the run before copying when git lists files inside a build or install folder: a missing
    /// <c>.gitignore</c> entry would otherwise copy thousands of files, and the copy would not be clean.
    /// </summary>
    private static void RefuseUnignoredOutput(string[] files)
    {
        var unignored = files.Where(IsGenerated).ToList();
        if (unignored.Count == 0)
        {
            return;
        }

        var examples = string.Join("\n", unignored.Take(5).Select(f => "  " + f));
        Assert.Fail(
            $"git lists {unignored.Count} files inside bin/, obj/ or node_modules/ folders, for example:\n{examples}\n"
            + "Add the folder to .gitignore (or remove the files from the index) and run again.");
    }

    /// <summary>Removes earlier runs' folders; one still in use (another run, an open file) stays for the next run.</summary>
    private void DeleteEarlierRuns()
    {
        if (!Directory.Exists(Runs))
        {
            return;
        }

        foreach (var earlier in Directory.EnumerateDirectories(Runs))
        {
            TryDelete(earlier);
        }
    }

    private static void TryDelete(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            // npm and git leave read-only files, which Directory.Delete refuses on Windows.
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            TestContext.Current.SendDiagnosticMessage($"Could not delete {directory}: {problem.Message}");
        }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("These tests run from a build inside the Spar repository.");
    }
}

/// <summary>Runs a tool and collects what it wrote.</summary>
public static class Tool
{
    public static async Task<(int Exit, string Output, TimeSpan Elapsed)> RunAsync(string directory, string tool, params string[] arguments)
    {
        // npm is a script on Windows (npm.cmd), so it starts through the command interpreter there.
        var windowsScript = OperatingSystem.IsWindows() && tool == "npm";
        var start = new ProcessStartInfo(windowsScript ? "cmd.exe" : tool)
        {
            WorkingDirectory = directory,
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

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, line) => Append(output, line.Data);
        process.ErrorDataReceived += (_, line) => Append(output, line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(15));
        process.WaitForExit();
        lock (output)
        {
            return (process.ExitCode, output.ToString(), clock.Elapsed);
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
}

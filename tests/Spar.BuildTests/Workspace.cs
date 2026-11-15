using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

[assembly: AssemblyFixture(typeof(Spar.BuildTests.Workspace))]

namespace Spar.BuildTests;

/// <summary>
/// A clean copy of the working tree, as a fresh clone would have it: every tracked file and every untracked file git
/// does not ignore, so no <c>bin/</c>, <c>obj/</c> or <c>node_modules/</c>. Made once per run under
/// <c>artifacts/build-tests/&lt;run&gt;/</c>, with the TypeScript SDK installed and built there, as CI does first.
/// </summary>
public sealed class Workspace : IAsyncLifetime
{
    private static readonly string Run = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}";

    public string Repository { get; } = FindRepository();

    /// <summary>The copy's root.</summary>
    public string Root => Path.Combine(Repository, "artifacts", "build-tests", Run, "repo");

    /// <summary>Where each build's binary log and its replay go.</summary>
    public string Logs => Path.Combine(Repository, "artifacts", "build-tests", Run, "logs");

    /// <summary>Every authored file of the copy with its SHA-256, taken right after copying.</summary>
    public IReadOnlyDictionary<string, string> Authored { get; private set; } = new Dictionary<string, string>();

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        var listed = await Tool.RunAsync(Repository, "git", "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        Assert.True(listed.Exit == 0, listed.Output);

        foreach (var relative in listed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // Kept for inspection; artifacts/ is disposable.

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

    /// <summary>What builds may write: build output, installed packages, and what the npm install of the copy wrote.</summary>
    private static bool IsGenerated(string relative) =>
        relative.Split('/').Any(segment => segment is "bin" or "obj" or "node_modules");

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

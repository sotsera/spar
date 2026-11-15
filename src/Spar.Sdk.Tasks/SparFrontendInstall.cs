using System.Diagnostics;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Spar.Sdk.Tasks;

/// <summary>
/// <c>npm ci --ignore-scripts</c> in the frontend folder (plan section 1, pipeline step 4; section 3c), under a lease:
/// the variants of one app build at the same time and share one <c>node_modules</c>, and two installs at once would
/// break it. The build that holds the lease installs; one that waited finds the install current and skips it.
/// </summary>
[MSBuildMultiThreadableTask]
public sealed class SparFrontendInstall : MSBuildTask, IMultiThreadableTask
{
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(10);

    public SparFrontendInstall()
        : this(TaskEnvironment.Fallback)
    {
    }

    public SparFrontendInstall(TaskEnvironment environment)
    {
        TaskEnvironment = environment;
    }

    public TaskEnvironment TaskEnvironment { get; set; }

    /// <summary>The frontend folder, which holds <c>package.json</c> and <c>package-lock.json</c>.</summary>
    [Required]
    public string FrontendDirectory { get; set; } = "";

    /// <summary>The lease file, under the app's <c>obj/</c>, shared by its variants.</summary>
    [Required]
    public string LeaseFile { get; set; } = "";

    public override bool Execute()
    {
        var frontend = TaskEnvironment.GetAbsolutePath(FrontendDirectory);
        var leasePath = TaskEnvironment.GetAbsolutePath(LeaseFile);
        Directory.CreateDirectory(new AbsolutePath(Path.GetDirectoryName(leasePath.Value)!));

        using var lease = Acquire(leasePath);
        if (lease is null)
        {
            Log.LogError("Another build has held the npm install lease for {0} minutes: {1}.", LeaseTimeout.TotalMinutes, leasePath);
            return false;
        }

        if (IsCurrent(frontend))
        {
            Log.LogMessage(MessageImportance.Normal, "Spar: node_modules is current (installed by another build).");
            return true;
        }

        return RunNpm(frontend);
    }

    /// <summary>npm's own record of the install is newer than the lockfile.</summary>
    private static bool IsCurrent(AbsolutePath frontend)
    {
        var lockfile = new FileInfo(new AbsolutePath("package-lock.json", frontend));
        var installed = new FileInfo(new AbsolutePath(Path.Combine("node_modules", ".package-lock.json"), frontend));
        return installed.Exists && lockfile.Exists && installed.LastWriteTimeUtc >= lockfile.LastWriteTimeUtc;
    }

    private FileStream? Acquire(AbsolutePath path)
    {
        var deadline = DateTime.UtcNow + LeaseTimeout;
        var waited = false;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                if (!waited)
                {
                    Log.LogMessage(MessageImportance.High, "Spar: waiting for another build's npm install to finish.");
                    waited = true;
                }

                Thread.Sleep(250);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private bool RunNpm(AbsolutePath frontend)
    {
        var start = TaskEnvironment.GetProcessStartInfo();
        start.WorkingDirectory = frontend;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;

        // npm is a script on Windows (npm.cmd), so it runs through the command interpreter there.
        if (OperatingSystem.IsWindows())
        {
            start.FileName = "cmd.exe";
            start.Arguments = "/d /s /c \"npm ci --ignore-scripts --no-audit --no-fund\"";
        }
        else
        {
            start.FileName = "npm";
            start.Arguments = "ci --ignore-scripts --no-audit --no-fund";
        }

        Log.LogMessage(MessageImportance.High, "Spar: npm ci --ignore-scripts in {0}", frontend);
        using var process = new Process { StartInfo = start };
        var output = new List<string>();
        process.OutputDataReceived += (_, line) => Collect(output, line.Data);
        process.ErrorDataReceived += (_, line) => Collect(output, line.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            return true;
        }

        lock (output)
        {
            Log.LogError("npm ci failed with exit code {0} in {1}:{2}{3}", process.ExitCode, frontend, Environment.NewLine, string.Join(Environment.NewLine, output.TakeLast(20)));
        }

        return false;
    }

    private void Collect(List<string> output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.Add(line);
        }

        Log.LogMessage(MessageImportance.Low, line);
    }
}

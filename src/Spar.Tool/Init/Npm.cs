using System.Diagnostics;
using Spar.Tool.Machine;

namespace Spar.Tool.Init;

/// <summary>How init runs npm: the real one, or a fake in the tests that writes what the real one would.</summary>
internal interface INpm
{
    NpmResult Run(IReadOnlyList<string> arguments, string workingDirectory);
}

/// <summary>npm's exit code and what it wrote.</summary>
internal sealed record NpmResult(int ExitCode, string Output);

/// <summary>
/// npm from <c>PATH</c> (on Windows <c>npm.cmd</c>, through the command interpreter), its output captured, with npm's
/// update notice off; ten minutes at most, since the scaffolder and the install download packages.
/// </summary>
internal sealed class RealNpm : INpm
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    public NpmResult Run(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var start = Processes.StartInfo("npm", arguments);
        start.WorkingDirectory = workingDirectory;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.Environment["npm_config_update_notifier"] = "false";
        start.Environment["NO_COLOR"] = "1";
        try
        {
            var result = Process.RunAndCaptureText(start, Limit);
            var output = (result.StandardOutput + result.StandardError).TrimEnd();
            return result.ExitStatus.Canceled
                ? new NpmResult(-1, output + $"\nnpm did not finish within {Limit.TotalMinutes:0} minutes.")
                : new NpmResult(result.ExitStatus.ExitCode, output);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return new NpmResult(-1, $"npm could not be started ({exception.Message}): Node.js and npm must be on PATH (dotnet spar doctor checks them).");
        }
    }
}

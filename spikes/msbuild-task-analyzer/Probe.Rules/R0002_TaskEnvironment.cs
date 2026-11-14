// MSBuildTask0002: process-global state that has a TaskEnvironment alternative. Multithreadable code only, by default.
using System.Diagnostics;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

[MSBuildMultiThreadableTask]
public sealed class R0002_TaskEnvironment : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    public override bool Execute()
    {
        var cwd = Environment.CurrentDirectory; // expect: MSBuildTask0002
        Environment.CurrentDirectory = cwd; // expect: MSBuildTask0002
        var cwd2 = Directory.GetCurrentDirectory(); // expect: MSBuildTask0002
        var home = Environment.GetEnvironmentVariable("HOME"); // expect: MSBuildTask0002
        var user = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User); // expect: MSBuildTask0002
        Environment.SetEnvironmentVariable("SPIKE_VAR", "1"); // expect: MSBuildTask0002
        var all = Environment.GetEnvironmentVariables(); // expect: MSBuildTask0002
        var expanded = Environment.ExpandEnvironmentVariables("%TEMP%"); // expect: MSBuildTask0002
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); // expect: MSBuildTask0002
        var full = Path.GetFullPath("relative/file.txt"); // expect: MSBuildTask0002
        var tmp = Path.GetTempPath(); // expect: MSBuildTask0002
        var tmpFile = Path.GetTempFileName(); // expect: MSBuildTask0002
        var tmpDir = Directory.CreateTempSubdirectory("spike"); // expect: MSBuildTask0002
        var random = Path.GetRandomFileName(); // expect: none
        using var started = Process.Start("cmd.exe", "/c exit 0"); // expect: MSBuildTask0002
        var info = new ProcessStartInfo("cmd.exe"); // expect: MSBuildTask0002

        // Already-absolute input: a canonicalization of AbsolutePath.Value is accepted.
        var canonical = Path.GetFullPath(TaskEnvironment.ProjectDirectory.Value); // expect: none

        Log.LogMessage(MessageImportance.Low, $"{cwd}{cwd2}{home}{user}{all.Count}{expanded}{appData}{full}{tmp}{tmpFile}{tmpDir}{random}{info.FileName}{canonical}{started?.Id}");
        return true;
    }
}

// Run-time probe for MSBuild's multithreaded strict mode: an in-process task that still uses the process working
// directory. scripts/strict-mode.ps1 runs it under -mt; the analyzer flags it at compile time.
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

[MSBuildMultiThreadableTask]
public sealed class StrictModeProbe : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    /// <summary>true: also write a file through a relative path (MSB4287 in the sentinel) and change the process working directory (MSB4286).</summary>
    public bool ChangeDirectory { get; set; }

    public override bool Execute()
    {
        var processDirectory = Directory.GetCurrentDirectory(); // expect: MSBuildTask0002
        var relativeHit = File.Exists("Leaf.proj"); // expect: MSBuildTask0003
        Log.LogMessage(MessageImportance.High,
            $"STRICT|cwd={processDirectory}|relative Leaf.proj exists={relativeHit}|project={TaskEnvironment.ProjectDirectory}");
        if (ChangeDirectory)
        {
            File.WriteAllText("left-behind.txt", "x"); // expect: MSBuildTask0003
            Directory.SetCurrentDirectory(TaskEnvironment.ProjectDirectory); // expect: MSBuildTask0001
        }

        return true;
    }
}

// Scope and coverage probes.
using System.Globalization;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

/// <summary>
/// A regular task (no attribute, no interface). By default only MSBuildTask0001 applies. With
/// msbuild_task_analyzer.run_mt_analyzers_on_all_tasks = true, MSBuildTask0002-0005 apply as well.
/// </summary>
public sealed class R_RegularTask : Task
{
    public override bool Execute()
    {
        var cwd = Environment.CurrentDirectory; // expect: none / all-tasks: MSBuildTask0002
        var exists = File.Exists("relative.txt"); // expect: none / all-tasks: MSBuildTask0003
        Log.LogMessage(MessageImportance.Low, $"{cwd}{exists}{TransitiveHelper.Unreached()}"); // all-tasks: MSBuildTask0005 in the helper
        return true;
    }
}

/// <summary>Process-global or thread-shared state that the analyzer does not document.</summary>
[MSBuildMultiThreadableTask]
public sealed class R_NotCovered : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    private static int s_counter;
    private static readonly List<string> s_seen = [];

    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    public override bool Execute()
    {
        s_counter++; // expect: ?
        s_seen.Add("x"); // expect: ?
        AppContext.SetSwitch("Spike.Switch", true); // expect: ?
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture; // expect: ?
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // expect: ?
        GC.Collect(); // expect: ?
        Environment.ExitCode = 3; // expect: ?
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch; // expect: ?
        Log.LogMessage(MessageImportance.Low, $"{s_counter}");
        return true;
    }
}

/// <summary>The package's suppressor removes CS8618 for [Required] task properties only.</summary>
public sealed class R_RequiredNoInit : Task
{
    [Required]
    public string Input { get; set; } // expect: none (CS8618 suppressed)

    public string Optional { get; set; } // expect: CS8618

    public override bool Execute() => Input.Length + Optional.Length >= 0;
}

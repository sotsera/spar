using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Tasks;

/// <summary>The shape Spar's tasks use: routed in-process under -mt, environment injected through the constructor.</summary>
[MSBuildMultiThreadableTask]
public sealed class ThreadSafeProbe : Task, IMultiThreadableTask
{
    public ThreadSafeProbe(TaskEnvironment taskEnvironment) => TaskEnvironment = taskEnvironment;

    // Kept for hosts that construct tasks directly; MSBuild prefers the TaskEnvironment constructor.
    public ThreadSafeProbe()
        : this(TaskEnvironment.Fallback)
    {
    }

    public TaskEnvironment TaskEnvironment { get; set; }

    [Required]
    public string Label { get; set; } = "";

    public int DelayMilliseconds { get; set; }

    /// <summary>A relative path, resolved against the project directory through the task environment.</summary>
    [Required]
    public string OutputFile { get; set; } = "";

    public override bool Execute()
    {
        var start = DateTime.UtcNow;
        Thread.Sleep(DelayMilliseconds);
        var output = TaskEnvironment.GetAbsolutePath(OutputFile);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, Label);
        ProbeReport.Log(Log, "threadsafe", Label, TaskEnvironment.ProjectDirectory,
            TaskEnvironment.GetEnvironmentVariable("PROBE_VAR") ?? "<null>", start);
        return true;
    }
}

/// <summary>A task that has not opted in. The analyzer checks it only for MSBuildTask0001 by default.</summary>
public sealed class LegacyProbe : Task
{
    [Required]
    public string Label { get; set; } = "";

    public int DelayMilliseconds { get; set; }

    public override bool Execute()
    {
        var start = DateTime.UtcNow;
        Thread.Sleep(DelayMilliseconds);
        ProbeReport.Log(Log, "legacy", Label, Directory.GetCurrentDirectory(),
            Environment.GetEnvironmentVariable("PROBE_VAR") ?? "<null>", start);
        return true;
    }
}

/// <summary>Implements the interface without the attribute: gets an environment, but is not routed in-process.</summary>
public sealed class InterfaceOnlyProbe : Task, IMultiThreadableTask
{
    public InterfaceOnlyProbe(TaskEnvironment taskEnvironment) => TaskEnvironment = taskEnvironment;

    public InterfaceOnlyProbe()
        : this(TaskEnvironment.Fallback)
    {
    }

    public TaskEnvironment TaskEnvironment { get; set; }

    [Required]
    public string Label { get; set; } = "";

    public int DelayMilliseconds { get; set; }

    public override bool Execute()
    {
        var start = DateTime.UtcNow;
        Thread.Sleep(DelayMilliseconds);
        ProbeReport.Log(Log, "interface-only", Label, TaskEnvironment.ProjectDirectory,
            TaskEnvironment.GetEnvironmentVariable("PROBE_VAR") ?? "<null>", start);
        return true;
    }
}

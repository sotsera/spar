// MSBuildTask0006-0010: typed task parameters. 0006-0008 need [MSBuildMultiThreadableTask] directly on the task.
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

[MSBuildMultiThreadableTask]
public sealed class R0006_TypedParameters : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    public string InputPath { get; set; } = ""; // expect: none (0006 is reported where the value is converted)

    public string FilePath { get; set; } = ""; // expect: none

    public string RelativeDefault { get; set; } = "obj"; // expect: MSBuildTask0008

    public ITaskItem Item { get; set; } = null!; // expect: none (0007 is reported where ItemSpec is parsed)

    public override bool Execute()
    {
        var abs = TaskEnvironment.GetAbsolutePath(InputPath); // expect: MSBuildTask0006
        var file = new FileInfo(TaskEnvironment.GetAbsolutePath(FilePath)); // expect: MSBuildTask0006
        var rel = TaskEnvironment.GetAbsolutePath(RelativeDefault);
        var count = int.Parse(Item.ItemSpec, System.Globalization.CultureInfo.InvariantCulture); // expect: MSBuildTask0007
        Log.LogMessage(MessageImportance.Low, $"{abs}{file}{rel}{count}");
        return true;
    }
}

public sealed class R0009_TypedItems : Task
{
    public ITaskItem<Guid> Id { get; set; } = null!; // expect: MSBuildTask0009

    public ITaskItem<int> Count { get; set; } = null!; // expect: MSBuildTask0010

    public ITaskItem<AbsolutePath>[] Paths { get; set; } = []; // expect: none

    public override bool Execute() => true;
}

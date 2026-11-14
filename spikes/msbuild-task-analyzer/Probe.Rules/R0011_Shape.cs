// MSBuildTask0011-0014: how the attribute, the interface and the constructor fit together.
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Rules;

/// <summary>Constructor injection: no MSBuildTask0011.</summary>
[MSBuildMultiThreadableTask]
public sealed class R0011_Injected : Task, IMultiThreadableTask // expect: none
{
    public R0011_Injected(TaskEnvironment taskEnvironment) => TaskEnvironment = taskEnvironment;

    public R0011_Injected()
    {
        TaskEnvironment = TaskEnvironment.Fallback;
    }

    public TaskEnvironment TaskEnvironment { get; set; }

    public override bool Execute() => true;
}

/// <summary>Attribute and property, no interface: MSBuild never assigns the property.</summary>
[MSBuildMultiThreadableTask]
public sealed class R0012_NoInterface : Task // expect: MSBuildTask0012
{
    public TaskEnvironment TaskEnvironment { get; set; } = TaskEnvironment.Fallback;

    public override bool Execute() => true;
}

/// <summary>Interface without attribute: still routed to a TaskHost under -mt. MSBuildTask0013 is off by default.</summary>
public sealed class R0013_NoAttribute : Task, IMultiThreadableTask // expect: MSBuildTask0011
{
    public TaskEnvironment TaskEnvironment { get; set; } = null!;

    public override bool Execute() => true;
}

/// <summary>The attribute on a type that is not a task.</summary>
[MSBuildMultiThreadableTask]
public sealed class R0014_NotATask // expect: MSBuildTask0014
{
}

/// <summary>The attribute on an abstract task: it is not inherited.</summary>
[MSBuildMultiThreadableTask]
public abstract class R0014_AbstractBase : Task // expect: MSBuildTask0014
{
}

public sealed class R0014_Derived : R0014_AbstractBase
{
    public override bool Execute() => true;
}

/// <summary>ToolTask implements IMultiThreadableTask itself; deriving from it is not migration intent.</summary>
public sealed class R_ToolTaskDerived : ToolTask // expect: MSBuildTask0011
{
    protected override string ToolName => "cmd.exe";

    protected override string GenerateFullPathToTool()
    {
        return Path.GetFullPath("cmd.exe"); // expect: none
    }
}

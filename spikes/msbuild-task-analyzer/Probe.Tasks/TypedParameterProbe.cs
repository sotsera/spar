using System.Globalization;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Probe.Tasks;

/// <summary>
/// Checks whether this MSBuild binds the typed parameters that MSBuildTask0006/0007 recommend
/// (AbsolutePath, FileInfo, DirectoryInfo, ITaskItem&lt;T&gt;), and what the bound values are relative to.
/// </summary>
[MSBuildMultiThreadableTask]
public sealed class TypedParameterProbe : Task, IMultiThreadableTask
{
    public TypedParameterProbe(TaskEnvironment taskEnvironment) => TaskEnvironment = taskEnvironment;

    public TypedParameterProbe()
        : this(TaskEnvironment.Fallback)
    {
    }

    public TaskEnvironment TaskEnvironment { get; set; }

    public AbsolutePath Root { get; set; }

    public FileInfo? File { get; set; }

    public DirectoryInfo? Directory { get; set; }

    public ITaskItem<AbsolutePath>[] Items { get; set; } = [];

    public ITaskItem<bool>? Flag { get; set; }

    public override bool Execute()
    {
        var items = string.Join(";", Items.Select(i => $"{i.ItemSpec}=>{i.Value}"));
        Log.LogMessage(MessageImportance.High, string.Join('|',
            "TYPED",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            $"Root={Root.Value} (original {Root.OriginalValue})",
            $"File={File?.FullName}",
            $"Directory={Directory?.FullName}",
            $"Items={items}",
            $"Flag={Flag?.Value}",
            $"ProjectDirectory={TaskEnvironment.ProjectDirectory}"));
        return true;
    }
}

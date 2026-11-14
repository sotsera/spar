using Spar;

namespace SqlTool.CopyJobs;

/// <summary>Mirrors a running copy on the taskbar.</summary>
public sealed class CopyProgressReporter(ITaskbar taskbar)
{
    public void Report(long rowsCopied, long rowsTotal) =>
        taskbar.SetProgress(rowsTotal == 0 ? 0 : (double)rowsCopied / rowsTotal);

    public void Failed() => taskbar.SetProgress(TaskbarProgress.Error);

    public void Finished(int tablesWithWarnings)
    {
        taskbar.ClearProgress();
        taskbar.SetBadge(tablesWithWarnings == 0 ? null : tablesWithWarnings);
    }
}

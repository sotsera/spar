namespace SqlTool.CopyJobs;

public sealed record CopyRecipe(string Source, string Target, IReadOnlyList<string> Tables);

public sealed record CopyPlan(Guid PlanId, IReadOnlyList<PlanStep> Steps);

public sealed record PlanStep(string Table, long EstimatedRows);

public sealed record RunCopy(Guid PlanId, bool TruncateTarget);

public sealed record CopyReport(int Tables, long Rows, TimeSpan Elapsed);

public sealed record PreviewRow(IReadOnlyList<string?> Cells);

public sealed record CopyProgress(string JobId, long Rows);

/// <summary>Why a copy could not be planned.</summary>
public closed record PlanError
{
    /// <summary>The named connection is not configured.</summary>
    public sealed record ConnectionNotFound(string ConnectionId) : PlanError;

    /// <summary>The table does not exist on that connection.</summary>
    public sealed record TableMissing(string Connection, string Table) : PlanError;
}

/// <summary>Why a planned copy did not run to the end.</summary>
public closed record RunError
{
    public sealed record PlanOutdated(string Reason) : RunError;

    public sealed record TargetNotEmpty(string Table, long Rows) : RunError;

    /// <summary>Rows committed before the failure stay in the target.</summary>
    public sealed record PartiallyCopied(long RowsCommitted, string Reason) : RunError;
}

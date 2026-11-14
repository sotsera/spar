// The contract types of docs/app-model.md section 3.1, reduced to what the spike needs.
namespace SqlTool.CopyJobs;

public sealed record CopyRecipe(string Source, string Target, IReadOnlyList<string> Tables);

public sealed record CopyPlan(Guid PlanId, IReadOnlyList<PlanStep> Steps);

public sealed record PlanStep(string Table, long EstimatedRows);

public sealed record RunCopy(Guid PlanId, bool TruncateTarget);

public sealed record CopyReport(int Tables, long Rows, TimeSpan Elapsed);

/// <summary>Why a copy could not be planned.</summary>
public closed record PlanError
{
    public sealed record ConnectionNotFound(string ConnectionId) : PlanError;

    public sealed record TableMissing(string Connection, string Table) : PlanError;
}

/// <summary>Why a planned copy did not run to the end.</summary>
public closed record RunError
{
    public sealed record PlanOutdated(string Reason) : RunError;

    public sealed record TargetNotEmpty(string Table, long Rows) : RunError;

    public sealed record PartiallyCopied(long RowsCommitted, string Reason) : RunError;
}

/// <summary>Why a job could not be cancelled. Used by the command without a value.</summary>
public closed record CancelError
{
    public sealed record NotRunning(string JobId) : CancelError;

    public sealed record Forbidden : CancelError;
}

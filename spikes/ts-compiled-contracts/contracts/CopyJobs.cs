// The C# contract the hand-written generated modules stand for. Not compiled in this spike:
// it is the target of copyJobs.d.ts.map in the js-csmap variant.
using System.Runtime.CompilerServices;
using Spar;

namespace Sample.App;

/// <summary>What to copy, and where.</summary>
/// <param name="Source">Name of the source connection.</param>
/// <param name="Target">Name of the target connection.</param>
/// <param name="Tables">Tables to copy, in order.</param>
public sealed record CopyRecipe(
    string Source,
    string Target,
    IReadOnlyList<string> Tables);

public sealed record ColumnDiff(string Column, string? Source, string? Target);

public sealed record PlanStep(string Table, long EstimatedRows);

public sealed record CopyPlan(IReadOnlyList<PlanStep> Steps);

public sealed record RunCopy(string PlanId, bool TruncateTarget);

public sealed record CopyReport(int Tables, long Rows, long ElapsedMs);

public sealed record CopyProgress(string Table, long Rows);

public sealed record PreviewRow(IReadOnlyList<string?> Cells);

public sealed record ProgressRequest(string JobId);

/// <summary>Why a copy cannot be planned.</summary>
public closed record PlanError
{
    /// <summary>The named connection is not configured.</summary>
    public sealed record ConnectionNotFound(string ConnectionId) : PlanError;

    /// <summary>The table does not exist on that connection.</summary>
    public sealed record TableMissing(string Connection, string Table) : PlanError;

    /// <summary>Source and target columns differ.</summary>
    public sealed record ColumnsMismatch(string Table, IReadOnlyList<ColumnDiff> Differences) : PlanError;
}

public closed record RunError
{
    public sealed record PlanOutdated(string Reason) : RunError;

    public sealed record TargetNotEmpty(string Table, long Rows) : RunError;

    public sealed record ConstraintViolation(string Table, int SqlNumber, string Message) : RunError;

    public sealed record PartiallyCopied(long RowsCommitted, string Reason) : RunError;
}

public closed record PreviewError
{
    public sealed record TableMissing(string Connection, string Table) : PreviewError;
}

/// <summary>Copies tables between connections.</summary>
[SparContract]
public sealed class CopyJobsService
{
    /// <summary>Checks the recipe against both connections and returns the steps.</summary>
    public Task<Result<CopyPlan, PlanError>> PlanAsync(CopyRecipe request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    /// <summary>Runs a plan.</summary>
    public Task<Result<CopyReport, RunError>> RunAsync(RunCopy request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    /// <summary>Number of jobs.</summary>
    public Task<int> CountAsync() => throw new NotImplementedException();

    /// <summary>Cancels every running job.</summary>
    public Task CancelAllAsync() => throw new NotImplementedException();

    /// <summary>The first rows a copy would write.</summary>
    public IAsyncEnumerable<PreviewRow> PreviewAsync(CopyRecipe request, [EnumeratorCancellation] CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    /// <summary>Progress of a running job.</summary>
    public IAsyncEnumerable<CopyProgress> ProgressAsync(ProgressRequest request, [EnumeratorCancellation] CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

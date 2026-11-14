using System.Runtime.CompilerServices;
using Spar;
using SqlTool.Connections;

namespace SqlTool.CopyJobs;

/// <summary>Plans, previews and runs table copies. Wire name: copyJobs.</summary>
[SparContract]
public sealed class CopyJobsService(IConnectionStore connections, ISparCall call)
{
    /// <summary>Checks the recipe against both connections and estimates the work.</summary>
    [SparCommand]
    public async Task<Result<CopyPlan, PlanError>> PlanAsync(CopyRecipe recipe, CancellationToken cancellationToken)
    {
        if (!await connections.ExistsAsync(recipe.Source, cancellationToken))
        {
            return new PlanError.ConnectionNotFound(recipe.Source);
        }

        var steps = new List<PlanStep>();
        foreach (var table in recipe.Tables)
        {
            var rows = await connections.CountRowsAsync(recipe.Source, table, cancellationToken);
            if (rows is null)
            {
                return new PlanError.TableMissing(recipe.Source, table);
            }

            steps.Add(new PlanStep(table, rows.Value));
        }

        return new CopyPlan(Guid.CreateVersion7(), steps);
    }

    /// <summary>Copies the planned tables. Cancelling stops between batches.</summary>
    [SparCommand]
    public async Task<Result<CopyReport, RunError>> RunAsync(RunCopy request, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
        return new RunError.PlanOutdated($"Plan {request.PlanId} was made before the last schema change (call {call.CallId}).");
    }

    /// <summary>The first rows of each table, streamed as they are read.</summary>
    [SparCommand]
    public async IAsyncEnumerable<PreviewRow> PreviewAsync(CopyRecipe recipe, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var table in recipe.Tables)
        {
            await Task.Delay(1, cancellationToken);
            yield return new PreviewRow([table, null]);
        }
    }

    /// <summary>Live progress of a running copy. Observation only, so it may resume after a lost connection.</summary>
    [SparCommand]
    [SparSubscription(Capacity = 256, Resume = SubscriptionResume.Automatic)]
    public async IAsyncEnumerable<CopyProgress> ProgressAsync(string jobId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long rows = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(250, cancellationToken);
            rows += 1000;
            yield return new CopyProgress(jobId, rows);
        }
    }

    /// <summary>Plain Task: success carries no value.</summary>
    [SparCommand]
    public Task CancelAllAsync() => Task.CompletedTask;

    /// <summary>Not a command: no [SparCommand].</summary>
    public static string Describe(CopyRecipe recipe) => $"{recipe.Source} -> {recipe.Target}";
}

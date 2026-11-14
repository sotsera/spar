// The command bodies of docs/app-model.md section 3.2, unchanged apart from the connection store being a plain
// dictionary instead of a DI service. They compile against every Result form.
using Spar;

namespace SqlTool.CopyJobs;

public sealed class CopyJobsService(IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> connections)
{
    public async Task<Result<CopyPlan, PlanError>> PlanAsync(CopyRecipe recipe, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (!connections.TryGetValue(recipe.Source, out var tables))
        {
            return new PlanError.ConnectionNotFound(recipe.Source);
        }

        var steps = new List<PlanStep>();
        foreach (var table in recipe.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tables.TryGetValue(table, out var rows))
            {
                return new PlanError.TableMissing(recipe.Source, table);
            }

            steps.Add(new PlanStep(table, rows));
        }

        return new CopyPlan(Guid.CreateVersion7(), steps);
    }

    public async Task<Result<CopyReport, RunError>> RunAsync(RunCopy request, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return new RunError.PlanOutdated($"Plan {request.PlanId} was made before the last schema change.");
    }

    /// <summary>A command with errors and no value.</summary>
    public ValueTask<Result<CancelError>> CancelAsync(string jobId)
    {
        Result<CancelError> result = jobId.Length == 0
            ? new CancelError.NotRunning(jobId)
            : Result.Success;
        return ValueTask.FromResult(result);
    }

    /// <summary>Plain Task: success carries no value and there are no declared errors.</summary>
    public Task CancelAllAsync() => Task.CompletedTask;
}

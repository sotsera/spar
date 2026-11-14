// Expected to compile cleanly on every form: the reading API and nullable flow analysis.
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class Reading
{
    public static int Steps(Result<CopyPlan, PlanError> result)
    {
        if (result.TryGetValue(out var plan))
        {
            return plan.Steps.Count;
        }

        return -1;
    }

    public static string Describe(Result<CopyPlan, PlanError> result)
    {
        if (!result.TryGetError(out var error))
        {
            return $"{result.Value.Steps.Count} steps";
        }

        // Exhaustive over the closed error without a default arm.
        return error switch
        {
            PlanError.ConnectionNotFound e => $"Unknown connection {e.ConnectionId.Length}",
            PlanError.TableMissing e => $"{e.Table} is missing on {e.Connection}",
        };
    }

    public static string WithIsError(Result<CopyPlan, PlanError> result) =>
        result.IsError ? result.Error.ToString() : result.Value.PlanId.ToString();

    public static string WithMatch(Result<CopyPlan, PlanError> result) => result.Match(
        plan => $"{plan.Steps.Count} steps",
        error => error switch
        {
            PlanError.ConnectionNotFound e => e.ConnectionId,
            PlanError.TableMissing e => e.Table,
        });

    public static bool PropertyPatterns(Result<CopyPlan, PlanError> result) =>
        result is { IsSuccess: true, Value.Steps.Count: > 0 }
        || result is { IsError: true, Error: PlanError.TableMissing { Table: "dbo.Orders" } };

    public static async Task<int> AwaitThenRead(Task<Result<CopyPlan, PlanError>> call)
    {
        var result = await call;
        return result.TryGetValue(out var plan) ? plan.Steps.Count : 0;
    }

    public static string NoValue(Result<CancelError> result)
    {
        if (!result.TryGetError(out var error))
        {
            return "cancelled";
        }

        return error switch
        {
            CancelError.NotRunning e => $"{e.JobId} is not running",
            CancelError.Forbidden => "forbidden",
        };
    }
}

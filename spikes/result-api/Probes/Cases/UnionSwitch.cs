// One exhaustive switch over the result itself: the value and every error case, no default arm.
// Expected to compile on the union forms only.
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class UnionSwitch
{
    public static string Describe(Result<CopyPlan, PlanError> result) => result switch
    {
        CopyPlan plan => $"{plan.Steps.Count} steps",
        PlanError.ConnectionNotFound e => $"Unknown connection {e.ConnectionId.Length}",
        PlanError.TableMissing e => $"{e.Table} is missing on {e.Connection}",
    };

    public static async Task<string> AwaitThenSwitch(Task<Result<CopyReport, RunError>> call) => await call switch
    {
        CopyReport report => $"{report.Rows} rows",
        RunError.PlanOutdated e => e.Reason,
        RunError.TargetNotEmpty e => e.Table,
        RunError.PartiallyCopied e => $"{e.RowsCommitted} committed",
    };

    public static bool IsPattern(Result<CopyPlan, PlanError> result) =>
        result is PlanError.TableMissing { Table: "dbo.Orders" };

    public static string ValueTypeValue(Result<long, PlanError> result) => result switch
    {
        long rows => $"{rows} rows",
        PlanError e => e.ToString(),
    };

    public static string NoValue(Result<CancelError> result) => result switch
    {
        Success => "cancelled",
        CancelError.NotRunning e => e.JobId,
        CancelError.Forbidden => "forbidden",
    };

    public static string Generic<TValue, TFailure>(Result<TValue, TFailure> result)
        where TValue : notnull
        where TFailure : class => result switch
        {
            TValue value => $"value {value}",
            TFailure error => $"error {error}",
        };
}

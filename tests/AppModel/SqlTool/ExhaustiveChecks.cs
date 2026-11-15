// Compile-time checks on the document's error types. Not copied from the document.
using Spar;
using SqlTool.CopyJobs;

namespace SqlTool;

internal static class ExhaustiveChecks
{
    /// <summary>Every PlanError case and no default arm: compiles without CS8509 because PlanError is closed.</summary>
    public static string Describe(PlanError error) => error switch
    {
        PlanError.ConnectionNotFound e => $"Unknown connection {e.ConnectionId}",
        PlanError.TableMissing e => $"{e.Table} is missing on {e.Connection}",
    };

    /// <summary>The same for RunError, whose cases carry more than one field.</summary>
    public static string Describe(RunError error) => error switch
    {
        RunError.PlanOutdated e => $"Plan outdated: {e.Reason}",
        RunError.TargetNotEmpty e => $"{e.Table} already has {e.Rows} rows",
        RunError.PartiallyCopied e => $"Stopped after {e.RowsCommitted} rows: {e.Reason}",
    };

    /// <summary>A derived case converts to Result through the closed base, outside an async method too.</summary>
    public static Result<CopyPlan, PlanError> Missing(string table) => new PlanError.TableMissing("src", table);

    public static Result<CopyPlan, PlanError> Planned() => new CopyPlan(Guid.Empty, []);
}

/// <summary>Reading a Result with one exhaustive union switch (the form chosen on 2026-11-14), and a command without a value.</summary>
internal static class ResultChecks
{
    public static async Task<string> PlanMessage(CopyJobs.CopyJobsService service, CopyRecipe recipe, CancellationToken ct) =>
        await service.PlanAsync(recipe, ct) switch
        {
            CopyPlan p => $"{p.Steps.Count} tables",
            PlanError.ConnectionNotFound e => $"Unknown connection {e.ConnectionId}",
            PlanError.TableMissing e => $"{e.Table} is missing on {e.Connection}",
        };

    public static Result<PlanError> Forget(bool known) => known ? Result.Success : new PlanError.ConnectionNotFound("x");

    public static string Read(Result<PlanError> result) => result switch
    {
        Success => "done",
        PlanError.ConnectionNotFound e => $"Unknown connection {e.ConnectionId}",
        PlanError.TableMissing e => $"{e.Table} is missing",
    };
}

// Expected CS8509 on every form: a case of the extracted error is not handled.
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class MissingErrorCase
{
    public static string Describe(Result<CopyReport, RunError> result) => result.TryGetError(out var error)
        ? error switch
        {
            RunError.PlanOutdated e => e.Reason,
            RunError.TargetNotEmpty e => e.Table,
        }
        : "ok";
}

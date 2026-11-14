// Expected CS8509 twice on the union forms: one switch forgets an error case, the other forgets the value.
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class UnionSwitchMissing
{
    public static string MissingCase(Result<CopyReport, RunError> result) => result switch
    {
        CopyReport report => $"{report.Rows} rows",
        RunError.PlanOutdated e => e.Reason,
        RunError.TargetNotEmpty e => e.Table,
    };

    public static string MissingValue(Result<CopyPlan, PlanError> result) => result switch
    {
        PlanError.ConnectionNotFound e => e.ConnectionId,
        PlanError.TableMissing e => e.Table,
    };
}

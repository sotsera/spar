// Must not compile: one PlanError case is missing and there is no default arm (expected CS8509).
// Built only with -p:SparCheckMissingCase=true.
using SqlTool.CopyJobs;

namespace SqlTool;

internal static class ExhaustiveMissingCase
{
    public static string Describe(PlanError error) => error switch
    {
        PlanError.ConnectionNotFound e => $"Unknown connection {e.ConnectionId}",
    };
}

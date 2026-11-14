// A command whose success value may be null (the wire writes "value":null).
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class NullableValue
{
    public static Result<CopyPlan?, PlanError> Found(bool found) => found ? new CopyPlan(Guid.Empty, []) : null;

    public static int Read(Result<CopyPlan?, PlanError> result)
    {
        if (result.TryGetValue(out var plan))
        {
            // plan is CopyPlan?: must be checked.
            return plan?.Steps.Count ?? 0;
        }

        return -1;
    }
}

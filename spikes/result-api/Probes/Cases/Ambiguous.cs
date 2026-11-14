// Value types that overlap the error type. The generator would reject these; how does each form react?
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class Ambiguous
{
    public static Result<object, PlanError> ObjectValue() => new PlanError.ConnectionNotFound("src");

    public static Result<PlanError, PlanError> SameType() => new PlanError.ConnectionNotFound("src");
}

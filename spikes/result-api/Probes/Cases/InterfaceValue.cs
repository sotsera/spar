// A value whose static type is an interface: IReadOnlyList<T> is a wire type.
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class InterfaceValue
{
    public static Result<IReadOnlyList<PlanStep>, PlanError> FromInterface(IReadOnlyList<PlanStep> steps) => steps;

    public static Result<IReadOnlyList<PlanStep>, PlanError> FromList(List<PlanStep> steps) => steps;

    public static Result<IReadOnlyList<PlanStep>, PlanError> FromCollectionExpression() => (PlanStep[])[];
}

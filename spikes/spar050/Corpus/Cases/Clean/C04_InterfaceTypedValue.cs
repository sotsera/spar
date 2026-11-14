// Case: C04 | an interface-typed value converted by the union
// Kind: clean
// Why: an ordinary union conversion.
namespace Corpus.Clean;

public static class C04
{
    public static Result<IReadOnlyList<PlanStep>, PlanError> Steps(IReadOnlyList<PlanStep> steps) => steps;
}

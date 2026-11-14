// Case: L21 | Result<PlanResult, RunError> built from default(PlanResult)
// Kind: detect
// Why: the outer Result is a value, but the value is a default inner Result.
namespace Corpus.Flow;

public static class L21
{
    public static Result<PlanResult, RunError> Nested() => default(PlanResult); //~ SPAR050
}

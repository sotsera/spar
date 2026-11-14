// Case: P14 | ValueTask.FromResult<PlanResult>(default) and new ValueTask<PlanResult>(default(PlanResult))
// Kind: detect
// Why: the default argument is a default Result.
namespace Corpus.Apis;

public static class P14
{
    public static ValueTask<PlanResult> FromResult() => ValueTask.FromResult<PlanResult>(default); //~ SPAR050

    public static ValueTask<PlanResult> Constructed() => new ValueTask<PlanResult>(default(PlanResult)); //~ SPAR050
}

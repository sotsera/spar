// Case: P13 | Task.FromResult<PlanResult>(default) and Task.FromResult(default(PlanResult))
// Kind: detect
// Why: the default argument is a default Result.
namespace Corpus.Apis;

public static class P13
{
    public static Task<PlanResult> Literal() => Task.FromResult<PlanResult>(default); //~ SPAR050

    public static Task<PlanResult> Typed() => Task.FromResult(default(PlanResult)); //~ SPAR050
}

// Case: P09 | Result?.GetValueOrDefault() without a fallback
// Kind: detect
// Why: null becomes default(Result). With a fallback it is clean.
namespace Corpus.Apis;

public static class P09
{
    public static PlanResult Unwrap(PlanResult? maybe) => maybe.GetValueOrDefault(); //~ SPAR051

    public static PlanResult WithFallback(PlanResult? maybe) => maybe.GetValueOrDefault(Values.Failed);
}

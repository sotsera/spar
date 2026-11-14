// Case: P25 | List<PlanResult>.Find and Array.Find
// Kind: detect
// Why: annotated T?; when nothing matches they return default(Result).
namespace Corpus.Apis;

public static class P25
{
    public static PlanResult FirstError(List<PlanResult> results) => results.Find(r => r.IsError); //~ SPAR051

    public static PlanResult FirstErrorIn(PlanResult[] results) => Array.Find(results, r => r.IsError); //~ SPAR051
}

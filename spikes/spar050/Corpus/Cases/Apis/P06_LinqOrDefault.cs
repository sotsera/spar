// Case: P06 | FirstOrDefault, LastOrDefault, SingleOrDefault, ElementAtOrDefault without a fallback
// Kind: detect
// Why: annotated T?; on a miss they return default(Result). SPAR051 offers First()/Last()/Single()/ElementAt().
namespace Corpus.Apis;

public static class P06
{
    public static PlanResult First(IEnumerable<PlanResult> results) => results.FirstOrDefault(); //~ SPAR051

    public static PlanResult Last(IEnumerable<PlanResult> results) => results.LastOrDefault(); //~ SPAR051

    public static PlanResult Single(IEnumerable<PlanResult> results) => results.SingleOrDefault(r => r.IsError); //~ SPAR051

    public static PlanResult At(IEnumerable<PlanResult> results) => results.ElementAtOrDefault(3); //~ SPAR051
}

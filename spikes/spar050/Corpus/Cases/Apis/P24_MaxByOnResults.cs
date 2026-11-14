// Case: P24 | MaxBy / Max over Results
// Kind: clean
// Why: annotated T?, but for a non-nullable value type they throw on an empty sequence, so they are excluded.
namespace Corpus.Apis;

public static class P24
{
    public static PlanResult Latest(IEnumerable<PlanResult> results) => results.MaxBy(r => r.IsSuccess ? 1 : 0);
}

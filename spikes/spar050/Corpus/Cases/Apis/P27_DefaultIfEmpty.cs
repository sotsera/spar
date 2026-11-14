// Case: P27 | DefaultIfEmpty() over Results
// Kind: detect
// Why: an empty sequence becomes one default Result. Not caught by the annotation (it returns IEnumerable<T?>), so it
// is on the known list.
namespace Corpus.Apis;

public static class P27
{
    public static IEnumerable<PlanResult> OrOne(IEnumerable<PlanResult> results) => results.DefaultIfEmpty(); //~ SPAR051
}

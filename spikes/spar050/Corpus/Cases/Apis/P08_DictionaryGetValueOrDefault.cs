// Case: P08 | GetValueOrDefault(key) on a dictionary of Results
// Kind: detect
// Why: a missing key gives default(Result).
namespace Corpus.Apis;

public static class P08
{
    public static PlanResult Lookup(IReadOnlyDictionary<string, PlanResult> results, string key) =>
        results.GetValueOrDefault(key); //~ SPAR051
}

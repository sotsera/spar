// Case: P01 | TryGetValue(key, out var r) with the bool ignored, then r returned
// Kind: detect
// Why: [MaybeNullWhen(false)] says r is default on a miss, and nothing checks the result.
namespace Corpus.Apis;

public static class P01
{
    public static PlanResult Get(Dictionary<string, PlanResult> results, string key)
    {
        results.TryGetValue(key, out var result); //~ SPAR051
        return result;
    }

    public static PlanResult Discarded(Dictionary<string, PlanResult> results, string key)
    {
        _ = results.TryGetValue(key, out var result); //~ SPAR051
        return result;
    }
}

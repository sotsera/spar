// Case: P23 | dictionary[key] on a missing key
// Kind: clean
// Why: throws KeyNotFoundException; no default Result appears.
namespace Corpus.Apis;

public static class P23
{
    public static PlanResult Get(Dictionary<string, PlanResult> results, string key) => results[key];
}

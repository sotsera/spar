// Case: P05 | the bool result stored in a local and tested later
// Kind: missed
// Why: following the bool through a local needs real flow analysis; the runtime guard throws.
namespace Corpus.Apis;

public static class P05
{
    public static PlanResult Get(Dictionary<string, PlanResult> results, string key)
    {
        var found = results.TryGetValue(key, out var result);
        Console.WriteLine(found);
        return result;
    }
}

// Case: P04 | the miss branch only logs, then r is read after the if
// Kind: detect
// Why: the failing branch neither exits nor assigns, so the read after the if can see the default.
namespace Corpus.Apis;

public static class P04
{
    public static PlanResult Get(Dictionary<string, PlanResult> results, string key)
    {
        if (!results.TryGetValue(key, out var result))
        {
            Console.WriteLine($"missing {key}");
        }

        return result; //~ SPAR051
    }
}

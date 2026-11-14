// Case: P02 | r read in the branch where TryGetValue returned false
// Kind: detect
// Why: the failing branch reads the default.
namespace Corpus.Apis;

public static class P02
{
    public static string Negated(Dictionary<string, PlanResult> results, string key)
    {
        if (!results.TryGetValue(key, out var result))
        {
            return result.ToString(); //~ SPAR051
        }

        return "found";
    }

    public static PlanResult ElseBranch(Dictionary<string, PlanResult> results, string key)
    {
        if (results.TryGetValue(key, out var result))
        {
            Console.WriteLine("found");
        }
        else
        {
            return result; //~ SPAR051
        }

        return Values.Ok;
    }
}

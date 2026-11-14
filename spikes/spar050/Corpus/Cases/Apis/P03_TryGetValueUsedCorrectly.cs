// Case: P03 | TryGetValue used as intended (if, early return, ?:, &&, assignment on miss)
// Kind: clean
// Why: r is read only where the call succeeded, or after it was assigned.
namespace Corpus.Apis;

public static class P03
{
    public static PlanResult InIf(Dictionary<string, PlanResult> results, string key)
    {
        if (results.TryGetValue(key, out var result))
        {
            return result;
        }

        return Values.Failed;
    }

    public static PlanResult EarlyReturn(Dictionary<string, PlanResult> results, string key)
    {
        if (!results.TryGetValue(key, out var result))
        {
            return Values.Failed;
        }

        return result;
    }

    public static PlanResult Conditional(Dictionary<string, PlanResult> results, string key) =>
        results.TryGetValue(key, out var result) ? result : Values.Failed;

    public static bool Combined(Dictionary<string, PlanResult> results, string key) =>
        results.TryGetValue(key, out var result) && result.IsSuccess;

    public static PlanResult AssignedOnMiss(Dictionary<string, PlanResult> results, string key)
    {
        if (!results.TryGetValue(key, out var result))
        {
            result = Values.Failed;
        }

        return result;
    }
}

// Case: C07 | reading a Result: switch, TryGetValue/TryGetError with the bool ignored, Value, ToString
// Kind: clean
// Why: TryGetValue's out argument is the T or the error, not a Result.
namespace Corpus.Clean;

public static class C07
{
    public static string Describe(PlanResult result) => result switch
    {
        CopyPlan plan => $"{plan.Steps.Count} steps",
        PlanError.ConnectionNotFound e => e.ConnectionId,
        PlanError.TableMissing e => e.Table,
    };

    public static string Loose(PlanResult result)
    {
        result.TryGetValue(out var plan);
        result.TryGetError(out var error);
        return $"{plan} {error} {result}";
    }
}

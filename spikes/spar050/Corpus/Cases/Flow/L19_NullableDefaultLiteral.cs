// Case: L19 | PlanResult? r = default (null)
// Kind: clean
// Why: the default of Nullable<Result> is null, not a default Result.
namespace Corpus.Flow;

public static class L19
{
    public static PlanResult? Nothing() => default;

    public static PlanResult? Null() => null;
}

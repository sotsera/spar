// Case: L20 | PlanResult? r = default(PlanResult)
// Kind: detect
// Why: HasValue is true and the value inside is a default Result.
namespace Corpus.Flow;

public static class L20
{
    public static PlanResult? Wrapped() => default(PlanResult); //~ SPAR050
}

// Case: L23 | [value, default] as PlanResult[]
// Kind: detect
// Why: the element is a default Result.
namespace Corpus.Flow;

public static class L23
{
    public static PlanResult[] Make() => [Values.Ok, default]; //~ SPAR050
}

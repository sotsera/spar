// Case: L22 | object o = default(PlanResult)
// Kind: detect
// Why: a boxed default Result.
namespace Corpus.Flow;

public static class L22
{
    public static object Boxed() => default(PlanResult); //~ SPAR050
}

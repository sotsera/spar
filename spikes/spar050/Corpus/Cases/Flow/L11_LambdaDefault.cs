// Case: L11 | a lambda returning default
// Kind: detect
// Why: same operation inside the lambda.
namespace Corpus.Flow;

public static class L11
{
    public static Func<PlanResult> Make() => () => default; //~ SPAR050
}

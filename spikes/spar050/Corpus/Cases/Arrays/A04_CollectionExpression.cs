// Case: A04 | collection expressions and spreads
// Kind: clean
// Why: every element comes from a value.
namespace Corpus.Arrays;

public static class A04
{
    public static PlanResult[] Pair() => [Values.Ok, Values.Failed];

    public static List<PlanResult> Spread(IEnumerable<PlanResult> source) => [.. source, Values.Ok];
}

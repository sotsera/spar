// Case: A08 | new PlanResult[2, 3]
// Kind: detect
// Why: every element of a rectangular array starts default.
namespace Corpus.Arrays;

public static class A08
{
    public static PlanResult[,] Grid() => new PlanResult[2, 3]; //~ SPAR052
}

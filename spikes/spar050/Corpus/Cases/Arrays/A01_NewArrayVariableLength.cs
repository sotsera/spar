// Case: A01 | new PlanResult[count] returned
// Kind: detect
// Why: every element is default until assigned.
namespace Corpus.Arrays;

public static class A01
{
    public static PlanResult[] Make(int count) => new PlanResult[count]; //~ SPAR052
}

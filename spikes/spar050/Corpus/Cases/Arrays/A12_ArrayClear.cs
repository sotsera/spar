// Case: A12 | Array.Clear on a Result array
// Kind: detect
// Why: clearing writes default into each slot.
namespace Corpus.Arrays;

public static class A12
{
    public static void ClearAll(PlanResult[] results)
    {
        Array.Clear(results); //~ SPAR052
    }

    public static void ClearRange(PlanResult[] results)
    {
        Array.Clear(results, 0, 2); //~ SPAR052
    }
}

// Case: A10 | Array.Resize growing an array
// Kind: detect
// Why: the new slots are default.
namespace Corpus.Arrays;

public static class A10
{
    public static PlanResult[] Grow(PlanResult[] results, int size)
    {
        Array.Resize(ref results, size); //~ SPAR052
        return results;
    }
}

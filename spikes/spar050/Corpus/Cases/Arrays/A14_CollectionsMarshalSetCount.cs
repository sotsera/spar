// Case: A14 | CollectionsMarshal.SetCount on a List<Result>
// Kind: detect
// Why: growing the count exposes default slots.
using System.Runtime.InteropServices;

namespace Corpus.Arrays;

public static class A14
{
    public static void Grow(List<PlanResult> results, int count)
    {
        CollectionsMarshal.SetCount(results, count); //~ SPAR052
    }
}

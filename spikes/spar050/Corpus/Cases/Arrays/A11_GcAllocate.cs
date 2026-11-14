// Case: A11 | GC.AllocateUninitializedArray and GC.AllocateArray
// Kind: detect
// Why: for a type holding references the runtime zeroes the array, so every element is default.
namespace Corpus.Arrays;

public static class A11
{
    public static PlanResult[] Uninitialized(int count) => GC.AllocateUninitializedArray<PlanResult>(count); //~ SPAR052

    public static PlanResult[] Zeroed(int count) => GC.AllocateArray<PlanResult>(count, pinned: false); //~ SPAR052
}

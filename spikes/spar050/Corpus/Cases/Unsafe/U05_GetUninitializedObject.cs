// Case: U05 | RuntimeHelpers.GetUninitializedObject(typeof(PlanResult))
// Kind: detect
// Why: creates a zeroed (boxed) Result without running any constructor.
using System.Runtime.CompilerServices;

namespace Corpus.Unsafe;

public static class U05
{
    public static object Make() => RuntimeHelpers.GetUninitializedObject(typeof(PlanResult)); //~ SPAR050
}

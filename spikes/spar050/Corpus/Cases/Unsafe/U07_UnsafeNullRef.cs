// Case: U07 | Unsafe.NullRef<PlanResult>() read through
// Kind: runtime
// Why: not a default Result: reading it is a NullReferenceException from the runtime.
namespace Corpus.Unsafe;

public static class U07
{
    public static bool IsNull() => System.Runtime.CompilerServices.Unsafe.IsNullRef(ref System.Runtime.CompilerServices.Unsafe.NullRef<PlanResult>());
}

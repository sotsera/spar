// Case: A23 | stackalloc PlanResult[2]
// Kind: impossible
// Why: Result holds a reference (TError), so it is a managed type and cannot be stack-allocated.
namespace Corpus.Arrays;

public static class A23
{
    public static int Make()
    {
        Span<PlanResult> results = stackalloc PlanResult[2]; //~ CS0208
        return results.Length;
    }
}

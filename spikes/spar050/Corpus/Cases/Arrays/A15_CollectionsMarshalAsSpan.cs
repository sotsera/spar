// Case: A15 | CollectionsMarshal.AsSpan on a List<Result>
// Kind: clean
// Why: the span covers only the Count elements, which were added as values.
using System.Runtime.InteropServices;

namespace Corpus.Arrays;

public static class A15
{
    public static int CountErrors(List<PlanResult> results)
    {
        var errors = 0;
        foreach (var result in CollectionsMarshal.AsSpan(results))
        {
            errors += result.IsError ? 1 : 0;
        }

        return errors;
    }
}

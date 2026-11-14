// Case: U02 | [SkipLocalsInit] on a method with ordinary Result locals
// Kind: clean
// Why: definite assignment still applies; the attribute only matters with stackalloc and Unsafe.SkipInit.
using System.Runtime.CompilerServices;

namespace Corpus.Unsafe;

public static class U02
{
    [SkipLocalsInit]
    public static PlanResult Make(bool ok)
    {
        PlanResult result;
        result = ok ? Values.Ok : Values.Failed;
        return result;
    }
}

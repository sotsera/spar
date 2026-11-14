// Case: U01 | Unsafe.SkipInit(out PlanResult r)
// Kind: detect
// Why: the local is not initialized at all: zero, or stack garbage under [SkipLocalsInit].
using System.Runtime.CompilerServices;

namespace Corpus.Unsafe;

public static class U01
{
    public static PlanResult Make()
    {
        System.Runtime.CompilerServices.Unsafe.SkipInit(out PlanResult result); //~ SPAR050
        return result;
    }
}

// Case: L01 | an out parameter assigned default
// Kind: detect
// Why: without [MaybeNullWhen] the caller has no reason to expect a default.
namespace Corpus.Flow;

public static class L01
{
    public static void Get(out PlanResult result)
    {
        result = default; //~ SPAR050
    }
}

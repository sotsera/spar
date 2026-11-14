// Case: L02 | a Try method that assigns default to a [MaybeNullWhen(false)] out parameter
// Kind: clean
// Why: the attribute declares the default; callers that read it on failure get SPAR051.
using System.Diagnostics.CodeAnalysis;

namespace Corpus.Flow;

public static class L02
{
    public static bool TryGet(int key, [MaybeNullWhen(false)] out PlanResult result)
    {
        if (key < 0)
        {
            result = default;
            return false;
        }

        result = Values.Compute(key);
        return true;
    }
}

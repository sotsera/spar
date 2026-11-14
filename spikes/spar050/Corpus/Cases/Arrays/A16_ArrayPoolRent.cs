// Case: A16 | ArrayPool<Result>.Shared.Rent, and Return with clearArray
// Kind: detect
// Why: a rented array is new (default elements) or was returned by someone else (stale or cleared elements).
// Returning with clearArray: true is not reported: the caller gives the array up.
using System.Buffers;

namespace Corpus.Arrays;

public static class A16
{
    public static PlanResult[] Rent(int count) => ArrayPool<PlanResult>.Shared.Rent(count); //~ SPAR052

    public static void Return(PlanResult[] rented)
    {
        ArrayPool<PlanResult>.Shared.Return(rented, clearArray: true);
    }
}

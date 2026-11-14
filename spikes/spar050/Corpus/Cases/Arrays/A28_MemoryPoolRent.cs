// Case: A28 | MemoryPool<Result>.Shared.Rent
// Kind: detect
// Why: the rented memory is an ArrayPool array underneath, with default or stale elements.
using System.Buffers;

namespace Corpus.Arrays;

public static class A28
{
    public static IMemoryOwner<PlanResult> Rent(int count) => MemoryPool<PlanResult>.Shared.Rent(count); //~ SPAR052
}

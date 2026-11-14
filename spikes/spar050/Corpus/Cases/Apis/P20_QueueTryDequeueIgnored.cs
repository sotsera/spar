// Case: P20 | Queue<PlanResult>.TryDequeue and ConcurrentQueue.TryPeek with the bool ignored
// Kind: detect
// Why: the same [MaybeNullWhen(false)] contract.
using System.Collections.Concurrent;

namespace Corpus.Apis;

public static class P20
{
    public static PlanResult Next(Queue<PlanResult> queue)
    {
        queue.TryDequeue(out var result); //~ SPAR051
        return result;
    }

    public static PlanResult Peek(ConcurrentQueue<PlanResult> queue)
    {
        queue.TryPeek(out var result); //~ SPAR051
        return result;
    }
}

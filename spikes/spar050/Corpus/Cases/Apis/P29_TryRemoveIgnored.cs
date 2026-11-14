// Case: P29 | ConcurrentDictionary.TryRemove(key, out var r) with the bool ignored
// Kind: detect
// Why: the same [MaybeNullWhen(false)] contract as TryGetValue.
using System.Collections.Concurrent;

namespace Corpus.Apis;

public static class P29
{
    public static PlanResult Take(ConcurrentDictionary<string, PlanResult> cache, string key)
    {
        cache.TryRemove(key, out var removed); //~ SPAR051
        return removed;
    }
}

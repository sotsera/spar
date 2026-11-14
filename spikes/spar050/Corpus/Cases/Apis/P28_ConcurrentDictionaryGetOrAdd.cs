// Case: P28 | ConcurrentDictionary.GetOrAdd with a factory
// Kind: clean
// Why: the factory supplies a real Result.
using System.Collections.Concurrent;

namespace Corpus.Apis;

public static class P28
{
    public static PlanResult Get(ConcurrentDictionary<string, PlanResult> cache, string key) =>
        cache.GetOrAdd(key, static _ => Values.Ok);
}

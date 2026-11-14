// Case: A26 | CollectionsMarshal.GetValueRefOrAddDefault on a Dictionary<K, Result>
// Kind: detect
// Why: a missing key is added with a default Result.
using System.Runtime.InteropServices;

namespace Corpus.Arrays;

public static class A26
{
    public static bool Touch(Dictionary<string, PlanResult> results, string key)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(results, key, out var exists); //~ SPAR052
        return exists && slot.IsSuccess;
    }
}

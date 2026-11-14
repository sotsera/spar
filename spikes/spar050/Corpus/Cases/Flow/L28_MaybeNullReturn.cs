// Case: L28 | [return: MaybeNull] PlanResult Find() => default, and a caller
// Kind: detect
// Why: the declaration is clean (the attribute declares the default); the caller gets SPAR051.
using System.Diagnostics.CodeAnalysis;

namespace Corpus.Flow;

public static class L28
{
    [return: MaybeNull]
    public static PlanResult Find(int key) => key > 0 ? Values.Compute(key) : default;

    public static PlanResult Caller() => Find(0); //~ SPAR051
}

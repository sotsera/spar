// Case: P26 | ImmutableArray<PlanResult>.FirstOrDefault()
// Kind: detect
// Why: ImmutableArrayExtensions.FirstOrDefault has the same T? annotation.
using System.Collections.Immutable;

namespace Corpus.Apis;

public static class P26
{
    public static PlanResult First(ImmutableArray<PlanResult> results) => results.FirstOrDefault(); //~ SPAR051
}

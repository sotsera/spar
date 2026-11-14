// Case: A17 | default(ImmutableArray<Result>)
// Kind: clean
// Why: a default ImmutableArray is not a default Result; it fails on its own (IsDefault, NullReferenceException).
using System.Collections.Immutable;

namespace Corpus.Arrays;

public static class A17
{
    public static ImmutableArray<PlanResult> Empty() => default;
}

// Case: A18 | ImmutableArray<Result>.Builder.Count = n
// Kind: detect
// Why: raising Count appends default elements.
using System.Collections.Immutable;

namespace Corpus.Arrays;

public static class A18
{
    public static ImmutableArray<PlanResult> Make(int count)
    {
        var builder = ImmutableArray.CreateBuilder<PlanResult>(count);
        builder.Count = count; //~ SPAR052
        return builder.MoveToImmutable();
    }
}

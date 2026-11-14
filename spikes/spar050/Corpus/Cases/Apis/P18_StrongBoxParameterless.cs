// Case: P18 | new StrongBox<PlanResult>() without a value
// Kind: detect
// Why: Value is default until set. With a value it is clean.
using System.Runtime.CompilerServices;

namespace Corpus.Apis;

public static class P18
{
    public static StrongBox<PlanResult> Empty() => new StrongBox<PlanResult>(); //~ SPAR051

    public static PlanResult Read() => new StrongBox<PlanResult>(Values.Ok).Value;
}

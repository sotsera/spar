// Case: C13 | Result? as the way to say "maybe unset": HasValue, ??, Value
// Kind: clean
// Why: null is the unset state; a fallback is a real Result.
namespace Corpus.Clean;

public static class C13
{
    public static PlanResult OrFallback(PlanResult? maybe) => maybe ?? Values.Failed;

    public static PlanResult Strict(PlanResult? maybe) => maybe.HasValue ? maybe.Value : Values.Failed;

    public static PlanResult? Nothing() => null;
}

// Case: A21 | Enumerable.Repeat<Result>(default, n)
// Kind: detect
// Why: the repeated element is a default Result.
namespace Corpus.Arrays;

public static class A21
{
    public static IEnumerable<PlanResult> Make(int count) => Enumerable.Repeat<PlanResult>(default, count); //~ SPAR050
}

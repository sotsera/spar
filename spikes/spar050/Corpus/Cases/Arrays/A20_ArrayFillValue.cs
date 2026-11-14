// Case: A20 | Array.Fill and Span.Fill with a value
// Kind: clean
// Why: every slot gets a real Result.
namespace Corpus.Arrays;

public static class A20
{
    public static void Fill(PlanResult[] results, Span<PlanResult> span)
    {
        Array.Fill(results, Values.Ok);
        span.Fill(Values.Failed);
    }
}

// Case: A25 | default(ArraySegment<Result>), default(Memory<Result>), Span<Result> = default
// Kind: clean
// Why: these defaults are empty views, not default Results.
namespace Corpus.Arrays;

public static class A25
{
    public static int Lengths()
    {
        ArraySegment<PlanResult> segment = default;
        Memory<PlanResult> memory = default;
        Span<PlanResult> span = default;
        return segment.Count + memory.Length + span.Length;
    }
}

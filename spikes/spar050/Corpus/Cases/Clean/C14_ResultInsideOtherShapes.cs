// Case: C14 | Result inside Task, IEnumerable, Dictionary, Func, with no default
// Kind: clean
// Why: nothing creates a default.
namespace Corpus.Clean;

public static class C14
{
    public static async IAsyncEnumerable<PlanResult> Stream()
    {
        await Task.Yield();
        yield return Values.Ok;
    }

    public static Func<int, PlanResult> Factory() => Values.Compute;

    public static IEnumerable<PlanResult> Lazy()
    {
        yield return Values.Failed;
    }
}

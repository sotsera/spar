// Case: L14 | an async lambda returning default
// Kind: detect
// Why: same operation.
namespace Corpus.Flow;

public static class L14
{
    public static Func<Task<PlanResult>> Make() => async () =>
    {
        await Task.Yield();
        return default; //~ SPAR050
    };
}

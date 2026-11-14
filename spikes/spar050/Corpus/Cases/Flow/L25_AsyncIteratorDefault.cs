// Case: L25 | yield return default in an async iterator
// Kind: detect
// Why: same operation.
namespace Corpus.Flow;

public static class L25
{
    public static async IAsyncEnumerable<PlanResult> Make()
    {
        await Task.Yield();
        yield return default; //~ SPAR050
    }
}

// Case: E02 | return default; in an async method
// Kind: detect
// Why: same operation inside the async state machine body.
namespace Corpus.Existing;

public static class E02
{
    public static async Task<PlanResult> DefaultInAsync()
    {
        await Task.Yield();
        return default; //~ SPAR050
    }
}

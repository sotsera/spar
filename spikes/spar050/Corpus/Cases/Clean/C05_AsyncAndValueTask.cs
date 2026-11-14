// Case: C05 | async, Task.FromResult and ValueTask returns of real Results
// Kind: clean
// Why: every returned Result is a value or an error.
namespace Corpus.Clean;

public static class C05
{
    public static async Task<PlanResult> Async()
    {
        await Task.Yield();
        return Values.Plan;
    }

    public static Task<PlanResult> FromResult() => Task.FromResult(Values.Ok);

    public static ValueTask<PlanResult> Sync() => new(Values.Failed);

    public static ValueTask<CancelResult> NoValue() => ValueTask.FromResult<CancelResult>(Result.Success);
}

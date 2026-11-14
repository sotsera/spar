// Case: P15 | TaskCompletionSource<PlanResult>.SetResult(default) / TrySetResult(default)
// Kind: detect
// Why: the default argument is a default Result.
namespace Corpus.Apis;

public static class P15
{
    public static void Complete(TaskCompletionSource<PlanResult> source)
    {
        source.SetResult(default); //~ SPAR050
    }

    public static bool TryComplete(TaskCompletionSource<PlanResult> source) => source.TrySetResult(default); //~ SPAR050
}

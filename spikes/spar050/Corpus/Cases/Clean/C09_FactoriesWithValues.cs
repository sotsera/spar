// Case: C09 | Lazy, ThreadLocal, StrongBox, TaskCompletionSource with real values
// Kind: clean
// Why: each holder gets a Result through a factory or a value.
using System.Runtime.CompilerServices;

namespace Corpus.Clean;

public static class C09
{
    public static PlanResult All(TaskCompletionSource<PlanResult> source)
    {
        var lazy = new Lazy<PlanResult>(() => Values.Ok);
        using var local = new ThreadLocal<PlanResult>(() => Values.Failed);
        var box = new StrongBox<PlanResult>(Values.Ok);
        source.SetResult(Values.Ok);
        return lazy.Value.IsSuccess && local.Value.IsError ? box.Value : Values.Failed;
    }
}

// Case: P16 | new ThreadLocal<PlanResult>() without a factory
// Kind: detect
// Why: Value is default on each thread until that thread sets it.
namespace Corpus.Apis;

public static class P16
{
    public static ThreadLocal<PlanResult> NoFactory() => new ThreadLocal<PlanResult>(); //~ SPAR051

    public static ThreadLocal<PlanResult> WithFactory() => new ThreadLocal<PlanResult>(() => Values.Ok);
}

// Case: P10 | new Lazy<PlanResult>() without a factory
// Kind: detect
// Why: Lazy<T> then creates the value with Activator.CreateInstance<T>(), which is default for a struct.
namespace Corpus.Apis;

public static class P10
{
    public static Lazy<PlanResult> NoFactory() => new Lazy<PlanResult>(); //~ SPAR050

    public static Lazy<PlanResult> WithFactory() => new Lazy<PlanResult>(() => Values.Ok);
}

// Case: P17 | new AsyncLocal<PlanResult>()
// Kind: detect
// Why: Value is default until the flow sets it (and in flows that never did).
namespace Corpus.Apis;

public static class P17
{
    public static AsyncLocal<PlanResult> Make() => new AsyncLocal<PlanResult>(); //~ SPAR051
}

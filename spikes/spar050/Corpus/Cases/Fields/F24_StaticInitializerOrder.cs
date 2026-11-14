// Case: F24 | a static initializer that reads a static field declared after it
// Kind: detect
// Why: static initializers run in text order, so s_fallback is still default when s_first is initialized.
namespace Corpus.Fields;

public static class F24
{
    private static readonly PlanResult s_first = s_fallback; //~ SPAR053
    private static readonly PlanResult s_fallback = Values.Ok;

    public static PlanResult First => s_first;

    public static PlanResult Fallback => s_fallback;
}

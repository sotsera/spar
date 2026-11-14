// Case: F39 | a static initializer that reads a static field declared before it
// Kind: clean
// Why: text order makes s_fallback initialized first.
namespace Corpus.Fields;

public static class F39
{
    private static readonly PlanResult s_fallback = Values.Ok;
    private static readonly PlanResult s_first = s_fallback;

    public static PlanResult First => s_first;
}

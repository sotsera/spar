// Case: L18 | default! and (PlanResult)default
// Kind: detect
// Why: the null-forgiving operator and a cast do not change the value.
namespace Corpus.Flow;

public static class L18
{
    public static PlanResult Forgiven() => default!; //~ SPAR050

    public static PlanResult Cast() => (PlanResult)default; //~ SPAR050
}

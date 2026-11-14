// Case: G01 | a source generic method returning default(T)!, called with T = PlanResult
// Kind: detect
// Why: SPAR054 joins the body's default(T) with the call's binding at compilation end.
namespace Corpus.Generics;

public static class G01
{
    public static PlanResult Make() => Helpers.Make<PlanResult>(); //~ SPAR054
}

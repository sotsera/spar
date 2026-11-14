// Case: G12 | default(T)! inside a lambda in a generic method, called with PlanResult
// Kind: detect
// Why: the lambda belongs to the method, so its default counts for the method's T.
namespace Corpus.Generics;

public static class G12
{
    public static PlanResult Make() => Helpers.ViaLambda<PlanResult>(); //~ SPAR054
}

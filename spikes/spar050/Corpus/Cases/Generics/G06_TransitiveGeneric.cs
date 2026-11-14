// Case: G06 | Outer<T>() calls Make<T>(), called with PlanResult
// Kind: detect
// Why: the summary follows T through calls between source generic methods.
namespace Corpus.Generics;

public static class G06
{
    public static PlanResult Make() => Helpers.Outer<PlanResult>(); //~ SPAR054
}

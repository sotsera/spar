// Case: G03 | Create<T>() where T : new(), called with PlanResult
// Kind: detect
// Why: every struct satisfies new(), and new T() is then default(Result). Reported from the constraint.
namespace Corpus.Generics;

public static class G03
{
    public static PlanResult Make() => Helpers.Create<PlanResult>(); //~ SPAR050
}

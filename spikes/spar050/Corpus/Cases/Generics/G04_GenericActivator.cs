// Case: G04 | Activator.CreateInstance<T>() inside a generic method, called with PlanResult
// Kind: detect
// Why: the generic summary treats Activator.CreateInstance<T>() like default(T).
namespace Corpus.Generics;

public static class G04
{
    public static PlanResult Make() => Helpers.Activate<PlanResult>(); //~ SPAR054
}

// Case: G08 | Cache<PlanResult>.GetOrDefault() returning default! for the type's T
// Kind: detect
// Why: the binding comes from the receiver's type arguments.
namespace Corpus.Generics;

public static class G08
{
    public static PlanResult Get(Cache<PlanResult> cache) => cache.GetOrDefault(); //~ SPAR054
}

// Case: G07 | Identity<T> and IsDefault<T> (EqualityComparer.Equals(value, default)) with PlanResult
// Kind: clean
// Why: Identity never creates a default; IsDefault only compares against one.
namespace Corpus.Generics;

public static class G07
{
    public static PlanResult Same(PlanResult value) => Helpers.Identity(value);

    public static bool Check(PlanResult value) => Helpers.IsDefault(value);
}

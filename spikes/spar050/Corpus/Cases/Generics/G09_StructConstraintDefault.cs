// Case: G09 | Zero<T>() where T : struct, called with PlanResult
// Kind: detect
// Why: same summary; the struct constraint changes nothing.
namespace Corpus.Generics;

public static class G09
{
    public static PlanResult Make() => Helpers.Zero<PlanResult>(); //~ SPAR054
}

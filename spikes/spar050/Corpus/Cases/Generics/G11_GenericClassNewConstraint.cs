// Case: G11 | new Pool<PlanResult>() where Pool<T> requires T : new()
// Kind: detect
// Why: the constraint binding is reported at the creation of the generic type.
namespace Corpus.Generics;

public static class G11
{
    public static Pool<PlanResult> Make() => new Pool<PlanResult>(); //~ SPAR050
}

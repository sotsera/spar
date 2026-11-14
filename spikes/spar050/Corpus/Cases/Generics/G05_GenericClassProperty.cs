// Case: G05 | Box<PlanResult>.Item initialized with default! in a generic class
// Kind: missed
// Why: the default(T) is in a property initializer of a generic type, not in a method a call reaches; the creation
// new Box<PlanResult>() is not followed into initializers. The runtime guard throws on Item.
namespace Corpus.Generics;

public static class G05
{
    public static PlanResult Read() => new Box<PlanResult>().Item;
}

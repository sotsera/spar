// Case: L03 | an out parameter never assigned
// Kind: impossible
// Why: CS0177.
namespace Corpus.Flow;

public static class L03
{
    public static void Get(out PlanResult result) //~ CS0177
    {
    }
}

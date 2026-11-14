// Case: L13 | yield return default in an iterator
// Kind: detect
// Why: same operation.
namespace Corpus.Flow;

public static class L13
{
    public static IEnumerable<PlanResult> Make()
    {
        yield return Values.Ok;
        yield return default; //~ SPAR050
    }
}

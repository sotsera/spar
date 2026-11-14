// Case: S07 | (int, PlanResult) t = default
// Kind: detect
// Why: ValueTuple's Item2 is a default Result.
namespace Corpus.Structs;

public static class S07
{
    public static (int Id, PlanResult Result) Make()
    {
        (int Id, PlanResult Result) pair = default; //~ SPAR050
        return pair;
    }
}

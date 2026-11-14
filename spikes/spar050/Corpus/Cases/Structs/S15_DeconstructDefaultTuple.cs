// Case: S15 | var (a, b) = default((PlanResult, int))
// Kind: detect
// Why: deconstructs a default Result.
namespace Corpus.Structs;

public static class S15
{
    public static PlanResult Make()
    {
        var (result, _) = default((PlanResult, int)); //~ SPAR050
        return result;
    }
}

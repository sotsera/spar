// Case: L09 | maybe ?? default
// Kind: detect
// Why: the fallback is a default Result.
namespace Corpus.Flow;

public static class L09
{
    public static PlanResult Unwrap(PlanResult? maybe) => maybe ?? default; //~ SPAR050
}

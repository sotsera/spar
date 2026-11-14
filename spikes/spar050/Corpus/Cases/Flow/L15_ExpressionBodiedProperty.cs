// Case: L15 | an expression-bodied property => default
// Kind: detect
// Why: same operation.
namespace Corpus.Flow;

public static class L15
{
    public static PlanResult Last => default; //~ SPAR050
}

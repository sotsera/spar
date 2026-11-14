// Case: L12 | a local function returning default
// Kind: detect
// Why: same operation inside the local function.
namespace Corpus.Flow;

public static class L12
{
    public static PlanResult Make()
    {
        return Local();

        static PlanResult Local() => default; //~ SPAR050
    }
}

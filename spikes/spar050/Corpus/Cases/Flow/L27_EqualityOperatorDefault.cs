// Case: L27 | r == default
// Kind: impossible
// Why: Result declares no == operator (CS0019).
namespace Corpus.Flow;

public static class L27
{
    public static bool IsDefault(PlanResult result) => result == default; //~ CS0019
}

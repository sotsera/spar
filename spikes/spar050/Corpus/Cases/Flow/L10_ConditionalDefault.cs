// Case: L10 | ok ? value : default
// Kind: detect
// Why: the false branch is a default Result.
namespace Corpus.Flow;

public static class L10
{
    public static PlanResult Pick(bool ok) => ok ? Values.Ok : default; //~ SPAR050
}

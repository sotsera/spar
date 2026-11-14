// Case: L17 | an optional Result parameter whose default value is default or new()
// Kind: detect
// Why: every caller that omits the argument passes a default Result. Reported at the parameter.
namespace Corpus.Flow;

public static class L17
{
    public static string Literal(PlanResult result = default) => result.ToString(); //~ SPAR050

    public static string New(PlanResult result = new()) => result.ToString(); //~ SPAR050

    public static string Caller() => Literal() + New();
}

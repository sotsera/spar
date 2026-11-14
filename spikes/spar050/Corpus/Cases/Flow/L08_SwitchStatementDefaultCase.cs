// Case: L08 | default: return default; in a switch statement
// Kind: detect
// Why: same operation.
namespace Corpus.Flow;

public static class L08
{
    public static PlanResult Map(int code)
    {
        switch (code)
        {
            case 0:
                return Values.Ok;
            default:
                return default; //~ SPAR050
        }
    }
}

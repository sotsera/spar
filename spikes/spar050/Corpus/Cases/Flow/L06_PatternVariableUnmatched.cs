// Case: L06 | a pattern variable read where the pattern did not match
// Kind: impossible
// Why: CS0165: pattern variables are definitely assigned only where the pattern matched.
namespace Corpus.Flow;

public static class L06
{
    public static PlanResult Get(object value)
    {
        if (value is PlanResult result)
        {
            return Values.Ok;
        }

        return result; //~ CS0165
    }
}

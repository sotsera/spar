// Case: L05 | an unassigned local returned
// Kind: impossible
// Why: CS0165.
namespace Corpus.Flow;

public static class L05
{
    public static PlanResult Get(bool ok)
    {
        PlanResult result;
        if (ok)
        {
            result = Values.Ok;
        }

        return result; //~ CS0165
    }
}

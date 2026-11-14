// Case: L29 | a local assigned on every path before use
// Kind: clean
// Why: definite assignment holds; no default value is created.
namespace Corpus.Flow;

public static class L29
{
    public static PlanResult Get(int code)
    {
        PlanResult result;
        if (code == 0)
        {
            result = Values.Ok;
        }
        else if (code == 1)
        {
            result = Values.Failed;
        }
        else
        {
            result = Values.Compute(code);
        }

        return result;
    }
}

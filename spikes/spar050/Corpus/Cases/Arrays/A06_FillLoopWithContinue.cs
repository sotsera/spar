// Case: A06 | a fill loop that can skip an element
// Kind: detect
// Why: continue skips the assignment, so the skipped slot stays default. Not the recognised shape.
namespace Corpus.Arrays;

public static class A06
{
    public static PlanResult[] Make(IReadOnlyList<int> inputs)
    {
        var results = new PlanResult[inputs.Count]; //~ SPAR052
        for (var i = 0; i < results.Length; i++)
        {
            if (inputs[i] < 0)
            {
                continue;
            }

            results[i] = Values.Compute(inputs[i]);
        }

        return results;
    }
}

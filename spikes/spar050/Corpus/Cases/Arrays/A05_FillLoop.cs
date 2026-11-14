// Case: A05 | new PlanResult[n] filled by the for loop that follows it
// Kind: clean
// Why: the recognised fill shape writes every element before the array can be read (FillLoop.cs).
namespace Corpus.Arrays;

public static class A05
{
    public static PlanResult[] ByLength(IReadOnlyList<int> inputs)
    {
        var results = new PlanResult[inputs.Count];
        for (var i = 0; i < results.Length; i++)
        {
            results[i] = Values.Compute(inputs[i]);
        }

        return results;
    }

    public static PlanResult[] BySize(int count)
    {
        var results = new PlanResult[count];
        for (var i = 0; i < count; i++)
        {
            var index = i * 2;
            results[i] = Values.Compute(index);
        }

        return results;
    }
}

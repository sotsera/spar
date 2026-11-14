// Case: A24 | new List<Result>(capacity) filled with Add
// Kind: clean
// Why: capacity is not count; nothing default is visible.
namespace Corpus.Arrays;

public static class A24
{
    public static List<PlanResult> Make(int count)
    {
        var results = new List<PlanResult>(count);
        for (var i = 0; i < count; i++)
        {
            results.Add(Values.Compute(i));
        }

        return results;
    }
}

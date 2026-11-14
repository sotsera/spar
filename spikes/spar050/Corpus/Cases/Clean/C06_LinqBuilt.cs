// Case: C06 | collections built from values with Select, ToArray, ToList, ToDictionary
// Kind: clean
// Why: every element comes from a value.
namespace Corpus.Clean;

public static class C06
{
    public static PlanResult[] Array(IEnumerable<int> inputs) => inputs.Select(Values.Compute).ToArray();

    public static Dictionary<int, PlanResult> Map(IEnumerable<int> inputs) => inputs.ToDictionary(i => i, Values.Compute);

    public static PlanResult First(IEnumerable<int> inputs) => inputs.Select(Values.Compute).First();
}

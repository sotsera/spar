// Case: L26 | r.Equals(default), r.Equals(default(PlanResult)), EqualityComparer<PlanResult>.Default.Equals(r, default)
// Kind: clean
// Why: comparing against a default creates no Result anyone reads. Equals(default) passes a null object and is false.
namespace Corpus.Flow;

public static class L26
{
    public static bool ObjectDefault(PlanResult result) => result.Equals(default);

    public static bool ResultDefault(PlanResult result) => result.Equals(default(PlanResult));

    public static bool Comparer(PlanResult result) => EqualityComparer<PlanResult>.Default.Equals(result, default);
}

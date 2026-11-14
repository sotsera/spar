// Case: P07 | FirstOrDefault(fallback), GetValueOrDefault(key, fallback), First()
// Kind: clean
// Why: the fallback is a real Result; First() throws on an empty sequence instead.
namespace Corpus.Apis;

public static class P07
{
    public static PlanResult First(IEnumerable<PlanResult> results) => results.FirstOrDefault(Values.Failed);

    public static PlanResult Lookup(Dictionary<string, PlanResult> results, string key) =>
        results.GetValueOrDefault(key, Values.Failed);

    public static PlanResult Strict(IEnumerable<PlanResult> results) => results.First();
}

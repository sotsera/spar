// Case: P22 | List<PlanResult>.Enumerator.Current read before MoveNext
// Kind: runtime
// Why: List's enumerator returns default(T) there; nothing in the signature says so.
namespace Corpus.Apis;

public static class P22
{
    public static PlanResult Peek(List<PlanResult> results)
    {
        using var enumerator = results.GetEnumerator();
        return enumerator.Current;
    }
}

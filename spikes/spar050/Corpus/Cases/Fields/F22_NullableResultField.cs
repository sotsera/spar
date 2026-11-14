// Case: F22 | the opt-out: a Result? field that may be unset
// Kind: clean
// Why: Nullable<Result> is null, not a default Result, and HasValue says which.
namespace Corpus.Fields;

public sealed class F22
{
    private PlanResult? _last;

    public bool HasLast => _last.HasValue;

    public void Set(PlanResult value) => _last = value;
}

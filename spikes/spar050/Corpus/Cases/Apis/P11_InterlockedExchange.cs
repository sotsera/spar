// Case: P11 | Interlocked.Exchange(ref field, value) on a Result field
// Kind: runtime
// Why: compiles (unconstrained T since .NET 9), but the runtime throws NotSupportedException for a struct that is
// not a primitive size. No default Result is produced (RuntimeProbes checks the exception).
namespace Corpus.Apis;

public sealed class P11
{
    private PlanResult _last = Values.Ok;

    public PlanResult Swap(PlanResult value) => Interlocked.Exchange(ref _last, value);
}

// Case: F31 | the constructor publishes this before assigning the Result
// Kind: runtime
// Why: another thread, or the callee, can read the field while it is default.
namespace Corpus.Fields;

public sealed class F31
{
    private readonly PlanResult _last;

    public F31(List<F31> registry)
    {
        registry.Add(this);
        _last = Values.Ok;
    }

    public PlanResult Last => _last;
}

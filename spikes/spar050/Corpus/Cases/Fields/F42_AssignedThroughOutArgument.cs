// Case: F42 | assigned by passing the field as an out argument in the constructor
// Kind: clean
// Why: an out argument is definitely assigned when the call returns.
namespace Corpus.Fields;

public sealed class F42
{
    private readonly PlanResult _last;

    public F42(int seed)
    {
        Produce(seed, out _last);
    }

    public PlanResult Last => _last;

    private static void Produce(int seed, out PlanResult result) => result = Values.Compute(seed);
}

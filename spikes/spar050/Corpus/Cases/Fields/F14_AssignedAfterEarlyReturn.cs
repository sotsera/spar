// Case: F14 | an early return before the assignment
// Kind: detect
// Why: the return path leaves it default.
namespace Corpus.Fields;

public sealed class F14
{
    private readonly PlanResult _last; //~ SPAR053

    public F14(int count)
    {
        if (count == 0)
        {
            return;
        }

        _last = Values.Compute(count);
    }

    public PlanResult Last => _last;
}

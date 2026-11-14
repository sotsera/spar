// Case: F11 | assigned in every constructor, one chaining to another
// Kind: clean
// Why: : this(…) carries the chained constructor's assignments.
namespace Corpus.Fields;

public sealed class F11
{
    private readonly PlanResult _last;

    public F11(PlanResult last)
    {
        _last = last;
    }

    public F11()
        : this(Values.Ok)
    {
    }

    public PlanResult Last => _last;
}

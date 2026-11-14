// Case: F13 | assigned in only one branch
// Kind: detect
// Why: the other path leaves it default.
namespace Corpus.Fields;

public sealed class F13
{
    private readonly PlanResult _last; //~ SPAR053

    public F13(bool ok)
    {
        if (ok)
        {
            _last = Values.Ok;
        }
    }

    public PlanResult Last => _last;
}

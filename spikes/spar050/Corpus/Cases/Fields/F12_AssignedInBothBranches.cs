// Case: F12 | assigned in both branches of an if/else
// Kind: clean
// Why: every path assigns.
namespace Corpus.Fields;

public sealed class F12
{
    private readonly PlanResult _last;

    public F12(bool ok)
    {
        if (ok)
        {
            _last = Values.Ok;
        }
        else
        {
            _last = Values.Failed;
        }
    }

    public PlanResult Last => _last;
}

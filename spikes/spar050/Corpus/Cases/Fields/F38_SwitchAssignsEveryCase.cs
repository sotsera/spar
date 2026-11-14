// Case: F38 | a switch statement that assigns in every section, with a default section
// Kind: clean
// Why: every path assigns.
namespace Corpus.Fields;

public sealed class F38
{
    private readonly PlanResult _last;

    public F38(int kind)
    {
        switch (kind)
        {
            case 0:
                _last = Values.Ok;
                break;
            default:
                _last = Values.Failed;
                break;
        }
    }

    public PlanResult Last => _last;
}

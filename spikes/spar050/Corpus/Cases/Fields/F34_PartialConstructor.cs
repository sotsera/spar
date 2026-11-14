// Case: F34 | a partial class whose constructor in another part assigns the field
// Kind: clean
// Why: the type is analysed as a whole.
namespace Corpus.Fields;

public sealed partial class F34
{
    private readonly PlanResult _last;

    public PlanResult Last => _last;
}

public sealed partial class F34
{
    public F34(PlanResult last)
    {
        _last = last;
    }
}

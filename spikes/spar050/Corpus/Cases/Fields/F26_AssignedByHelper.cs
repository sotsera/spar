// Case: F26 | assigned by a helper method the constructor calls
// Kind: by-design
// Why: like CS8618, assignments in called methods do not count. Assign in the constructor or use Result?.
namespace Corpus.Fields;

public sealed class F26
{
    private PlanResult _last; //~ SPAR053

    public F26()
    {
        Reset();
    }

    public PlanResult Last => _last;

    private void Reset() => _last = Values.Ok;
}

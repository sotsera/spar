// Case: F35 | assigned by a deconstruction in the constructor
// Kind: clean
// Why: a tuple deconstruction assigns each target.
namespace Corpus.Fields;

public sealed class F35
{
    private readonly PlanResult _first;
    private readonly PlanResult _second;

    public F35(PlanResult first, PlanResult second)
    {
        (_first, _second) = (first, second);
    }

    public PlanResult First => _first;

    public PlanResult Second => _second;
}

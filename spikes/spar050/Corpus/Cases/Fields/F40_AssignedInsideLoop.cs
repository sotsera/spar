// Case: F40 | assigned inside a loop that may not run
// Kind: detect
// Why: zero iterations leave it default.
namespace Corpus.Fields;

public sealed class F40
{
    private readonly PlanResult _last; //~ SPAR053

    public F40(IReadOnlyList<int> inputs)
    {
        foreach (var input in inputs)
        {
            _last = Values.Compute(input);
        }
    }

    public PlanResult Last => _last;
}

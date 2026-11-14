// Case: F28 | assigned in try, catch rethrows
// Kind: clean
// Why: a construction that throws produces no object.
namespace Corpus.Fields;

public sealed class F28
{
    private readonly PlanResult _last;

    public F28(int seed)
    {
        try
        {
            _last = Values.Compute(seed);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
    }

    public PlanResult Last => _last;
}

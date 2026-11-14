// Case: F16 | a primary constructor parameter stored by a field initializer
// Kind: clean
// Why: the initializer runs as part of the primary constructor.
namespace Corpus.Fields;

public sealed class F16(PlanResult last)
{
    private readonly PlanResult _last = last;

    public PlanResult Last => _last;
}

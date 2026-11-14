// Case: F36 | an expression-bodied constructor
// Kind: clean
// Why: the expression body assigns the field.
namespace Corpus.Fields;

public sealed class F36
{
    private readonly PlanResult _last;

    public F36(PlanResult last) => _last = last;

    public PlanResult Last => _last;
}

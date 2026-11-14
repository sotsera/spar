// Case: F15 | a field with an initializer
// Kind: clean
// Why: initialized before any constructor body.
namespace Corpus.Fields;

public sealed class F15
{
    private PlanResult _last = Values.Ok;

    public PlanResult Last => _last;

    public void Set(PlanResult value) => _last = value;
}

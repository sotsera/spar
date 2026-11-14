// Case: F27 | a protected base field assigned only by the derived constructor
// Kind: by-design
// Why: each type's constructors are checked on their own, as CS8618 does.
namespace Corpus.Fields;

public abstract class F27Base
{
    protected PlanResult _last; //~ SPAR053

    public PlanResult Last => _last;
}

public sealed class F27 : F27Base
{
    public F27()
    {
        _last = Values.Ok;
    }
}

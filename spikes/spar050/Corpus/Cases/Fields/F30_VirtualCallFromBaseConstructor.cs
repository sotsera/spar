// Case: F30 | a base constructor calls a virtual method that reads a derived field before it is assigned
// Kind: runtime
// Why: the derived constructor does assign the field, after the base constructor ran. CA2214 (not on by default)
// reports the virtual call; following it across types is beyond a per-type check.
namespace Corpus.Fields;

public abstract class F30Base
{
    protected F30Base()
    {
        Describe();
    }

    protected abstract string Describe();
}

public sealed class F30 : F30Base
{
    private readonly PlanResult _last;

    public F30()
    {
        _last = Values.Ok;
    }

    protected override string Describe() => _last.ToString();
}

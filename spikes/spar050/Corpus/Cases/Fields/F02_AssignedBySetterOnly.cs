// Case: F02 | a private field assigned only by a property setter
// Kind: detect
// Why: readable before the first set; CS0649 is silent because something writes the field.
namespace Corpus.Fields;

public sealed class F02
{
    private PlanResult _current; //~ SPAR053

    public PlanResult Current
    {
        get => _current;
        set => _current = value;
    }
}

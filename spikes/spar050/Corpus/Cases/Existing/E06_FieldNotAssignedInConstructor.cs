// Case: E06 | a field assigned by a method but not by the constructor
// Kind: detect
// Why: between construction and Set, Last reads a default Result. CS0649 is silent because Set writes the field.
namespace Corpus.Existing;

public sealed class E06Holder
{
    private PlanResult _last; //~ SPAR053

    public E06Holder(int seed)
    {
        Seed = seed;
    }

    public int Seed { get; }

    public PlanResult Last => _last;

    public void Set(PlanResult value) => _last = value;
}

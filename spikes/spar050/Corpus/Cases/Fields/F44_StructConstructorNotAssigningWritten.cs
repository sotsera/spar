// Case: F44 | a struct constructor that does not assign a Result field another member writes
// Kind: detect
// Why: the constructor leaves it zeroed, and CS0649 is silent because Set writes it.
namespace Corpus.Fields;

public struct F44
{
    private PlanResult _last; //~ SPAR053

    public F44(int id)
    {
        Id = id;
    }

    public int Id { get; }

    public readonly PlanResult Last => _last;

    public void Set(PlanResult value) => _last = value;
}

// Case: F19 | a struct constructor that does not assign the Result field
// Kind: compiler
// Why: since C# 11 unassigned struct fields are zeroed, so the field is default after this constructor. Nothing
// writes it, so CS0649 reports it and SPAR053 does not (F44 is the same struct with a writer).
namespace Corpus.Fields;

public readonly struct F19
{
    private readonly PlanResult _last; //~ CS0649

    public F19(int id)
    {
        Id = id;
    }

    public int Id { get; }

    public PlanResult Last => _last;
}

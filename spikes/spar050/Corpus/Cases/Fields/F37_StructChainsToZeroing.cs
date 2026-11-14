// Case: F37 | a struct constructor chaining to the implicit this() and not assigning the Result
// Kind: detect
// Why: this() zeroes every field.
namespace Corpus.Fields;

public struct F37
{
    public PlanResult Last; //~ SPAR053

    public F37(int id)
        : this()
    {
        Id = id;
    }

    public int Id { get; }
}

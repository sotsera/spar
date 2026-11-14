// Case: F07 | a getter-only auto-property never assigned
// Kind: detect
// Why: no compiler diagnostic exists for an unassigned getter-only auto-property of a struct type.
namespace Corpus.Fields;

public sealed class F07
{
    public F07(int id)
    {
        Id = id;
    }

    public int Id { get; }

    public PlanResult Last { get; } //~ SPAR053
}

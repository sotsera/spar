// Case: F33 | a property using the field keyword, never assigned by the constructor
// Kind: detect
// Why: the synthesized backing field starts default.
namespace Corpus.Fields;

public sealed class F33
{
    public PlanResult Last //~ SPAR053
    {
        get => field;
        set => field = value;
    }
}

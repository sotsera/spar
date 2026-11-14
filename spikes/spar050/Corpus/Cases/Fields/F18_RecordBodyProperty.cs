// Case: F18 | a record with a Result property declared in its body
// Kind: detect
// Why: new F18() leaves it default unless an initializer sets it.
namespace Corpus.Fields;

public sealed record F18
{
    public PlanResult Last { get; init; } //~ SPAR053
}

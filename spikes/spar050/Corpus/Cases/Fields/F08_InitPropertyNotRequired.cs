// Case: F08 | an init-only property that object initializers may omit
// Kind: detect
// Why: new F08() without the initializer leaves it default. The fix is required.
namespace Corpus.Fields;

public sealed class F08
{
    public PlanResult Last { get; init; } //~ SPAR053
}

// Case: F06 | a { get; set; } auto-property not assigned by the constructor
// Kind: detect
// Why: the backing field starts default. This is what CS8618 reports for a non-nullable reference.
namespace Corpus.Fields;

public sealed class F06
{
    public PlanResult Last { get; set; } //~ SPAR053
}

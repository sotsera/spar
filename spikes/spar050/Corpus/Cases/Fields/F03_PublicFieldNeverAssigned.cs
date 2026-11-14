// Case: F03 | a public field that nothing in the assembly assigns
// Kind: detect
// Why: CS0649 ignores public fields (another assembly could assign them); SPAR053 reports it.
namespace Corpus.Fields;

public sealed class F03
{
    public PlanResult Last; //~ SPAR053
}

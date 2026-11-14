// Case: F17 | a positional record with a Result parameter
// Kind: clean
// Why: the synthesized property is assigned from the parameter.
namespace Corpus.Fields;

public sealed record F17(PlanResult Last, int Attempts);

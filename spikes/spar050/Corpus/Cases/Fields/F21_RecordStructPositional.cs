// Case: F21 | a positional record struct
// Kind: clean
// Why: the declaration is fine; its parameterless new() is reported at the use (S06).
namespace Corpus.Fields;

public readonly record struct F21(PlanResult Last, int Id);

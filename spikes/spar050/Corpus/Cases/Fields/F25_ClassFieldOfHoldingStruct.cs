// Case: F25 | a class field whose struct type holds a Result
// Kind: detect
// Why: a default F25Pair holds a default Result.
namespace Corpus.Fields;

public readonly record struct F25Pair(int Id, PlanResult Result);

public sealed class F25
{
    public F25Pair Pair { get; set; } //~ SPAR053
}

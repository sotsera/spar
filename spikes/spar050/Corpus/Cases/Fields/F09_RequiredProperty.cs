// Case: F09 | a required property
// Kind: clean
// Why: the compiler (CS9035) makes every object creation set it.
namespace Corpus.Fields;

public sealed class F09
{
    public required PlanResult Last { get; init; }

    public static F09 Make() => new() { Last = Values.Ok };
}

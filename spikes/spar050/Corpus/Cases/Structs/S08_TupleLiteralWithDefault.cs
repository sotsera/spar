// Case: S08 | (1, default) converted to (int, PlanResult)
// Kind: detect
// Why: the default element is target-typed to the Result.
namespace Corpus.Structs;

public static class S08
{
    public static (int Id, PlanResult Result) Make() => (1, default); //~ SPAR050
}

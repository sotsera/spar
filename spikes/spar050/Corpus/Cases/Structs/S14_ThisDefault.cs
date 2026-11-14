// Case: S14 | this = default in a struct method
// Kind: detect
// Why: resets every field, including the Result.
namespace Corpus.Structs;

public struct S14
{
    public S14(PlanResult last)
    {
        Last = last;
    }

    public PlanResult Last { get; private set; }

    public void Reset()
    {
        this = default; //~ SPAR050
    }
}

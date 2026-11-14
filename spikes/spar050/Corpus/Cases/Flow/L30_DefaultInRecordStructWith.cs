// Case: L30 | a struct holding a Result, reset with this = new()
// Kind: detect
// Why: new() of a struct without a parameterless constructor is default.
namespace Corpus.Flow;

public struct L30
{
    public PlanResult Last;

    public void Reset()
    {
        this = new(); //~ SPAR050
    }
}

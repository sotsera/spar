// Case: F20 | a struct with a public Result field and no constructor
// Kind: clean
// Why: nothing to report at the declaration; default(F20) and new F20() are reported where they are written (S01–S04).
namespace Corpus.Fields;

public struct F20
{
    public PlanResult Last;

    public int Id;
}

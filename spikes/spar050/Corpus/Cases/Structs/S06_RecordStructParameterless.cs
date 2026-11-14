// Case: S06 | new() of a positional record struct
// Kind: detect
// Why: a record struct still has the zeroing parameterless constructor.
namespace Corpus.Structs;

public static class S06
{
    public static Holder Make() => new Holder(); //~ SPAR050
}

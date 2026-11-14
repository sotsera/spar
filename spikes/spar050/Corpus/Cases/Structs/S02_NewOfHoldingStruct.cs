// Case: S02 | new Pair() without an explicit parameterless constructor
// Kind: detect
// Why: same as default(Pair).
namespace Corpus.Structs;

public static class S02
{
    public static Pair Make() => new Pair(); //~ SPAR050
}

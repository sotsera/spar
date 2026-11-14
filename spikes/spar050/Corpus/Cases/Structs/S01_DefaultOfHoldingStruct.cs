// Case: S01 | default(Pair) where Pair has a Result field
// Kind: detect
// Why: the struct's Result field is default.
namespace Corpus.Structs;

public static class S01
{
    public static Pair Make() => default(Pair); //~ SPAR050
}

// Case: S13 | default(Holder) with { Attempts = 1 }
// Kind: detect
// Why: with copies the default Result.
namespace Corpus.Structs;

public static class S13
{
    public static Holder Make() => default(Holder) with { Attempts = 1 }; //~ SPAR050
}

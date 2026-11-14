// Case: S05 | a struct whose parameterless constructor assigns the Result, and default() of it
// Kind: detect
// Why: new() runs the constructor (clean); default(…) still skips it (SPAR050).
namespace Corpus.Structs;

public static class S05
{
    public static PairWithConstructor Constructed() => new PairWithConstructor();

    public static PairWithConstructor Defaulted() => default; //~ SPAR050
}

// Case: S12 | default(Outer) where Outer holds a Pair that holds a Result
// Kind: detect
// Why: containment is followed through nested structs.
namespace Corpus.Structs;

public static class S12
{
    public static Outer Make() => default; //~ SPAR050
}

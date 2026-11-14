// Case: S04 | new Pair { Id = 1 }
// Kind: detect
// Why: the initializer leaves the Result default.
namespace Corpus.Structs;

public static class S04
{
    public static Pair Make() => new Pair { Id = 1 }; //~ SPAR050
}

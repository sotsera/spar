// Case: S03 | new Pair { Result = …, Id = 1 }
// Kind: clean
// Why: the object initializer sets every member that holds a Result.
namespace Corpus.Structs;

public static class S03
{
    public static Pair Make() => new Pair { Result = Values.Ok, Id = 1 };
}

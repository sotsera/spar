// Case: X10 | a library method that returns an array of default Results
// Kind: runtime
// Why: the array creation is inside the library.
using Library;

namespace Corpus.External;

public static class X10
{
    public static Result<string, LibError>[] Get() => Factory.MakeArray(3);
}

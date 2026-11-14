// Case: X07 | a library Find<T>() declared T?, called with a Result
// Kind: detect
// Why: the T? annotation is in metadata.
using Library;

namespace Corpus.External;

public static class X07
{
    public static Result<string, LibError> Get() => Generic.Find<Result<string, LibError>>(); //~ SPAR051
}

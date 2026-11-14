// Case: X06 | a library Make<T>() => default!, unannotated, called with a Result
// Kind: runtime
// Why: the body is IL in another assembly; SPAR054 needs the body in source.
using Library;

namespace Corpus.External;

public static class X06
{
    public static Result<string, LibError> Get() => Generic.Make<Result<string, LibError>>();
}

// Case: X05 | a library Create<T>() where T : new(), called with a Result
// Kind: detect
// Why: the constraint is in the signature.
using Library;

namespace Corpus.External;

public static class X05
{
    public static Result<string, LibError> Get() => Generic.Create<Result<string, LibError>>(); //~ SPAR050
}

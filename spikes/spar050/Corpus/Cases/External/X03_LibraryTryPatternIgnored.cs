// Case: X03 | a library Try method with [MaybeNullWhen(false)], bool ignored
// Kind: detect
// Why: the attribute is in metadata.
using Library;

namespace Corpus.External;

public static class X03
{
    public static Result<string, LibError> Get()
    {
        Factory.TryMake("", out var result); //~ SPAR051
        return result;
    }
}

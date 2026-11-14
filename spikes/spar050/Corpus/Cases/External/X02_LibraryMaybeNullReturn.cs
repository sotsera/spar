// Case: X02 | a library method marked [return: MaybeNull]
// Kind: detect
// Why: the attribute is in metadata, so SPAR051 works across assemblies.
using Library;

namespace Corpus.External;

public static class X02
{
    public static Result<string, LibError> Get() => Factory.Find(""); //~ SPAR051
}

// Case: X01 | a library method that returns default with no hint in its signature
// Kind: runtime
// Why: only the library's IL knows; the consumer sees an ordinary Result return. The runtime guard throws on use.
using Library;

namespace Corpus.External;

public static class X01
{
    public static Result<string, LibError> Get() => Factory.Make();
}

// Case: X04 | a library class whose public Result field nothing assigns
// Kind: runtime
// Why: the library was not built with the analyzer; the consumer only reads a field.
using Library;

namespace Corpus.External;

public static class X04
{
    public static Result<string, LibError> Get() => new Holder().Last;
}

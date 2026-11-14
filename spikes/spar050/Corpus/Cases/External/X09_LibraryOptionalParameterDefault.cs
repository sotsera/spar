// Case: X09 | a call that omits a library method's optional Result parameter (= default)
// Kind: detect
// Why: the omitted argument is an implicit default Result, reported at the call because the parameter is metadata.
using Library;

namespace Corpus.External;

public static class X09
{
    public static string Get() => Factory.Describe(); //~ SPAR050
}

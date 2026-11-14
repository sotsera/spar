// Case: X08 | default(LibPair), a library struct with a private Result field
// Kind: detect
// Why: depends on whether the analyzer can see private fields of a referenced struct (see RESULTS.md).
using Library;

namespace Corpus.External;

public static class X08
{
    public static LibPair Get() => default; //~ SPAR050
}

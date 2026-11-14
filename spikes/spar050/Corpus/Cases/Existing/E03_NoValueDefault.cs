// Case: E03 | Result<CancelError> => default
// Kind: detect
// Why: Result`1 is recognised like Result`2.
namespace Corpus.Existing;

public static class E03
{
    public static CancelResult NoValueDefault() => default; //~ SPAR050
}

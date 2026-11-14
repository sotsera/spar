// Case: E04 | new() of a Result
// Kind: detect
// Why: new() on a struct without an explicit parameterless constructor is default.
namespace Corpus.Existing;

public static class E04
{
    public static PlanResult NewWithoutArguments() => new(); //~ SPAR050
}

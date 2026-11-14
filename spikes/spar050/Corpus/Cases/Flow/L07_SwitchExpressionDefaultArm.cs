// Case: L07 | a switch expression whose discard arm is default
// Kind: detect
// Why: the arm's default is target-typed to the Result.
namespace Corpus.Flow;

public static class L07
{
    public static PlanResult Map(int code) => code switch
    {
        0 => Values.Ok,
        1 => Values.Failed,
        _ => default, //~ SPAR050
    };
}

// Case: S16 | default(Wrapper<PlanResult>)
// Kind: detect
// Why: the constructed type's field is a Result.
namespace Corpus.Structs;

public static class S16
{
    public static Wrapper<PlanResult> Make() => default; //~ SPAR050
}

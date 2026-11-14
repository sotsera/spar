// Case: S10 | default(ValueTask<PlanResult>) and new ValueTask<PlanResult>()
// Kind: detect
// Why: a default ValueTask<T> is completed with default(T); awaiting it gives a default Result.
namespace Corpus.Structs;

public static class S10
{
    public static ValueTask<PlanResult> Defaulted() => default; //~ SPAR050

    public static ValueTask<PlanResult> Constructed() => new ValueTask<PlanResult>(); //~ SPAR050
}

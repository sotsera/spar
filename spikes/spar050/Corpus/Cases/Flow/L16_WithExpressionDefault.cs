// Case: L16 | record with { Last = default }
// Kind: detect
// Why: the assigned value is a default Result.
namespace Corpus.Flow;

public sealed record L16Holder(PlanResult Last, int Attempts);

public static class L16
{
    public static L16Holder Reset(L16Holder holder) => holder with { Last = default }; //~ SPAR050
}

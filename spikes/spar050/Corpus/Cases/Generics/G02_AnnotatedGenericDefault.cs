// Case: G02 | a generic method declared T? (maybe default), called with PlanResult
// Kind: detect
// Why: the T? annotation is SPAR051 at the call, from the signature alone.
namespace Corpus.Generics;

public static class G02
{
    public static PlanResult Find() => Helpers.Find<PlanResult>(); //~ SPAR051
}

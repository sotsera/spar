// Case: F05 | a static field assigned by an Initialize method
// Kind: detect
// Why: readable before Initialize runs.
namespace Corpus.Fields;

public static class F05
{
    private static PlanResult s_cached; //~ SPAR053

    public static PlanResult Cached => s_cached;

    public static void Initialize() => s_cached = Values.Ok;
}

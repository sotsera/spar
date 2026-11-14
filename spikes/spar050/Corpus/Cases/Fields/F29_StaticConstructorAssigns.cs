// Case: F29 | a static field assigned by the static constructor
// Kind: clean
// Why: the static constructor runs before any access.
namespace Corpus.Fields;

public static class F29
{
    private static readonly PlanResult s_last;

    static F29()
    {
        s_last = Values.Ok;
    }

    public static PlanResult Last => s_last;
}

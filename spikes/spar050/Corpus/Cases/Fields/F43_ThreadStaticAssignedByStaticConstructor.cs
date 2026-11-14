// Case: F43 | a [ThreadStatic] Result field assigned by the static constructor
// Kind: detect
// Why: the static constructor runs on one thread; every other thread sees default. CA2019 covers only initializers.
namespace Corpus.Fields;

public static class F43
{
    [ThreadStatic]
    private static PlanResult t_last; //~ SPAR053

    static F43()
    {
        t_last = Values.Ok;
    }

    public static PlanResult Last => t_last;
}

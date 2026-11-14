// Case: F23 | a [ThreadStatic] Result field with an initializer
// Kind: compiler
// Why: the initializer runs once, on the thread that runs the static constructor; every other thread sees default.
// .NET's CA2019 reports it (on by default), so SPAR053 does not.
namespace Corpus.Fields;

public static class F23
{
    [ThreadStatic]
    private static PlanResult t_last = Values.Ok; //~ CA2019

    public static PlanResult Last => t_last;

    public static void Set(PlanResult value) => t_last = value;
}

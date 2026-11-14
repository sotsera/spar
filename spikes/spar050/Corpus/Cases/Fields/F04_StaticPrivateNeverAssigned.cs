// Case: F04 | a private static field that nothing assigns
// Kind: compiler
// Why: CS0649 covers static fields too.
namespace Corpus.Fields;

public static class F04
{
    private static PlanResult s_last; //~ CS0649

    public static PlanResult Last => s_last;
}

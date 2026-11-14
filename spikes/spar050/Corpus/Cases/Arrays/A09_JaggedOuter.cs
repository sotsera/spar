// Case: A09 | new PlanResult[3][] (jagged outer array)
// Kind: clean
// Why: the elements are null arrays, not Results.
namespace Corpus.Arrays;

public static class A09
{
    public static PlanResult[][] Rows() => new PlanResult[3][];
}

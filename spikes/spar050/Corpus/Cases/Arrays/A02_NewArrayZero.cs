// Case: A02 | new PlanResult[0]
// Kind: clean
// Why: an empty array has no element to read. (.NET's CA1825 suggests Array.Empty.)
namespace Corpus.Arrays;

public static class A02
{
    public static PlanResult[] Make() => new PlanResult[0]; //~ CA1825
}

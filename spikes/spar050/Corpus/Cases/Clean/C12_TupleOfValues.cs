// Case: C12 | tuples and record structs built from values
// Kind: clean
// Why: every Result element is a value.
namespace Corpus.Clean;

public readonly record struct C12Holder(PlanResult Last, int Attempts);

public static class C12
{
    public static (int Id, PlanResult Result) Pair() => (1, Values.Ok);

    public static C12Holder Holder() => new(Values.Failed, 1);

    public static KeyValuePair<string, PlanResult> Entry() => new("a", Values.Ok);
}

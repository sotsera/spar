// Case: A03 | new PlanResult[] { … } and new PlanResult[2] { … }
// Kind: clean
// Why: the compiler requires one initializer element per slot.
namespace Corpus.Arrays;

public static class A03
{
    public static PlanResult[] Implicit() => new PlanResult[] { Values.Ok, Values.Failed };

    public static PlanResult[] Sized() => new PlanResult[2] { Values.Ok, Values.Failed };
}

// Shared struct types for the S cases (not a case).
namespace Corpus.Structs;

public struct Pair
{
    public int Id;

    public PlanResult Result;
}

public struct PairWithConstructor
{
    public PairWithConstructor()
    {
        Result = Values.Ok;
    }

    public PlanResult Result { get; }
}

public readonly record struct Holder(PlanResult Last, int Attempts);

public struct Outer
{
    public Pair Inner;

    public string Name;
}

public struct Wrapper<T>
{
    public T Item;
}

// Case: C10 | a class with required, initialized and constructor-assigned Result members
// Kind: clean
// Why: every member is assigned before it can be read.
namespace Corpus.Clean;

public sealed class C10
{
    private readonly PlanResult _fromConstructor;

    public C10(PlanResult fromConstructor)
    {
        _fromConstructor = fromConstructor;
    }

    public required PlanResult Required { get; init; }

    public PlanResult Initialized { get; set; } = Values.Ok;

    public PlanResult FromConstructor => _fromConstructor;

    public static C10 Make() => new(Values.Failed) { Required = Values.Ok };
}

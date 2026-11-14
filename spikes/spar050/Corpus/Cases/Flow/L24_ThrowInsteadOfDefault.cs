// Case: L24 | throw where a value cannot be produced
// Kind: clean
// Why: no default value exists.
namespace Corpus.Flow;

public static class L24
{
    public static PlanResult Map(int code) => code switch
    {
        0 => Values.Ok,
        1 => Values.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };
}

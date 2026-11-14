// Case: U06 | Activator.CreateInstance<PlanResult>() and Activator.CreateInstance(typeof(PlanResult))
// Kind: detect
// Why: both run the zeroing parameterless constructor of the struct.
namespace Corpus.Unsafe;

public static class U06
{
    public static PlanResult Generic() => Activator.CreateInstance<PlanResult>(); //~ SPAR050

    public static object? ByType() => Activator.CreateInstance(typeof(PlanResult)); //~ SPAR050 CA2263
}

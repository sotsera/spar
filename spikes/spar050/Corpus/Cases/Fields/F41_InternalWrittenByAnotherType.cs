// Case: F41 | an internal field written only by another type
// Kind: missed
// Why: SPAR053 leaves fields its type never writes to CS0649, and CS0649 is silent because another type writes it.
// Seeing that would need a compilation-wide pass; the runtime guard throws.
namespace Corpus.Fields;

public sealed class F41
{
    internal PlanResult Last;
}

public static class F41Writer
{
    public static void Fill(F41 target) => target.Last = Values.Ok;
}

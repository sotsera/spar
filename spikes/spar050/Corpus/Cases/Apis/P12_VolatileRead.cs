// Case: P12 | Volatile.Read(ref field) of a Result field
// Kind: impossible
// Why: the generic overload is constrained to reference types.
namespace Corpus.Apis;

public sealed class P12
{
    private PlanResult _last = Values.Ok;

    public PlanResult Read() => Volatile.Read(ref _last); //~ CS0452
}

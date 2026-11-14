// Case: F32 | an internal field that nothing assigns
// Kind: compiler
// Why: CS0649 also covers internal fields.
namespace Corpus.Fields;

public sealed class F32
{
    internal PlanResult Last; //~ CS0649
}

// Case: F01 | a private instance field that nothing assigns
// Kind: compiler
// Why: CS0649 (a warning, an error with TreatWarningsAsErrors). SPAR053 stays silent so the problem has one report.
namespace Corpus.Fields;

public sealed class F01
{
    private PlanResult _last; //~ CS0649

    public PlanResult Last => _last;
}

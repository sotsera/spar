// Case: X11 | a third-party generator's output that returns default, and a generated unassigned field
// Kind: runtime
// Why: generated code is not analysed (GeneratedCodeAnalysisFlags.None): the user cannot fix it. The consumer's call
// looks ordinary. The generated file is Defaults.g.cs from CorpusGenerator.
using Corpus.Generated;

namespace Corpus.External;

public static class X11
{
    public static PlanResult Get() => GeneratedFactory.Make();

    public static PlanResult Field() => new GeneratedHolder().Last;
}

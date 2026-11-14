// Case: E01 | => default (spikes/result-api DefaultValue.cs)
// Kind: detect
// Why: the default literal converted to a Result is an IDefaultValueOperation of a Result type.
namespace Corpus.Existing;

public static class E01
{
    public static PlanResult DefaultLiteral() => default; //~ SPAR050
}

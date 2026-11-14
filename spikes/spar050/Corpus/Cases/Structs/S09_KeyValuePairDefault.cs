// Case: S09 | default(KeyValuePair<string, PlanResult>)
// Kind: detect
// Why: KeyValuePair stores the value inline. Its fields are private in metadata, so it is on a short known list.
namespace Corpus.Structs;

public static class S09
{
    public static KeyValuePair<string, PlanResult> Make() => default; //~ SPAR050
}

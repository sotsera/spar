// Case: A27 | copying into a larger array, or ToArray over a partially filled pool array
// Kind: detect
// Why: the destination array creation is reported; the slots after the copied range stay default.
namespace Corpus.Arrays;

public static class A27
{
    public static PlanResult[] Pad(PlanResult[] source)
    {
        var padded = new PlanResult[source.Length + 4]; //~ SPAR052
        source.CopyTo(padded, 0);
        return padded;
    }
}

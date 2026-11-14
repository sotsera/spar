// Case: A07 | new PlanResult[n] filled with Parallel.For
// Kind: by-design
// Why: correct code, but the analyzer cannot prove the delegate writes every slot. Rewrite with Select/ToArray or
// suppress at the site.
namespace Corpus.Arrays;

public static class A07
{
    public static PlanResult[] Make(int count)
    {
        var results = new PlanResult[count]; //~ SPAR052
        Parallel.For(0, count, i => results[i] = Values.Compute(i));
        return results;
    }
}

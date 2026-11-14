// Case: A13 | Span<Result>.Clear() and Span<Result>.Fill(default)
// Kind: detect
// Why: Clear writes default (SPAR052); Fill(default) passes a default Result (SPAR050 on the argument). CA1855
// (.NET) suggests Clear() for Fill(default), which SPAR052 then reports.
namespace Corpus.Arrays;

public static class A13
{
    public static void Clear(Span<PlanResult> results)
    {
        results.Clear(); //~ SPAR052
    }

    public static void Fill(Span<PlanResult> results)
    {
        results.Fill(default); //~ SPAR050 CA1855
    }
}

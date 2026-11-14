// Case: A19 | Array.CreateInstance(typeof(Result), n)
// Kind: detect
// Why: the reflection-style factory creates a zeroed array.
namespace Corpus.Arrays;

public static class A19
{
    public static Array Make(int count) => Array.CreateInstance(typeof(PlanResult), count); //~ SPAR052
}

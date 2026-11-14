// Case: E05 | new Result<…>[4]
// Kind: detect
// Why: an array creation without an initializer leaves every element default (SPAR052, a warning: the array may
// still be filled before it is read).
namespace Corpus.Existing;

public static class E05
{
    public static PlanResult[] Array() => new PlanResult[4]; //~ SPAR052
}

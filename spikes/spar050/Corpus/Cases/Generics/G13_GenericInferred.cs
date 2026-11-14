// Case: G13 | a type argument inferred from an argument, Outer chain through Identity
// Kind: clean
// Why: Identity<T>(value) passes T through without creating a default.
namespace Corpus.Generics;

public static class G13
{
    public static PlanResult[] Twice(PlanResult value) => [Helpers.Identity(value), Helpers.Identity(Values.Ok)];
}

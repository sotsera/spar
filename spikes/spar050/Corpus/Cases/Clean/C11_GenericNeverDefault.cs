// Case: C11 | generic code over Results that never creates a default
// Kind: clean
// Why: values flow in and out.
namespace Corpus.Clean;

public static class C11
{
    public static T Pick<T>(bool first, T a, T b) => first ? a : b;

    public static List<T> Collect<T>(params ReadOnlySpan<T> items) => [.. items];

    public static PlanResult Use() => Pick(true, Values.Ok, Values.Failed);

    public static List<PlanResult> UseMany() => Collect(Values.Ok, Values.Failed);
}

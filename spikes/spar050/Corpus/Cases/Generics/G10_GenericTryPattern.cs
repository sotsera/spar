// Case: G10 | a generic Try method with [MaybeNullWhen(false)] out T, used correctly and with the result ignored
// Kind: detect
// Why: its own found = default is declared, not reported; ignoring the bool is SPAR051 at the call.
namespace Corpus.Generics;

public static class G10
{
    public static PlanResult Checked(IReadOnlyList<PlanResult> items) =>
        Helpers.TryFind(items, r => r.IsError, out var found) ? found : Values.Ok;

    public static PlanResult Ignored(IReadOnlyList<PlanResult> items)
    {
        Helpers.TryFind(items, r => r.IsError, out var found); //~ SPAR051
        return found;
    }
}

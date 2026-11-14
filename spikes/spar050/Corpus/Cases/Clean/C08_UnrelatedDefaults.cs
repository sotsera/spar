// Case: C08 | default of other types: CancellationToken, int, string, a class, a struct without Result
// Kind: clean
// Why: only values that hold a Result are reported.
namespace Corpus.Clean;

public readonly record struct C08Point(int X, int Y);

public static class C08
{
    public static async Task<int> Defaults(CancellationToken cancellationToken = default)
    {
        await Task.Delay(0, cancellationToken);
        int count = default;
        string? name = default;
        var point = new C08Point();
        var points = new C08Point[4];
        return count + (name?.Length ?? 0) + point.X + points.Length;
    }
}

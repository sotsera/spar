// Case: C02 | Result.Success and default(Success)
// Kind: clean
// Why: Success is a valueless marker struct; its default is the success.
namespace Corpus.Clean;

public static class C02
{
    public static CancelResult Done() => Result.Success;

    public static CancelResult Forbidden() => new CancelError.Forbidden();

    public static Success Marker() => default;
}

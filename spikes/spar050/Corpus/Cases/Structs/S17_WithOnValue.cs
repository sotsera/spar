// Case: S17 | holder with { Attempts = 2 } on a real value
// Kind: clean
// Why: with copies a valid Result.
namespace Corpus.Structs;

public static class S17
{
    public static Holder Retry(Holder holder) => holder with { Attempts = holder.Attempts + 1 };

    public static Holder Make() => new Holder(Values.Ok, 0);
}

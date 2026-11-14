// Case: C03 | a switch expression whose every arm is a value or an error
// Kind: clean
// Why: no default arm value.
namespace Corpus.Clean;

public static class C03
{
    public static PlanResult Map(int code) => code switch
    {
        0 => Values.Plan,
        1 => new PlanError.ConnectionNotFound("main"),
        _ => new PlanError.TableMissing("src", code.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };
}

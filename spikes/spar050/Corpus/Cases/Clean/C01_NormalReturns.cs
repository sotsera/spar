// Case: C01 | values and error cases returned by union conversion
// Kind: clean
// Why: the ordinary way to produce a Result.
namespace Corpus.Clean;

public static class C01
{
    public static PlanResult Value() => new CopyPlan(Guid.Empty, []);

    public static PlanResult Error() => new PlanError.ConnectionNotFound("main");

    public static PlanResult Variable(bool ok)
    {
        PlanResult result = ok ? new CopyPlan(Guid.Empty, []) : new PlanError.TableMissing("src", "t");
        return result;
    }
}

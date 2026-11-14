// Valid results for the cases to use. Must stay clean under every rule.
namespace Corpus;

public static class Values
{
    public static CopyPlan Plan { get; } = new(Guid.Empty, []);

    public static PlanResult Ok => Plan;

    public static PlanResult Failed => new PlanError.TableMissing("src", "dbo.Gone");

    public static CancelResult Cancelled => Result.Success;

    public static PlanResult Compute(int index) => index % 2 == 0 ? Plan : new PlanError.ConnectionNotFound($"c{index}");
}

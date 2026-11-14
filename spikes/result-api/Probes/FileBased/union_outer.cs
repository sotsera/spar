#:property PublishAot=false
using System;

Console.WriteLine(Describe(Plan(true)));
Console.WriteLine(Describe(Plan(false)));
Result<CopyPlan, PlanError> d = default;
Console.WriteLine(d.Value is null ? "default Value is null" : "default has value");
Console.WriteLine(typeof(Result<CopyPlan, PlanError>).IsValueType);
foreach (var i in typeof(Result<CopyPlan, PlanError>).GetInterfaces()) Console.WriteLine(i);
foreach (var m in typeof(Result<CopyPlan, PlanError>).GetMembers()) Console.WriteLine(m);

static string Describe(Result<CopyPlan, PlanError> r) => r switch
{
    CopyPlan p => $"plan {p.Id}",
    PlanError.TableMissing t => $"missing {t.Table}",
    PlanError.ConnectionNotFound c => $"no conn {c.Id}",
};

static Result<CopyPlan, PlanError> Plan(bool fail)
{
    if (fail)
    {
        return new PlanError.TableMissing("t");
    }
    return new CopyPlan(1);
}

public sealed record CopyPlan(int Id);
public closed record PlanError
{
    public sealed record ConnectionNotFound(string Id) : PlanError;
    public sealed record TableMissing(string Table) : PlanError;
}
public union Result<T, TError>(T, TError);

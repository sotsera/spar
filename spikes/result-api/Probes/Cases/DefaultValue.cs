// Ways to produce a result that is neither a value nor an error. Which of them does the compiler report?
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class DefaultValue
{
    public static Result<CopyPlan, PlanError> DefaultLiteral() => default;

    public static async Task<Result<CopyPlan, PlanError>> DefaultInAsync()
    {
        await Task.Yield();
        return default;
    }

    public static Result<CancelError> NoValueDefault() => default;

    public static Result<CopyPlan, PlanError> NewWithoutArguments() => new();

    public static Result<CopyPlan, PlanError>[] Array() => new Result<CopyPlan, PlanError>[4];
}

public sealed class Holder
{
    private Result<CopyPlan, PlanError> _last;

    public Holder(int seed)
    {
        Seed = seed;
    }

    public int Seed { get; }

    public Result<CopyPlan, PlanError> Last => _last;

    public void Set(Result<CopyPlan, PlanError> value) => _last = value;
}

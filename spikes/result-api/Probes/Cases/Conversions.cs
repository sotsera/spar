// Expected to compile on every form: the conversions a command author relies on.
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class Conversions
{
    public static Result<CopyPlan, PlanError> Sync(bool fail)
    {
        if (fail)
        {
            return new PlanError.TableMissing("src", "dbo.Orders");
        }

        return new CopyPlan(Guid.Empty, []);
    }

    public static async Task<Result<CopyPlan, PlanError>> TaskAsync(bool fail, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        if (fail)
        {
            return new PlanError.ConnectionNotFound("src");
        }

        return new CopyPlan(Guid.Empty, []);
    }

    public static ValueTask<Result<CopyPlan, PlanError>> ValueTaskSync(bool fail)
    {
        Result<CopyPlan, PlanError> result = fail
            ? new PlanError.ConnectionNotFound("src")
            : new CopyPlan(Guid.Empty, []);
        return ValueTask.FromResult(result);
    }

    public static async ValueTask<Result<CopyReport, RunError>> ValueTaskAsync(RunCopy request)
    {
        await Task.Yield();
        return request.TruncateTarget
            ? new CopyReport(1, 10, TimeSpan.Zero)
            : new RunError.TargetNotEmpty("dbo.Orders", 10);
    }

    public static Result<CopyPlan, PlanError> SwitchArms(int code) => code switch
    {
        0 => new CopyPlan(Guid.Empty, []),
        1 => new PlanError.ConnectionNotFound("src"),
        _ => new PlanError.TableMissing("src", "t"),
    };

    public static Func<Task<Result<CopyPlan, PlanError>>> Lambda() =>
        async () =>
        {
            await Task.Yield();
            return new PlanError.ConnectionNotFound("src");
        };

    public static Result<string, PlanError> Scalar() => "plain string value";

    public static Result<long, PlanError> ValueType() => 42L;

    public static Result<TValue, TFailure> Generic<TValue, TFailure>(TValue value)
        where TFailure : class => value;

    public static async Task<Result<CancelError>> NoValueAsync(bool running)
    {
        await Task.Yield();
        if (!running)
        {
            return new CancelError.NotRunning("job-1");
        }

        return Result.Success;
    }

    public static Result<CancelError> NoValueForbidden() => new CancelError.Forbidden();
}

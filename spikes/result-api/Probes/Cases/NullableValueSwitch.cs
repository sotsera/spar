// Union forms: a switch over a result whose value type is nullable, without and with a null arm.
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class NullableValueSwitch
{
    public static string WithoutNull(Result<CopyPlan?, PlanError> result) => result switch
    {
        CopyPlan plan => $"{plan.Steps.Count} steps",
        PlanError e => e.ToString(),
    };

    // Unconstrained TValue may be nullable, so this needs a null arm too (UnionSwitch.Generic adds notnull instead).
    public static string GenericWithoutNull<TValue, TFailure>(Result<TValue, TFailure> result)
        where TFailure : class => result switch
        {
            TValue value => $"value {value}",
            TFailure error => $"error {error}",
        };

    public static string WithNull(Result<CopyPlan?, PlanError> result) => result switch
    {
        CopyPlan plan => $"{plan.Steps.Count} steps",
        null => "no plan",
        PlanError e => e.ToString(),
    };
}

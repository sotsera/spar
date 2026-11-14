// A ShouldBeError<TCase>() test helper. A classic extension method cannot infer T and TError once TCase is given,
// so callers must name all three (the second call below fails). Does a C# 14 extension block, which declares T and
// TError on the receiver, let the caller give only TCase? (Observed: no; it also needs all three.)
using Spar;
using SqlTool.CopyJobs;

namespace Probes;

public static class ClassicHelpers
{
    public static TCase ShouldBeErrorClassic<T, TError, TCase>(this Result<T, TError> result)
        where TError : class
        where TCase : TError => result.TryGetError(out var error) && error is TCase typed
            ? typed
            : throw new InvalidOperationException($"Expected {typeof(TCase).Name}, got {result}.");
}

public static class BlockHelpers
{
    extension<T, TError>(Result<T, TError> result)
        where TError : class
    {
        public TCase ShouldBeError<TCase>()
            where TCase : TError => result.TryGetError(out var error) && error is TCase typed
                ? typed
                : throw new InvalidOperationException($"Expected {typeof(TCase).Name}, got {result}.");
    }
}

public static class HelperUse
{
    public static string Use(Result<CopyPlan, PlanError> result)
    {
        var classic = result.ShouldBeErrorClassic<CopyPlan, PlanError, PlanError.TableMissing>();
        var inferred = result.ShouldBeErrorClassic<PlanError.TableMissing>();
        var block = result.ShouldBeError<PlanError.TableMissing>();
        var blockAll = result.ShouldBeError<CopyPlan, PlanError, PlanError.TableMissing>();
        return classic.Table + inferred.Table + block.Table + blockAll.Table;
    }
}

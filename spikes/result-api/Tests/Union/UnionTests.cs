// Union forms only: one exhaustive switch over the result, and how default and a null value behave in it.
using Spar;
using SqlTool.CopyJobs;

namespace Tests;

public sealed class UnionTests
{
    private static readonly CopyJobsService s_service = new(
        new Dictionary<string, IReadOnlyDictionary<string, long>>
        {
            ["src"] = new Dictionary<string, long> { ["dbo.Orders"] = 120 },
        });

    [Fact]
    public async Task SwitchOverTheResult()
    {
        var recipe = new CopyRecipe("src", "dst", ["dbo.Orders", "dbo.Gone"]);

        var text = await s_service.PlanAsync(recipe, TestContext.Current.CancellationToken) switch
        {
            CopyPlan plan => $"{plan.Steps.Count} steps",
            PlanError.ConnectionNotFound e => $"no connection {e.ConnectionId}",
            PlanError.TableMissing e => $"{e.Table} is missing",
        };

        Assert.Equal("dbo.Gone is missing", text);
    }

    [Fact]
    public async Task PatternWithPayload()
    {
        var result = await s_service.PlanAsync(new CopyRecipe("x", "dst", []), TestContext.Current.CancellationToken);

        Assert.True(result is PlanError.ConnectionNotFound { ConnectionId: "x" });
    }

    [Fact]
    public async Task NoValueSwitch()
    {
        var result = await s_service.CancelAsync("job-1");

        var text = result switch
        {
            Success => "cancelled",
            CancelError.NotRunning e => $"{e.JobId} is not running",
            CancelError.Forbidden => "forbidden",
        };

        Assert.Equal("cancelled", text);
    }

    [Fact]
    public void NullValueGoesToTheNullArm()
    {
        Result<CopyPlan?, PlanError> result = (CopyPlan?)null;

        var text = result switch
        {
            CopyPlan plan => $"{plan.Steps.Count} steps",
            null => "no plan",
            PlanError e => e.ToString(),
        };

        Assert.Equal("no plan", text);
    }

    [Fact]
    public void ValueTypeValueDoesNotBox()
    {
        Result<long, PlanError> result = 7L;
        var total = 0L;

        // Warm up, then count: the switch reads the value through TryGetValue(out long).
        total += Read(result);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            total += Read(result);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(7007, total);
        Assert.Equal(0, allocated);
    }

#if RESULT_STRUCT
    [Fact]
    public void SwitchOverDefaultThrowsTheResultMessage()
    {
        var none = default(Result<CopyPlan, PlanError>);

        var exception = Assert.Throws<InvalidOperationException>(() => none switch
        {
            CopyPlan plan => plan.ToString(),
            PlanError e => e.ToString(),
        });
        Assert.StartsWith("default(Result) is neither", exception.Message, StringComparison.Ordinal);
    }
#endif

    private static long Read(Result<long, PlanError> result) => result switch
    {
        long rows => rows,
        PlanError => 0,
    };
}

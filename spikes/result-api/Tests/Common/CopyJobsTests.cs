// How the fast test project calls commands in-process and asserts on results, with plain xunit.v3 asserts.
using Spar;
using SqlTool.CopyJobs;

namespace Tests;

public sealed class CopyJobsTests
{
    private static readonly CopyJobsService s_service = new(
        new Dictionary<string, IReadOnlyDictionary<string, long>>
        {
            ["src"] = new Dictionary<string, long> { ["dbo.Orders"] = 120, ["dbo.Lines"] = 900 },
        });

    [Fact]
    public async Task PlanReturnsOneStepPerTable()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Orders", "dbo.Lines"), TestContext.Current.CancellationToken);

        // Value throws when the result is an error, and its message names the error.
        var plan = result.Value;
        Assert.Equal(["dbo.Orders", "dbo.Lines"], plan.Steps.Select(s => s.Table));
    }

    [Fact]
    public async Task PlanReportsTheMissingTable()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Orders", "dbo.Gone"), TestContext.Current.CancellationToken);

        // IsType returns the case typed, so its payload can be asserted.
        var missing = Assert.IsType<PlanError.TableMissing>(result.Error);
        Assert.Equal("dbo.Gone", missing.Table);
    }

    [Fact]
    public async Task PlanReportsTheMissingTableByValue()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Gone"), TestContext.Current.CancellationToken);

        // Error cases are records, so a whole case compares by value.
        Assert.Equal(new PlanError.TableMissing("src", "dbo.Gone"), result.Error);
    }

    [Fact]
    public async Task UnknownConnectionIsAnError()
    {
        var recipe = Recipe("dbo.Orders") with { Source = "nowhere" };

        var result = await s_service.PlanAsync(recipe, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(new PlanError.ConnectionNotFound("nowhere"), result.Error);
    }

    [Fact]
    public async Task CancelWithoutValue()
    {
        var done = await s_service.CancelAsync("job-1");
        var notRunning = await s_service.CancelAsync("");

        Assert.True(done.IsSuccess);
        Assert.IsType<CancelError.NotRunning>(notRunning.Error);
    }

    [Fact]
    public async Task ValueOnAnErrorNamesTheError()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Gone"), TestContext.Current.CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("TableMissing { Connection = src, Table = dbo.Gone }", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorOnASuccessShowsTheValue()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Orders"), TestContext.Current.CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(() => result.Error);
        Assert.Contains("CopyPlan { PlanId = ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MatchAndTryGet()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Orders"), TestContext.Current.CancellationToken);

        var described = result.Match(plan => $"{plan.Steps.Count} steps", error => error.ToString());
        Assert.Equal("1 steps", described);
        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Steps);
        Assert.False(result.TryGetError(out _));
    }

    [Fact]
    public void NullValueIsASuccess()
    {
        Result<CopyPlan?, PlanError> result = (CopyPlan?)null;

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ToStringShowsTheContent()
    {
        Result<long, PlanError> rows = 5L;
        Result<long, PlanError> error = new PlanError.ConnectionNotFound("x");

        Assert.Equal("Success(5)", rows.ToString());
        Assert.Equal("Error(ConnectionNotFound { ConnectionId = x })", error.ToString());
    }

    [Fact]
    public void NullErrorIsRejected()
    {
        PlanError? missing = null;

        Assert.Throws<ArgumentNullException>(() => (Result<CopyPlan, PlanError>)missing!);
    }

#if RESULT_STRUCT
    [Fact]
    public void DefaultThrowsOnEveryRead()
    {
        var none = default(Result<CopyPlan, PlanError>);

        Assert.Throws<InvalidOperationException>(() => none.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => none.IsError);
        Assert.Throws<InvalidOperationException>(() => none.Value);
        Assert.Throws<InvalidOperationException>(() => none.Error);
        Assert.Throws<InvalidOperationException>(() => none.TryGetValue(out _));
        Assert.Throws<InvalidOperationException>(() => none.Match(_ => 1, _ => 2));
        Assert.Equal("default(Result): neither a value nor an error", none.ToString());
        Assert.Throws<InvalidOperationException>(() => default(Result<CancelError>).IsSuccess);
    }
#endif

    // Deliberately failing, to record what a failed assertion prints. Run with: --explicit only
    [Fact(Explicit = true)]
    public async Task FailingValueOnError()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Gone"), TestContext.Current.CancellationToken);
        Assert.Single(result.Value.Steps);
    }

    [Fact(Explicit = true)]
    public async Task FailingIsTypeOnSuccess()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Orders"), TestContext.Current.CancellationToken);
        Assert.IsType<PlanError.TableMissing>(result.Error);
    }

    [Fact(Explicit = true)]
    public async Task FailingIsTypeOnOtherCase()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Gone"), TestContext.Current.CancellationToken);
        Assert.IsType<PlanError.ConnectionNotFound>(result.Error);
    }

    [Fact(Explicit = true)]
    public async Task FailingEqualOnResult()
    {
        var result = await s_service.PlanAsync(Recipe("dbo.Gone"), TestContext.Current.CancellationToken);
        Assert.Equal(new PlanError.TableMissing("src", "dbo.Other"), result.Error);
    }

    private static CopyRecipe Recipe(params string[] tables) => new("src", "dst", tables);
}

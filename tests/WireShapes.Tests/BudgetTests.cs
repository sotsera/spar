using System.Diagnostics;
using Spar.Testing;

namespace WireShapes.Tests;

/// <summary>
/// The command-test budget (plan, "Provisional budgets"): an app started, called once and stopped, as a command test
/// does, twenty times in a row; the first start pays for warming up and is recorded on its own. Release builds give the
/// budget's numbers (<see cref="Budgets"/>).
/// </summary>
public sealed class BudgetTests
{
    private const int Starts = 20;

    [Fact]
    public async Task Command_test_app_start()
    {
        var starts = new List<double>(Starts);
        var cycles = new List<double>(Starts);
        for (var i = 0; i < Starts; i++)
        {
            var clock = Stopwatch.StartNew();
            var app = await SparTestApp.StartAsync<Program>(cancellationToken: TestContext.Current.CancellationToken);
            starts.Add(clock.Elapsed.TotalMilliseconds);

            var echoed = await new EchoClient(app).CheckAsync(new Item("x"), TestContext.Current.CancellationToken);
            await app.DisposeAsync();
            cycles.Add(clock.Elapsed.TotalMilliseconds);

            Assert.True(echoed is Item { Name: "x" }, $"The call returned {echoed}.");
        }

        Budgets.Record("Command test, first start + call + stop", Budgets.Summary(cycles[..1]));
        Budgets.Record("Command test, app start (budget 2 ms)", Budgets.Summary(starts[1..]));
        Budgets.Record("Command test, start + call + stop", Budgets.Summary(cycles[1..]));
    }
}

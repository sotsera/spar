using System.Collections.Concurrent;
using System.Diagnostics;
using Spar.Testing;
using WireShapes.Tests;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>Window tests run one at a time while the budget is measured, so other windows do not share its time.</summary>
[CollectionDefinition(nameof(BudgetCollection), DisableParallelization = true)]
public sealed class BudgetCollection;

/// <summary>
/// The window-test budget (plan, "Provisional budgets": open, journey and close, median, at most 700 ms on Windows),
/// the cost of the shared browser and of each test's controller, and the page API's cost per action. Recorded to
/// <c>artifacts/test-results/Spar.WindowTests/budgets.txt</c>, never asserted: times vary with the machine.
/// </summary>
[Trait("Surface", "Window")]
[Collection(nameof(BudgetCollection))]
public sealed class BudgetTests
{
    private const int Runs = 20;
    private const int Actions = 100;

    [Fact]
    public async Task A_window_test_opens_runs_a_journey_and_closes()
    {
        var open = new List<double>();
        var journey = new List<double>();
        var close = new List<double>();
        var whole = new List<double>();
        var controller = new List<double>();
        var environment = new List<double>();
        for (var run = 0; run < Runs; run++)
        {
            var phases = new ConcurrentDictionary<string, long>();
            var started = Stopwatch.GetTimestamp();
            var host = await HostPage.OpenAsync(options => options.WindowPhase = phase => phases.TryAdd(phase, Stopwatch.GetTimestamp()));
            var opened = Stopwatch.GetTimestamp();
            await JourneyAsync(host);
            var done = Stopwatch.GetTimestamp();
            await host.DisposeAsync();
            var closed = Stopwatch.GetTimestamp();

            open.Add(Milliseconds(started, opened));
            journey.Add(Milliseconds(opened, done));
            close.Add(Milliseconds(done, closed));
            whole.Add(Milliseconds(started, closed));
            controller.Add(Milliseconds(phases["controller requested"], phases["controller"]));
            environment.Add(Milliseconds(started, phases["environment"]));
        }

        Budgets.Record("Window test: open, journey and close", Budgets.Summary(whole) + ", p95 " + P95(whole));
        Budgets.Record("Window test: open (to the page's Ready)", Budgets.Summary(open));
        Budgets.Record("Window test: journey (fill, check, select, click, 3 assertions)", Budgets.Summary(journey));
        Budgets.Record("Window test: close (teardown and the renderer's exit)", Budgets.Summary(close));
        Budgets.Record("Window test: its InPrivate controller", Budgets.Summary(controller));
        Budgets.Record("Window test: the shared environment, first window of the process", Budgets.Summary(environment.Take(1).ToList()));
        Budgets.Record("Window test: the shared environment, later windows", Budgets.Summary(environment.Skip(1).ToList()));
    }

    [Fact]
    public async Task Each_action_of_the_page_api_costs()
    {
        await using var host = await HostPage.OpenAsync();
        var page = host.Page;
        var heading = page.GetByRole(SparRole.Heading, new() { Name = "Window host", Exact = true });
        var name = page.GetByLabel("Customer name");
        var echo = page.GetByRole(SparRole.Button, new() { Name = "Echo", Exact = true });

        await MeasureAsync("Window test action: GetByRole, count", () => heading.CountAsync());
        await MeasureAsync("Window test action: GetByLabel, count", () => name.CountAsync());
        await MeasureAsync("Window test action: an assertion that holds", () => Expect(heading).ToBeVisibleAsync());
        await MeasureAsync("Window test action: click (checks, trusted press, landed)", () => echo.ClickAsync());
        await MeasureAsync("Window test action: fill", () => name.FillAsync("Ada"));
        await MeasureAsync("Window test action: key press", () => page.Keyboard.PressAsync("End"));
        await MeasureAsync("Window test action: evaluate 1+1", () => page.EvaluateAsync("1+1"));
        await MeasureAsync("Window test action: viewport screenshot", () => page.ScreenshotAsync(), count: 20);
        host.AssertClean();
    }

    /// <summary>A short journey through the order form, the shape of a typical window test's steps.</summary>
    private static async Task JourneyAsync(HostPage host)
    {
        var order = host.Page.GetByRole(SparRole.Region, new() { Name = "Order" });
        await order.GetByLabel("Customer name").FillAsync("Ada");
        await order.GetByRole(SparRole.Checkbox, new() { Name = "Gift wrap" }).CheckAsync();
        await order.GetByLabel("Region").SelectOptionAsync("Americas");
        await order.GetByRole(SparRole.Button, new() { Name = "Place order" }).ClickAsync();
        await Expect(order.GetByText("ordered")).ToHaveTextAsync("Ada ordered 1 for Americas, gift wrapped, any day");
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
        await Expect(order.GetByLabel("Gift wrap")).ToBeCheckedAsync();
    }

    private static async Task MeasureAsync(string what, Func<Task> action, int count = Actions)
    {
        await action();
        var times = new List<double>(count);
        for (var index = 0; index < count; index++)
        {
            var started = Stopwatch.GetTimestamp();
            await action();
            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        Budgets.Record(what, Budgets.Summary(times) + ", p95 " + P95(times));
    }

    private static double Milliseconds(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    private static string P95(List<double> times)
    {
        var sorted = times.Order().ToArray();
        var index = Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1);
        return sorted[index].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms";
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spar.Testing;
using WireShapes.Tests;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>Window tests run one at a time while the budget is measured, so other windows do not share its time.</summary>
[CollectionDefinition(nameof(BudgetCollection), DisableParallelization = true)]
public sealed class BudgetCollection;

/// <summary>
/// The window-test budget (plan, "Provisional budgets": open, journey and close, median, at most 700 ms on Windows),
/// the cost of the shared browser and of each test's controller, the page API's cost per action, the desktop bridge's
/// round trip, and a Production executable's launch to ui-ready and working set (the other M1b budgets). Recorded to
/// <c>artifacts/test-results/Spar.WindowTests/budgets.txt</c>, never asserted: times vary with the machine.
/// </summary>
[Trait("Surface", "Window")]
[Collection(nameof(BudgetCollection))]
public sealed partial class BudgetTests
{
    private const int Runs = 20;
    private const int Actions = 100;
    private const int Launches = 10;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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

    [Fact]
    public async Task A_small_call_crosses_the_desktop_bridge_and_back()
    {
        await using var host = await HostPage.OpenAsync();

        // The page times 200 echoes after 20 warm-up calls (plan, "Provisional budgets": at most 1 ms, median).
        await host.ClickAsync("Time the echo");
        var times = await EchoTimesAsync(host.Page);

        Budgets.Record("Desktop bridge: small unary round trip, timed by the page", Budgets.Summary(times) + ", p95 " + P95(times));
        host.AssertClean();
    }

    [Fact]
    public async Task The_Production_app_starts_to_ui_ready_and_holds_its_working_set()
    {
        WindowSupport.SkipUnlessSupported();
        var ready = new List<double>();
        var app = new List<double>();
        var withBrowser = new List<double>();

        // The Production NativeAOT executable as a user starts it again: one warm-up launch creates the data root and the
        // browser profile, and every measured launch reuses them (a new root would measure a first start each time).
        string? root = null;
        try
        {
            for (var run = 0; run <= Launches; run++)
            {
                await using var launched = await SparBlackBox.LaunchAsync(Projects.WindowHost, o =>
                {
                    o.DataRoot = root;
                    o.KeepData = true;
                }, Token);
                root ??= launched.DataRoot;
                await Task.Delay(TimeSpan.FromSeconds(2), Token);
                var processes = launched.ProcessIds.Select(TryGetProcess).OfType<Process>().ToList();
                var appMegabytes = Megabytes(processes.Where(p => p.Id == launched.ProcessId).Sum(p => p.WorkingSet64));
                var allMegabytes = Megabytes(processes.Sum(p => p.WorkingSet64));
                processes.ForEach(p => p.Dispose());
                var log = await ReadSharedAsync(launched.LogFile);
                await launched.CloseAsync(Token);
                if (run == 0)
                {
                    continue;
                }

                // The host's phases count from its own start, which its first line places after the process's; the log
                // grows with each launch on the root, so the last lines are this launch's.
                ready.Add(Number(HostStarted(), log) + Number(UiReady(), log));
                app.Add(appMegabytes);
                withBrowser.Add(allMegabytes);
            }
        }
        finally
        {
            if (root is not null)
            {
                TestDataRoots.Delete(root);
            }
        }

        Budgets.Record("Launch to ui-ready, warm (profile reused), from the process's start (WindowHost, Production NativeAOT)", Budgets.Summary(ready));
        Budgets.Record("App process working set 2 s after ui-ready (WindowHost, Production NativeAOT)", InMegabytes(app));
        Budgets.Record("Working set with the browser's processes, 2 s after ui-ready (same launches)", InMegabytes(withBrowser));
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

    private static async Task<List<double>> EchoTimesAsync(SparPage page)
    {
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var text = await page.EvaluateAsync<string>("document.getElementById('echo-times').textContent");
            if (text.Length > 0)
            {
                using var json = JsonDocument.Parse(text);
                return [.. json.RootElement.EnumerateArray().Select(time => time.GetDouble())];
            }

            Assert.True(DateTime.UtcNow < limit, "The page did not finish timing the echo within 30 s.");
            await Task.Delay(50, Token);
        }
    }

    private static Process? TryGetProcess(int id)
    {
        try
        {
            return Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
            // Gone since the job listed it.
            return null;
        }
    }

    private static double Megabytes(long bytes) => bytes / 1048576.0;

    private static string InMegabytes(List<double> megabytes)
    {
        var sorted = megabytes.Order().ToArray();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return string.Create(CultureInfo.InvariantCulture, $"{median:0.0} MB ({sorted[0]:0.0}–{sorted[^1]:0.0}, n = {sorted.Length})");
    }

    private static double Number(Regex pattern, string log)
    {
        var matches = pattern.Matches(log);
        Assert.True(matches.Count > 0, $"The app's log has no line matching {pattern}.");
        return double.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(Token);
    }

    [GeneratedRegex(@"Host started ([0-9.]+) ms after the process")]
    private static partial Regex HostStarted();

    [GeneratedRegex(@"\] ui-ready at ([0-9.]+) ms")]
    private static partial Regex UiReady();

    private static string P95(List<double> times)
    {
        var sorted = times.Order().ToArray();
        var index = Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1);
        return sorted[index].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms";
    }
}

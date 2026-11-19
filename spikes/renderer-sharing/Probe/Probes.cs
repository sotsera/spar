using System.Diagnostics;
using System.Text;

namespace RendererSharing;

/// <summary>
/// The three questions of the spike (../RESULTS.md): do test windows share a renderer; does ending one window's renderer
/// stop another window's page; and does one window's held <c>WM_ENDSESSION</c> lengthen another's on the shared UI
/// thread.
/// </summary>
public sealed class Probes
{
    private const uint EndSession = 0x0016;
    private static readonly nint Logoff = unchecked((nint)0x80000000);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>N windows open at once: each window's renderers by its main frame, and every renderer of the root by command line.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task Listing(int count)
    {
        var windows = await OpenAsync(count);
        try
        {
            var report = new StringBuilder();
            report.AppendLine($"listing, {count} windows open at once (test process {Environment.ProcessId})");
            var all = new List<int>();
            for (var index = 0; index < windows.Count; index++)
            {
                var renderers = await windows[index].App.Window.RendererProcessesAsync();
                all.AddRange(renderers);
                report.AppendLine($"  window {index}: renderers by main frame [{string.Join(", ", renderers)}]");
            }

            var processes = await Os.BrowserProcessesAsync();
            foreach (var group in processes.GroupBy(process => process.Type).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                report.AppendLine($"  os {group.Key}: {group.Count()} [{string.Join(", ", group.Select(p => p.RendererClientId is null ? p.Id.ToString() : $"{p.Id}(client {p.RendererClientId})"))}]");
            }

            var extras = processes.Select(p => p.Extra).Where(e => e.Length > 0).Distinct().ToList();
            report.AppendLine($"  os argument flags seen: {(extras.Count == 0 ? "none" : string.Join(" | ", extras))}");
            var shared = all.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
            report.AppendLine($"  distinct renderer pids across windows: {all.Distinct().Count()} for {count} windows; shared pids: [{string.Join(", ", shared)}]");
            var notListed = processes.Where(p => p.Type == "renderer" && !all.Contains(p.Id)).Select(p => p.Id).ToList();
            report.AppendLine($"  renderers of the root no window lists (spare or the anchor's): [{string.Join(", ", notListed)}]");
            Evidence.Append("listing.txt", report.ToString());
        }
        finally
        {
            await DisposeAllAsync(windows);
        }
    }

    /// <summary>N windows; window 0's renderers (by its main frame) are ended; does every other page still answer?</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task KillOne(int count)
    {
        var windows = await OpenAsync(count);
        try
        {
            var report = new StringBuilder();
            report.AppendLine($"kill one, {count} windows");
            var before = new List<IReadOnlyList<int>>();
            foreach (var window in windows)
            {
                before.Add(await window.App.Window.RendererProcessesAsync());
            }

            var victim = before[0];
            report.AppendLine($"  window 0 renderers: [{string.Join(", ", victim)}]; others: {string.Join(" ", before.Skip(1).Select(r => "[" + string.Join(", ", r) + "]"))}");
            foreach (var id in victim)
            {
                using var process = Process.GetProcessById(id);
                process.Kill();
                await process.WaitForExitAsync(Token);
            }

            await Task.Delay(500, Token);
            for (var index = 0; index < windows.Count; index++)
            {
                var (answered, ms, detail) = await windows[index].PingAsync(TimeSpan.FromSeconds(3));
                var log = windows[index].ReadLog();
                var exited = log.Contains("renderer exited", StringComparison.Ordinal);
                var after = await windows[index].App.Window.RendererProcessesAsync();
                report.AppendLine($"  window {index}: page answered={answered} in {ms:0.0} ms ({detail}); 'renderer exited' in its log={exited}; renderers now [{string.Join(", ", after)}]");
            }

            Evidence.Append("kill-one.txt", report.ToString());
        }
        finally
        {
            await DisposeAllAsync(windows);
        }
    }

    /// <summary>
    /// Two windows' session ends overlapping on the one UI thread, as the renderer test and the fast session-end test
    /// overlapped in the full suite: A has the page guard "slow" (and, with <paramref name="kill"/>, its renderer ended
    /// 300 ms in), B has no guard. <paramref name="order"/> says whose <c>WM_ENDSESSION</c> is sent first.
    /// </summary>
    [Theory]
    [InlineData("fast-first", true, 1)]
    [InlineData("fast-first", true, 2)]
    [InlineData("fast-first", true, 3)]
    [InlineData("fast-first", false, 1)]
    [InlineData("fast-first", false, 2)]
    [InlineData("fast-first", false, 3)]
    [InlineData("slow-first", true, 1)]
    [InlineData("slow-first", true, 2)]
    [InlineData("slow-first", true, 3)]
    [InlineData("fast-first-0", true, 1)]
    [InlineData("fast-first-0", true, 2)]
    [InlineData("fast-first-0", true, 3)]
    [InlineData("fast-first-0", false, 1)]
    [InlineData("fast-first-0", false, 2)]
    [InlineData("fast-first-0", false, 3)]
    [InlineData("fast-alone", false, 1)]
    [InlineData("fast-alone", false, 2)]
    [InlineData("fast-alone", false, 3)]
    public async Task Overlap(string order, bool kill, int repetition)
    {
        var windows = await OpenAsync(2);
        var slow = windows[0];
        var fast = windows[1];
        try
        {
            await slow.GuardAsync("slow");
            var renderers = await slow.App.Window.RendererProcessesAsync();
            var fastRenderers = await fast.App.Window.RendererProcessesAsync();

            Task<double> slowEnd = Task.FromResult(0.0);
            Task<double> fastEnd;
            if (order == "fast-alone")
            {
                fastEnd = TimedAsync(fast);
            }
            else if (order.StartsWith("fast-first", StringComparison.Ordinal))
            {
                // "fast-first" sends the slow window's message 30 ms later, as the suite's timing did; "fast-first-0"
                // posts it right after, so it always reaches the UI thread while the fast window's hold pumps.
                fastEnd = TimedAsync(fast);
                if (order == "fast-first")
                {
                    await Task.Delay(30, Token);
                }

                slowEnd = TimedAsync(slow);
            }
            else
            {
                slowEnd = TimedAsync(slow);
                await Task.Delay(30, Token);
                fastEnd = TimedAsync(fast);
            }

            if (kill)
            {
                await Task.Delay(300, Token);
                foreach (var id in renderers)
                {
                    using var process = Process.GetProcessById(id);
                    process.Kill();
                }
            }

            var fastMs = await fastEnd;
            var slowMs = await slowEnd;
            var fastHeld = fast.Phases.FirstOrDefault(p => p.StartsWith("session end held", StringComparison.Ordinal));
            var slowHeld = slow.Phases.FirstOrDefault(p => p.StartsWith("session end held", StringComparison.Ordinal));
            if (repetition == 1 && kill)
            {
                Evidence.Append("overlap-logs.txt", $"{order} kill={kill}: the slow window's app log{Environment.NewLine}{slow.ReadLog()}");
            }

            Evidence.Append("overlap.txt",
                $"{order,-10} kill={kill,-5} #{repetition}: fast WM_ENDSESSION returned after {fastMs:0} ms ({fastHeld}); slow after {slowMs:0} ms ({slowHeld}); slow renderers [{string.Join(", ", renderers)}], fast renderers [{string.Join(", ", fastRenderers)}]");
        }
        finally
        {
            await DisposeAllAsync(windows);
        }
    }

    private static async Task<double> TimedAsync(ProbeWindow window)
    {
        var started = Stopwatch.GetTimestamp();
        await window.App.Window.SendMessageAsync(EndSession, 1, Logoff);
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static async Task<List<ProbeWindow>> OpenAsync(int count)
    {
        var opening = Enumerable.Range(0, count).Select(_ => ProbeWindow.OpenAsync()).ToList();
        return [.. await Task.WhenAll(opening)];
    }

    private static async Task DisposeAllAsync(List<ProbeWindow> windows)
    {
        foreach (var window in windows)
        {
            try
            {
                await window.DisposeAsync();
            }
            catch (Exception exception)
            {
                Evidence.Append("dispose-failures.txt", $"{TestContext.Current.Test?.TestDisplayName}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}

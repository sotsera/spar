extern alias StartupProbeApp;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spar.Testing;
using StartupProbeNotes = StartupProbeApp::StartupProbe.Notes;

namespace Spar.WindowTests;

/// <summary>
/// The splash frame by frame (plan section 3d; M1d step 5's evidence, <c>docs/evidence/m1d-page-startup.md</c>): the
/// start-up probe's splash page opened in its test window after a page of the theme colour alone, and the window's
/// pixels read as it starts (on Windows with <c>PrintWindow</c> from the offscreen window, on Linux from the run's
/// private Xvfb), in the middle, where the mark sits, and near a corner. Each start is classified as the cover, the mark
/// or the app (the probe's blue page) frame by frame: how often the mark appeared, how long it was on screen, and any
/// frame of a colour that is none of them. A measurement, so an explicit test: run it with
/// <c>--filter-class Spar.WindowTests.StartupFrameTests</c> and its results are in <c>startup-frames.txt</c>.
/// </summary>
[Trait("Surface", "Window")]
[Collection(nameof(StartupCollection))]
public sealed class StartupFrameTests
{
    private const uint Cover = 0xEEF3F8;
    private const uint Mark = 0xE8A33D;
    private const uint App = 0x2563EB;

    /// <summary>The starts measured, by name, and the address each gives the splash page.</summary>
    public static TheoryData<string, string> Starts => new()
    {
        { "fast", "?ready=0" },
        { "bundle +300 ms", "?late=300&ready=0" },
        { "bundle +1200 ms", "?late=1200&ready=0" },
        { "data after 1500 ms", "?ready=1500" },
        { "bundle +600 ms, 600 ms of work in the connecting task", "?late=600&work=600&at=same&ready=0" },
        { "bundle +600 ms, 600 ms of work in a later task", "?late=600&work=600&at=later&ready=0" },
        { "bundle +600 ms, 600 ms of work before the SDK connects", "?late=600&work=600&at=before&ready=0" },
        { "bundle +200 ms, 600 ms of work in the connecting task", "?late=200&work=600&at=same&ready=0" },
        { "bundle +200 ms, 600 ms of work in a later task", "?late=200&work=600&at=later&ready=0" },
        { "bundle +200 ms, 600 ms of work before the SDK connects", "?late=200&work=600&at=before&ready=0" },
        { "600 ms of work in the connecting task", "?work=600&at=same&ready=0" },
        { "600 ms of work in a later task", "?work=600&at=later&ready=0" },
        { "600 ms of work before the SDK connects", "?work=600&at=before&ready=0" },
        { "1500 ms of work in the connecting task", "?work=1500&at=same&ready=0" },
    };

    [Theory(Explicit = true)]
    [MemberData(nameof(Starts))]
    public async Task The_splash_frame_by_frame(string start, string query)
    {
        var runs = int.TryParse(Environment.GetEnvironmentVariable("SPAR_STARTUP_FRAME_RUNS"), CultureInfo.InvariantCulture, out var count) ? count : 5;
        await using var app = await StartupTests.OpenAsync(SparColorScheme.Light);
        var results = new List<StartFrames>();
        for (var run = 1; run <= runs; run++)
        {
            await app.Page.GotoAsync("/blank.html");
            await Task.Delay(500, TestContext.Current.CancellationToken);

            var frames = await SampleAsync(() => StartupTests.StartSplashAsync(app, query));
            results.Add(frames);
            Results.Append("startup-frames.txt", $"{Host} | {start} | run {run} | {frames}");
        }

        Results.Append("startup-frames.txt", $"{Host} | {start} | summary | {Summary(results)}");

        // What every start must keep: never a frame of another colour, never the mark twice; a fast one never shows it.
        Assert.All(results, frames => Assert.Equal(0, frames.Wrong));
        Assert.All(results, frames => Assert.InRange(frames.Appearances, 0, 1));
        if (start == "fast")
        {
            Assert.All(results, frames => Assert.Equal(0, frames.Appearances));
        }
    }

    /// <summary>The starts of a window's first page, by their index in the probe's <c>firstPageStarts</c> (start.js).</summary>
    public static TheoryData<int, string> FirstPageStarts => new()
    {
        { 0, "fast" },
        { 1, "bundle +300 ms" },
        { 2, "bundle +1200 ms" },
        { 3, "data after 1500 ms" },
        { 4, "bundle +600 ms, 600 ms of work in the connecting task" },
        { 5, "bundle +600 ms, 600 ms of work in a later task" },
        { 6, "bundle +600 ms, 600 ms of work before the SDK connects" },
        { 7, "1500 ms of work in the connecting task" },
    };

    /// <summary>
    /// The same starts as a fresh window's first document, which the engine may treat differently from a later one
    /// (WebView2 held a content-less first page's first frame for about 550 ms in <c>spikes/theme-startup</c>). The
    /// window's width carries the start; the times count from just before the window was asked for.
    /// </summary>
    [Theory(Explicit = true)]
    [MemberData(nameof(FirstPageStarts))]
    public async Task The_first_pages_splash_frame_by_frame(int index, string start)
    {
        var runs = int.TryParse(Environment.GetEnvironmentVariable("SPAR_STARTUP_FRAME_RUNS"), CultureInfo.InvariantCulture, out var count) ? count : 5;
        var results = new List<StartFrames>();
        for (var run = 1; run <= runs; run++)
        {
            Environment.SetEnvironmentVariable("STARTUP_PROBE_START", index.ToString(CultureInfo.InvariantCulture));
            try
            {
                // The last run's window must be gone first: it has the same title, and its page is the app's blue.
                await Waits.UntilAsync(() => !WindowShown(), TimeSpan.FromSeconds(10), "the last run's window to close");
                SparTestApp? app = null;
                StartFrames frames;
                try
                {
                    frames = await SampleAsync(async () =>
                    {
                        app = await StartupTests.OpenAsync(SparColorScheme.Light);
                        var notes = app.Services.GetRequiredService<StartupProbeNotes>();
                        return JsonDocument.Parse(notes.All[0]).RootElement.Clone();
                    });
                }
                finally
                {
                    // Closed once the sampling stopped, so no frame of a closing window is counted.
                    if (app is not null)
                    {
                        await app.DisposeAsync();
                    }
                }

                results.Add(frames);
                Results.Append("startup-frames.txt", $"{Host} | first page | {start} | run {run} | {frames}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("STARTUP_PROBE_START", null);
            }
        }

        Results.Append("startup-frames.txt", $"{Host} | first page | {start} | summary | {Summary(results)}");
        Assert.All(results, frames => Assert.Equal(0, frames.Wrong));
        Assert.All(results, frames => Assert.InRange(frames.Appearances, 0, 1));
    }

    private static string Host => OperatingSystem.IsWindows() ? "WebView2" : "WebKitGTK";

    /// <summary>Whether a window of the probe is shown in this process.</summary>
    private static bool WindowShown() =>
        OperatingSystem.IsWindows() ? WindowFrames.Find("StartupProbe") != 0
        : OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DISPLAY") is { } display && X11Frames.Shows(display, Environment.ProcessId);

    /// <summary>Samples the window from just before the navigation until the page noted its splash gone, and a little after.</summary>
    private static async Task<StartFrames> SampleAsync(Func<Task<JsonElement>> start)
    {
        using var stop = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var sampler = Task.Factory.StartNew(() => Sample(clock, stop.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var marks = await start();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        return StartFrames.Of(await sampler, marks);
    }

    private static List<(double Milliseconds, uint Middle, uint Corner)> Sample(Stopwatch clock, CancellationToken stop)
    {
        if (OperatingSystem.IsWindows())
        {
            // A first page's window may not exist yet: it is waited for, and sampled from the moment it is shown.
            var window = WindowFrames.Find("StartupProbe");
            while (window == 0 && !stop.IsCancellationRequested)
            {
                Thread.Sleep(1);
                window = WindowFrames.Find("StartupProbe");
            }

            return window == 0 ? [] : WindowFrames.SampleMiddleAndCorner(window, clock, stop);
        }

        if (OperatingSystem.IsLinux())
        {
            // The test process's private display is set when its first window opens, which may be this start's.
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            while (string.IsNullOrEmpty(display) && !stop.IsCancellationRequested)
            {
                Thread.Sleep(1);
                display = Environment.GetEnvironmentVariable("DISPLAY");
            }

            return string.IsNullOrEmpty(display) ? [] : X11Frames.SampleMiddleAndCorner(display, Environment.ProcessId, clock, stop);
        }

        throw new PlatformNotSupportedException();
    }

    private static string Summary(List<StartFrames> results)
    {
        var shown = results.Where(r => r.Appearances > 0).ToList();
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{results.Count} runs, the mark shown in {shown.Count}");
        if (shown.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $", on screen {shown.Min(r => r.MarkMs):0}–{shown.Max(r => r.MarkMs):0} ms");
            text.Append(CultureInfo.InvariantCulture, $", from {shown.Min(r => r.MarkFrom):0}–{shown.Max(r => r.MarkFrom):0} ms after the navigation");
        }

        text.Append(CultureInfo.InvariantCulture, $", the app from {results.Min(r => r.AppFrom):0}–{results.Max(r => r.AppFrom):0} ms");
        text.Append(CultureInfo.InvariantCulture, $", wrong samples {results.Sum(r => r.Wrong)}, at most {results.Max(r => r.Appearances)} appearance(s), unpainted first samples {results.Sum(r => r.Unpainted)}");
        return text.ToString();
    }

    /// <summary>One start's frames: the mark's appearances and time on screen, the app's first frame, and wrong colours.</summary>
    private sealed record StartFrames(int Samples, double Interval, int Appearances, double MarkFrom, double MarkMs, double AppFrom, int Wrong, string Wrongs, int Unpainted, string Marks)
    {
        public static StartFrames Of(List<(double Milliseconds, uint Middle, uint Corner)> samples, JsonElement marks)
        {
            var appearances = 0;
            var markFrom = double.NaN;
            var markTo = double.NaN;
            var appFrom = double.NaN;
            var wrong = 0;
            var wrongs = new SortedSet<string>(StringComparer.Ordinal);
            var inMark = false;
            var unpainted = 0;
            var painted = false;
            foreach (var (time, middle, corner) in samples)
            {
                // A new window read before its first paint is black all over (PrintWindow of a window DWM has not
                // composed yet; the M1b on-screen captures never saw it): those leading samples are counted apart.
                if (!painted && middle == 0 && corner == 0)
                {
                    unpainted++;
                    continue;
                }

                painted = true;
                var kind = Nearest(middle);
                if (kind == Mark)
                {
                    if (!inMark)
                    {
                        appearances++;
                        markFrom = double.IsNaN(markFrom) ? time : markFrom;
                    }

                    markTo = time;
                }

                inMark = kind == Mark;
                if (kind == App && double.IsNaN(appFrom))
                {
                    appFrom = time;
                }

                // The corner shows the cover or the app, and the cover's fade-out blends the two: anything far from
                // that line is a frame of another colour (white, #121212, black).
                if (!OnLine(corner, Cover, App))
                {
                    wrong++;
                    wrongs.Add($"#{corner:x6}");
                }
            }

            var interval = samples.Count > 1 ? (samples[^1].Milliseconds - samples[0].Milliseconds) / (samples.Count - 1) : 0;
            var markMs = appearances == 0 ? 0 : markTo - markFrom + interval;
            return new StartFrames(samples.Count, interval, appearances, markFrom, markMs, appFrom, wrong, string.Join(" ", wrongs), unpainted, marks.ToString());
        }

        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"samples {Samples} every {Interval:0.0} ms | mark {Appearances}x, from {MarkFrom:0} ms, on screen {MarkMs:0} ms | app from {AppFrom:0} ms | wrong {Wrong} {Wrongs} | unpainted first {Unpainted} | page {Marks}");

        private static uint Nearest(uint colour)
        {
            var cover = Distance(colour, Cover);
            var mark = Distance(colour, Mark);
            var app = Distance(colour, App);
            return mark < cover && mark < app ? Mark : app < cover ? App : Cover;
        }

        /// <summary>Whether <paramref name="colour"/> is within a small distance of the segment between two colours.</summary>
        private static bool OnLine(uint colour, uint from, uint to)
        {
            var best = double.MaxValue;
            for (var step = 0; step <= 32; step++)
            {
                best = Math.Min(best, Distance(colour, Blend(from, to, step / 32.0)));
            }

            return best < 24;
        }

        private static uint Blend(uint from, uint to, double share)
        {
            uint Channel(int shift) => (uint)Math.Round((((from >> shift) & 0xFF) * (1 - share)) + (((to >> shift) & 0xFF) * share));
            return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
        }

        private static double Distance(uint a, uint b)
        {
            double Delta(int shift) => (int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF);
            return Math.Sqrt((Delta(16) * Delta(16)) + (Delta(8) * Delta(8)) + (Delta(0) * Delta(0)));
        }
    }
}

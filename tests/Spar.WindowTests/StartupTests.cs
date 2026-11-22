extern alias StartupProbeApp;

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spar.Testing;
using Notes = StartupProbeApp::StartupProbe.Notes;
using Probe = StartupProbeApp::Program;

namespace Spar.WindowTests;

/// <summary>
/// The page side of start-up (plan section 3d), in the real window of the start-up probe (<c>tests/apps/StartupProbe</c>):
/// the theme style in the window's colours, allowed by its hash, in the scheme the window has; the splash removed by
/// <c>ready()</c> before the host hears it, at once on a fast start and after its minimum time on a slow one; and the
/// SDK's <c>setColorScheme</c> taking effect at once and saved for the next start. One at a time: on Linux the page's
/// <c>prefers-color-scheme</c> follows GTK's dark preference of the whole process, which <c>setColorScheme</c> changes.
/// The frame-by-frame measurements are in <see cref="StartupFrameTests"/>.
/// </summary>
[Trait("Surface", "Window")]
[Collection(nameof(StartupCollection))]
public sealed class StartupTests
{
    [Theory]
    [InlineData(SparColorScheme.Light, "rgb(238, 243, 248)")]
    [InlineData(SparColorScheme.Dark, "rgb(20, 26, 34)")]
    public async Task The_page_takes_the_window_colours_from_its_theme_style(SparColorScheme scheme, string background)
    {
        await using var app = await OpenAsync(scheme);

        var look = await LookAsync(app);

        Assert.True(look.GetProperty("themeApplied").GetBoolean(), "The theme style was blocked: its hash is not in style-src.");
        Assert.Equal(background, look.GetProperty("background").GetString());
        Assert.Equal("light dark", look.GetProperty("colorScheme").GetString());
        Assert.Equal("light dark", look.GetProperty("meta").GetString());
        Assert.Equal(scheme == SparColorScheme.Dark, look.GetProperty("dark").GetBoolean());
        Assert.Empty(app.Problems);
    }

    [Fact]
    public async Task A_fast_start_removes_the_splash_before_its_mark_appears_and_before_the_host_hears_ready()
    {
        await using var app = await OpenAsync(SparColorScheme.Light);

        var marks = await StartSplashAsync(app, "?ready=0");

        Assert.True(marks.GetProperty("removed").GetDouble() >= marks.GetProperty("ready").GetDouble());
        Assert.True(marks.GetProperty("removed").GetDouble() - marks.GetProperty("ready").GetDouble() < 200, $"The splash stayed: {marks}");
        Assert.Equal(JsonValueKind.Null, marks.GetProperty("busy").ValueKind);
        await AssertReadyAfterRemovalAsync(app, marks);
        Assert.Empty(app.Problems);
    }

    [Fact]
    public async Task A_slow_start_shows_the_mark_then_keeps_it_for_its_minimum_time()
    {
        await using var app = await OpenAsync(SparColorScheme.Light);

        var marks = await StartSplashAsync(app, "?ready=1200", async () =>
        {
            // Within the slow start, once the delay is over: the mark is fully shown, over the opaque cover.
            await Task.Delay(TimeSpan.FromMilliseconds(1000), TestContext.Current.CancellationToken);
            var shown = await app.Page.EvaluateAsync<string>(
                "getComputedStyle(document.querySelector('#spar-splash .mark')).opacity + ' ' + getComputedStyle(document.getElementById('spar-splash')).backgroundColor");
            Assert.Equal("1 rgb(238, 243, 248)", shown);
        });

        var appears = marks.GetProperty("entranceStart").GetDouble() + marks.GetProperty("entranceDelay").GetDouble();
        Assert.True(marks.GetProperty("ready").GetDouble() > appears, $"The mark had not appeared at ready: {marks}");
        Assert.True(marks.GetProperty("removed").GetDouble() - appears >= 400 - 1, $"The mark left before its minimum time: {marks}");
        Assert.Equal(JsonValueKind.Null, marks.GetProperty("busy").ValueKind);
        await AssertReadyAfterRemovalAsync(app, marks);
        Assert.Empty(app.Problems);
    }

    [Fact]
    public async Task SetColorScheme_applies_at_once_and_is_saved_for_the_next_start()
    {
        await using var app = await OpenAsync(SparColorScheme.Light);
        var saved = Path.Combine(app.Folders.DataRoot, "color-scheme");

        var dark = await ChooseAsync(app, "dark");
        var darkWindow = await app.Window.NativeThemeAsync();
        var savedDark = File.ReadAllText(saved);
        var light = await ChooseAsync(app, "light");
        var lightWindow = await app.Window.NativeThemeAsync();

        Assert.Equal(("saved", true, "rgb(20, 26, 34)"), Summary(dark));
        Assert.Equal((true, 0x221A14u), darkWindow);
        Assert.Equal("dark\n", savedDark);
        Assert.Equal(("saved", false, "rgb(238, 243, 248)"), Summary(light));
        Assert.Equal((false, 0xF8F3EEu), lightWindow);
        Assert.Equal("light\n", File.ReadAllText(saved));
        Assert.Contains(HostLines(app, "Colour scheme dark chosen by the page"), line => line.Contains("applied and saved", StringComparison.Ordinal));
        Assert.Empty(app.Problems);
    }

    [Fact]
    public async Task The_saved_choice_is_the_next_starts_scheme()
    {
        string root;
        await using (var first = await OpenAsync(SparColorScheme.Light, keepData: true))
        {
            Assert.Equal("saved", (await ChooseAsync(first, "dark")).GetProperty("outcome").GetString());
            root = first.Folders.DataRoot;
        }

        try
        {
            Assert.Equal("dark\n", File.ReadAllText(Path.Combine(root, "color-scheme")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task<SparTestApp> OpenAsync(SparColorScheme scheme, bool keepData = false)
    {
        await WindowSupport.SkipUnlessSupportedAsync();
        var app = await SparTestApp.StartAsync<Probe>(options =>
        {
            options.Surface = SparTestSurface.Window;
            options.ColorScheme = scheme;
            options.KeepData = keepData;
        }, TestContext.Current.CancellationToken);
        try
        {
            // The first page's note comes once its splash has gone, and its ready reaches the host two frames after
            // that, so a test that navigated at once would end that document first.
            var notes = app.Services.GetRequiredService<Notes>();
            await Waits.UntilAsync(() => notes.All.Count >= 1, TimeSpan.FromSeconds(20), "the first page's note");
            await Waits.UntilAsync(() => HostLines(app, "reported ui-ready").Count >= 1, TimeSpan.FromSeconds(10), "the first page's ui-ready");
            await SparAssertions.Expect(app.Page.GetByText("Ready")).ToBeVisibleAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    /// <summary>Opens the splash page with <paramref name="query"/> and returns what it noted once its splash had gone.</summary>
    internal static async Task<JsonElement> StartSplashAsync(SparTestApp app, string query, Func<Task>? meanwhile = null)
    {
        var notes = app.Services.GetRequiredService<Notes>();
        var before = notes.All.Count;
        await app.Page.GotoAsync("/splash.html" + query);
        if (meanwhile is not null)
        {
            await meanwhile();
        }

        await Waits.UntilAsync(() => notes.All.Count > before, TimeSpan.FromSeconds(20), "the splash page's note");
        var marks = JsonDocument.Parse(notes.All[before]).RootElement.Clone();
        Results.Append("startup.txt", $"{TestContext.Current.TestCase?.TestMethodName} {query}: {marks}");
        return marks;
    }

    private static async Task AssertReadyAfterRemovalAsync(SparTestApp app, JsonElement marks)
    {
        var removedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(marks.GetProperty("timeOrigin").GetDouble() + marks.GetProperty("removed").GetDouble()));
        List<string> lines = [];
        await Waits.UntilAsync(
            () => (lines = HostLines(app, "reported ui-ready")).Count >= 2,
            TimeSpan.FromSeconds(10),
            $"the splash page's ui-ready in the app's log ({string.Join(" | ", HostLines(app, "ui-ready"))})");

        // The log's UTC time leads each line; both clocks are this machine's, read to the millisecond.
        var ready = DateTimeOffset.Parse(lines[^1][..lines[^1].IndexOf(' ', StringComparison.Ordinal)], CultureInfo.InvariantCulture);
        var after = (ready - removedAt).TotalMilliseconds;
        Results.Append("startup.txt", $"  ui-ready {after.ToString("0", CultureInfo.InvariantCulture)} ms after the splash left");
        Assert.True(after >= -2, $"ui-ready came {-after:0} ms before the splash left.");
        Assert.False(await app.Page.EvaluateAsync<bool>("document.getElementById('spar-splash') !== null"));
    }

    /// <summary>The host's lines in the app's log file (in the test's own logs folder) that contain <paramref name="text"/>.</summary>
    private static List<string> HostLines(SparTestApp app, string text)
    {
        var path = Path.Combine(app.Folders.Logs, "app.log");
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return [.. reader.ReadToEnd().Split('\n').Where(line => line.Contains(text, StringComparison.Ordinal))];
    }

    private static async Task<JsonElement> LookAsync(SparTestApp app) =>
        await app.Page.EvaluateAsync<JsonElement>("window.probe.look()");

    private static async Task<JsonElement> ChooseAsync(SparTestApp app, string scheme) =>
        await app.Page.EvaluateAsync<JsonElement>($"window.probe.choose('{scheme}')");

    private static (string?, bool, string?) Summary(JsonElement chosen) =>
        (chosen.GetProperty("outcome").GetString(), chosen.GetProperty("dark").GetBoolean(), chosen.GetProperty("background").GetString());
}

/// <summary>The page start-up tests, one at a time and after the parallel ones (<see cref="StartupTests"/>).</summary>
[CollectionDefinition(nameof(StartupCollection), DisableParallelization = true)]
public sealed class StartupCollection;

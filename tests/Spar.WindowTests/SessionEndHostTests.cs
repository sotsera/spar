using System.Collections.Concurrent;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// Two host defects found by Windows Sandbox's real sign-out (step 8c): the session end's last lines never reached the
/// log file, because Windows may end the process as soon as <c>WM_ENDSESSION</c> returns and the file is written from a
/// queue; and the host recreated a browser lost during the session end. On WindowHost's Production executable (the
/// session end sent to its window, then the process ended at once, as Windows may) and in the test's window (a renderer
/// ended while the page's guard holds the session end).
/// </summary>
[Collection(SessionEndHostTests.Alone)]
public sealed class SessionEndHostTests
{
    /// <summary>
    /// Run with no other test: ending this window's renderer (one process, listed by its main frame) made another test's
    /// window's page stop answering (its session end held 3 s instead of 0.1, 5 of 6 full runs), so the renderers of
    /// windows on the test process's shared environment are not as separate as the listing suggests.
    /// </summary>
    public const string Alone = "Session end, alone";

    private const uint EndSession = 0x0016;
    private static readonly nint Logoff = unchecked((nint)0x80000000);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Surface", "BlackBox")]
    public async Task The_session_ends_last_lines_are_in_the_log_when_Windows_ends_the_process_right_after()
    {
        WindowSupport.SkipUnlessSupported();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);

        Assert.True(await app.Automation.EndSessionAsync(app.ProcessId, Token));
        app.Kill();
        Assert.True(await app.WaitForProcessTreeExitAsync());

        var log = Read(app.LogFile);
        Assert.Contains("session ending", log, StringComparison.Ordinal);
        Assert.Matches(@"session end: window closed[^\n]*; WM_ENDSESSION held \d+ ms", log);
        Results.Append("session-end.txt", "black-box session end then kill: the last line was in the log");
    }

    [Fact]
    [Trait("Surface", "Window")]
    public async Task A_renderer_lost_during_the_session_end_is_not_reloaded()
    {
        var phases = new ConcurrentQueue<string>();
        await using var host = await HostPage.OpenAsync(options => options.WindowPhase = phases.Enqueue);
        await host.GuardAsync("slow");
        var renderers = await host.App.Window.RendererProcessesAsync();
        Assert.NotEmpty(renderers);
        Results.Append("session-end.txt", $"renderer test: {renderers.Count} renderer(s) listed for its window: {string.Join(", ", renderers)}");

        // The page's guard holds the session end for 2 s; its renderer is ended inside that time.
        var ending = host.App.Window.SendMessageAsync(EndSession, 1, Logoff);
        await Task.Delay(300, Token);
        foreach (var id in renderers)
        {
            using var renderer = System.Diagnostics.Process.GetProcessById(id);
            renderer.Kill();
        }

        await ending;
        Assert.Equal(0, await host.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), Token));

        var file = Path.Combine(host.App.Folders.Logs, "app.log");
        await Waits.UntilAsync(() => File.Exists(file) && Read(file).Contains("during shutdown", StringComparison.Ordinal), TimeSpan.FromSeconds(5), "the lost renderer in the log");
        var log = Read(file);
        Assert.DoesNotContain("renderer reloaded", log, StringComparison.Ordinal);
        Assert.DoesNotContain("browser recreated", log, StringComparison.Ordinal);
        Assert.DoesNotContain(phases, phase => phase.Contains("renderer reloaded", StringComparison.Ordinal));
    }

    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>The collection whose tests run with no other test of this project at the same time.</summary>
[CollectionDefinition(SessionEndHostTests.Alone, DisableParallelization = true)]
public sealed class SessionEndAloneCollection;

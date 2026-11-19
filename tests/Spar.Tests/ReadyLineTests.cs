using System.Runtime.Versioning;
using Spar.Hosting;
using Spar.Logging;
using Spar.Testing;
using Spar.Windows;

namespace Spar.Tests;

/// <summary>
/// The black-box fixture's ready signal is a format Spar keeps stable (decision "Step 8b's shape: the black-box
/// fixture"): the line the Windows host writes to the app's log file when the page reports <c>ui-ready</c> is the line the
/// fixture waits for, and no other phase is.
/// </summary>
public sealed class ReadyLineTests : IDisposable
{
    private readonly string _root = Path.Combine(TestFolders.Artifacts(), "test-host", "ready-line", Guid.NewGuid().ToString("N")[..12]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows10.0.17763")]
    public void The_line_the_host_writes_for_ui_ready_is_the_fixture_s_ready_signal()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host writes this line; the Linux host arrives in M1c.");
        var log = new HostLog(standardError: false);
        log.UseFile(_root, LogLevels.BuildDefault(development: false));
        log.Phase("window shown");
        log.Phase(HostPhases.UiReady);
        log.Close();

        var lines = File.ReadAllLines(Path.Combine(_root, LogFile.FileName));

        var ready = Assert.Single(lines, ReadyLine.IsReady);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z INFO  Spar\.Host  \[[0-9a-f]{8}\] ui-ready at [0-9.]+ ms$", ready);
        Assert.Equal("ui-ready", ReadyLine.Phase);
    }

    [Theory]
    [InlineData("2026-11-19T07:41:07.318Z INFO  Spar.Host  [0f3a9c21] ui-ready at 412.5 ms", true)]
    [InlineData("2026-11-19T07:41:07.318Z INFO  Spar.Host  [0f3a9c21] ui-ready at 412 ms", true)]
    [InlineData("2026-11-19T07:41:07.318Z INFO  Spar.Host  [0f3a9c21] window shown at 12.1 ms", false)]
    [InlineData("2026-11-19T07:41:07.318Z INFO  Notebook  [0f3a9c21] ui-ready at 412.5 ms", false)]
    [InlineData("    2026-11-19T07:41:07.318Z INFO  Spar.Host  [0f3a9c21] ui-ready at 412.5 ms", false)]
    public void Only_the_host_s_own_ui_ready_line_counts(string line, bool ready) => Assert.Equal(ready, ReadyLine.IsReady(line));
}

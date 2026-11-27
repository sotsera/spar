using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// An installation that leaves nothing behind (decision "M3b's decide-first questions", item 6), on WindowHost's
/// Production executable copied to a folder of its own with an installation file beside it: browser data in memory,
/// the profile, cache and data folders removed after a close, what a kill left removed at the next start, and a file
/// with an unknown key refused with exit 70 and the key named.
/// </summary>
[Trait("Surface", "BlackBox")]
public sealed class InstallationTests : IDisposable
{
    private readonly string _installed = Path.Combine(TestDataRoots.Next("installation"), "app");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Profile(SparBlackBoxApp app) => Path.Combine(app.AppFolder, OperatingSystem.IsWindows() ? "WebView2" : "WebKit");

    [Fact]
    public async Task A_close_leaves_no_profile_cache_or_data_and_the_log_says_what_the_file_set()
    {
        WindowSupport.SkipUnlessBlackBox();
        var project = Install("""{ "folders": { "ephemeralBrowserData": true, "removeOnExit": ["profile", "cache", "data"] } }""");

        await using var app = await SparBlackBox.LaunchAsync(project, o => o.KeepData = true, Token);
        Assert.Equal(0, await app.CloseAsync(Token));
        await app.WaitForProcessTreeExitAsync();

        Assert.False(Directory.Exists(Profile(app)), "the profile stayed");
        Assert.False(Directory.Exists(Path.Combine(app.AppFolder, "cache")), "the cache stayed");
        Assert.False(Directory.Exists(app.DataFolder), "the data folder stayed");
        var log = await File.ReadAllTextAsync(app.LogFile, Token);
        Assert.Contains("sets folders.ephemeralBrowserData = true; folders.removeOnExit = profile, cache, data.", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_a_kill_left_is_removed_at_the_next_start()
    {
        WindowSupport.SkipUnlessBlackBox();
        var project = Install("""{ "folders": { "removeOnExit": ["data"] } }""");
        await using var first = await SparBlackBox.LaunchAsync(project, o => o.KeepData = true, Token);
        Directory.CreateDirectory(first.DataFolder);
        var leftover = Path.Combine(first.DataFolder, "leftover.txt");
        await File.WriteAllTextAsync(leftover, "a killed session's", Token);
        first.Kill();
        await first.WaitForExitAsync(Token);
        await first.WaitForProcessTreeExitAsync();
        Assert.True(File.Exists(leftover));

        await using var second = await SparBlackBox.LaunchAsync(project, o => o.DataRoot = first.DataRoot, Token);

        Assert.False(File.Exists(leftover), "the next start left what the kill left");
        Assert.Equal(0, await second.CloseAsync(Token));
    }

    [Fact]
    public async Task An_installation_file_with_an_unknown_key_stops_the_start_with_exit_70()
    {
        WindowSupport.SkipUnlessBlackBox();
        var project = Install("""{ "browser": { "devtools": true } }""");

        await using var app = await SparBlackBox.LaunchAsync(project, o => o.WaitForReady = false, Token);
        var dialog = await app.WaitForDialogAsync(TimeSpan.FromSeconds(15), Token);
        Assert.Contains("'browser' is not a key of the installation file", dialog.Text, StringComparison.Ordinal);
        await dialog.PressAsync("Close");

        Assert.Equal(70, await app.WaitForExitAsync(Token));
        Assert.Contains("WindowHost.spar.json cannot be used", app.StandardError, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installed))
        {
            Directory.Delete(Path.GetDirectoryName(_installed)!, recursive: true);
        }
    }

    /// <summary>WindowHost's executable copied to a folder of this test's own, with <paramref name="json"/> beside it.</summary>
    private SparProject Install(string json)
    {
        var source = Path.GetDirectoryName(Projects.WindowHost.ExecutablePath)!;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_installed, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target, File.GetUnixFileMode(file));
            }
        }

        var executable = Path.Combine(_installed, Path.GetFileName(Projects.WindowHost.ExecutablePath));
        File.WriteAllText(Path.Combine(_installed, Path.GetFileNameWithoutExtension(executable) + ".spar.json"), json);
        return SparProject.Built(executable, "WindowHost", Projects.WindowHost.AppId);
    }
}

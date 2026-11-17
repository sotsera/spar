using System.Runtime.Versioning;
using Spar.Hosting;
using Spar.Windows;

namespace Spar.Tests;

/// <summary>
/// The security defaults of plan section 3c in what they decide without a WebView: the app id's folders per channel,
/// the profile folder's checks, the folder in use, the opt-ins in the start-up log, the environment variables Spar
/// removes, the keys the browser keeps where devtools are on, and the context menu items kept.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class SecurityDefaultsTests
{
    [Fact]
    public void A_Development_build_is_a_separate_app_to_the_OS()
    {
        Assert.Equal("com.example.SqlTool", SparFolders.ChannelId("com.example.SqlTool", development: false));
        Assert.Equal("com.example.SqlTool.Development", SparFolders.ChannelId("com.example.SqlTool", development: true));
    }

    [Fact]
    public void Every_folder_derives_from_the_root_unless_the_app_sets_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "com.example.SqlTool");
        var profile = Path.Combine(Path.GetTempPath(), "elsewhere", "profile");

        var defaults = SparFolders.Under(root, new FolderOptions());
        var chosen = SparFolders.Under(root, new FolderOptions().Profile(profile));

        Assert.Equal(Path.Combine(root, "WebView2"), defaults.Profile);
        Assert.Equal(Path.Combine(root, "data"), defaults.Data);
        Assert.Equal(Path.Combine(root, "logs"), defaults.Logs);
        Assert.Equal(profile, chosen.Profile);
        Assert.Equal(Path.Combine(root, "data"), chosen.Data);
    }

    [Theory]
    [InlineData(@"C:\Profiles\SqlTool", null)]
    [InlineData(@"relative\profile", "not an absolute path")]
    [InlineData(@"\rooted\but\no\drive", "not an absolute path")]
    [InlineData(@"C:relative", "not an absolute path")]
    [InlineData(@"\\server\share\profile", "network drive")]
    [InlineData("//server/share/profile", "network drive")]
    [InlineData(@"\\?\UNC\server\share\profile", "network drive")]
    [InlineData(@"\\?\C:\Profiles\SqlTool", null)]
    public void The_profile_folder_must_be_absolute_and_local(string path, string? problem)
    {
        // Windows path forms; on Linux they are relative names, and the Linux rules come with its host (M1c).
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path forms");
        var found = SparFolders.ProfileProblem(path);

        if (problem is null)
        {
            Assert.Null(found);
        }
        else
        {
            Assert.Contains(problem, found, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("/var/lib/profiles/sqltool", null)]
    [InlineData("relative/profile", "not an absolute path")]
    public void On_Linux_the_profile_folder_must_be_absolute(string path, string? problem)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux path forms");
        var found = SparFolders.ProfileProblem(path);

        if (problem is null)
        {
            Assert.Null(found);
        }
        else
        {
            Assert.Contains(problem, found, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_profile_choice_is_no_problem() => Assert.Null(SparFolders.ProfileProblem(null));

    [Fact]
    public void A_moved_profile_is_recorded_and_both_folders_stay_out_of_reach()
    {
        var requested = Path.Combine(Path.GetTempPath(), "app", "WebView2");
        var moved = Path.Combine(Path.GetTempPath(), "policy", "profile");
        var profile = new BrowserProfile(requested);

        Assert.False(profile.RecordInUse(requested.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.True(profile.RecordInUse(moved));
        Assert.True(profile.Contains(Path.Combine(requested, "EBWebView", "Default", "Cookies")));
        Assert.True(profile.Contains(Path.Combine(moved, "EBWebView")));
        Assert.True(profile.Contains(moved));
        Assert.False(profile.Contains(Path.Combine(Path.GetTempPath(), "app", "WebView2-other")));
        Assert.False(profile.Contains(Path.Combine(Path.GetTempPath(), "app", "data")));
    }

    [Fact]
    public void Every_opt_in_in_force_is_named_in_the_start_up_log()
    {
        Assert.Equal("", new BrowserOptions().Active());

        var options = new BrowserOptions().AllowDownloads().AllowBrowserEnvironmentOverrides();

        Assert.Equal("AllowDownloads() AllowBrowserEnvironmentOverrides()", options.Active());
    }

    [Fact]
    public void The_environment_variables_Spar_removes_are_exactly_the_documented_ones()
    {
        Assert.Equal(
            [
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", "WEBVIEW2_USER_DATA_FOLDER",
                "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE", "WEBVIEW2_CHANNEL_SEARCH_KIND", "WEBVIEW2_RELEASE_CHANNELS",
                "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER", "WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER",
            ],
            BrowserEnvironment.Overrides());
    }

    [Fact]
    public void The_page_pickers_opt_in_drops_Spar_s_browser_argument()
    {
        Assert.Equal("--disable-blink-features=FileSystemAccessLocal", BrowserEnvironment.Arguments(pagePicker: false));
        Assert.Null(BrowserEnvironment.Arguments(pagePicker: true));
    }

    [Theory]
    [InlineData(0x7Bu, false, false, false, false, true)] // F12
    [InlineData(0x49u, true, true, false, false, true)] // Ctrl+Shift+I
    [InlineData(0x4Au, true, true, false, false, true)] // Ctrl+Shift+J
    [InlineData(0x43u, true, true, false, false, true)] // Ctrl+Shift+C
    [InlineData(0x43u, true, false, false, false, false)] // Ctrl+C is the page's
    [InlineData(0x74u, false, false, false, false, false)] // F5 outside Development
    [InlineData(0x74u, false, false, false, true, true)] // F5 in Development
    [InlineData(0x52u, true, false, false, true, true)] // Ctrl+R in Development
    [InlineData(0x52u, true, false, false, false, false)] // Ctrl+R outside Development
    [InlineData(0x50u, true, false, false, true, false)] // Ctrl+P (print)
    [InlineData(0x46u, true, false, false, true, false)] // Ctrl+F (find)
    [InlineData(0x25u, false, false, true, true, false)] // Alt+Left (back)
    [InlineData(0x7Bu, false, false, true, true, false)] // Alt+F12
    public void Where_devtools_are_on_only_their_keys_and_in_Development_the_reload_keys_reach_the_browser(
        uint key, bool control, bool shift, bool alt, bool development, bool allowed) =>
        Assert.Equal(allowed, BrowserDefaults.BrowserKeyAllowed(key, control, shift, alt, development));

    [Theory]
    [InlineData("cut", true, false, true)]
    [InlineData("copy", true, false, true)]
    [InlineData("paste", true, false, true)]
    [InlineData("selectAll", true, false, true)]
    [InlineData("copy", false, false, false)]
    [InlineData("pasteAndMatchStyle", true, false, false)]
    [InlineData("spellcheck", true, false, false)]
    [InlineData("saveAs", false, false, false)]
    [InlineData("print", false, false, false)]
    [InlineData("back", false, false, false)]
    [InlineData("inspectElement", false, false, false)]
    [InlineData("inspectElement", false, true, true)]
    [InlineData("inspectElement", true, true, true)]
    public void The_context_menu_keeps_only_editing_items_and_Inspect_where_devtools_are_on(string name, bool editable, bool devTools, bool kept) =>
        Assert.Equal(kept, BrowserDefaults.KeptItem(name, editable, devTools));
}

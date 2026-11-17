using System.Runtime.InteropServices.Marshalling;
using Spar.Windows.Interop;
using Windows.Win32;

namespace Spar.Windows;

/// <summary>
/// What the browser gets before it starts (plan section 3c): the WebView2 environment variables that would change it
/// are removed from this process unless the app allowed them, and the environment options carry Spar's own arguments.
/// <para>
/// Removing a variable in-process is enough: WebView2's loader reads the variables when the environment is created, in
/// this process, and the browser process it starts inherits this process's environment block, so neither sees a
/// variable removed before the first call to the loader (<c>docs/evidence/m1b-security-defaults.md</c>). That first
/// call is the Runtime check of start-up, so the filter runs before it. Policies an administrator sets under
/// <c>Software\Policies\Microsoft\Edge\WebView2</c> are read by the loader from the registry and stay in force.
/// </para>
/// </summary>
internal static class BrowserEnvironment
{
    /// <summary>
    /// Turns the File System Access pickers off (<c>showOpenFilePicker</c>, <c>showSaveFilePicker</c>,
    /// <c>showDirectoryPicker</c>), so the page sees no such API, as on WebKitGTK (decision "Window-test profiles,
    /// trusted input, the page's pickers and the Sandbox runner").
    /// </summary>
    public const string NoFileSystemAccess = "--disable-blink-features=FileSystemAccessLocal";

    /// <summary>
    /// The variables WebView2 reads to change the browser before it starts: the four plan section 3c names, the
    /// release-channel variables that replaced <c>WEBVIEW2_RELEASE_CHANNEL_PREFERENCE</c> in Microsoft's documentation,
    /// and the two that put the page under a script debugger. Every other variable passes.
    /// </summary>
    public static string[] Overrides() =>
    [
        "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
        "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER",
        "WEBVIEW2_USER_DATA_FOLDER",
        "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE",
        "WEBVIEW2_CHANNEL_SEARCH_KIND",
        "WEBVIEW2_RELEASE_CHANNELS",
        "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER",
        "WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER",
    ];

    /// <summary>
    /// Removes each override variable that is set, and logs it by name (never its value), unless the app allowed them,
    /// in which case each one set is logged as in force. Runs once, at the start of the host, before the loader's first
    /// call.
    /// </summary>
    public static void Filter(bool allowed, HostLog log)
    {
        foreach (var name in Overrides())
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                continue;
            }

            if (allowed)
            {
                log.Warn($"{name} in force (AllowBrowserEnvironmentOverrides).");
                continue;
            }

            // Win32 directly: the variable goes from the block the loader reads and the browser process inherits.
            PInvoke.SetEnvironmentVariable(name, null);
            log.Warn($"{name} ignored (AllowBrowserEnvironmentOverrides allows it).");
        }
    }

    /// <summary>The browser arguments Spar passes: none when the app allowed the page's own pickers.</summary>
    public static string? Arguments(bool pagePicker) => pagePicker ? null : NoFileSystemAccess;
}

/// <summary>
/// The environment options Spar passes to <c>CreateCoreWebView2EnvironmentWithOptions</c>: its browser arguments, and
/// Spar's minimum Runtime as the oldest compatible one. WebView2 appends <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c> to
/// these arguments when the app allows the variable, so Spar's own stay in force.
/// </summary>
[GeneratedComClass]
internal sealed partial class EnvironmentOptions(string? arguments, string targetVersion) : ICoreWebView2EnvironmentOptions
{
    public string? GetAdditionalBrowserArguments() => arguments;

    public void PutAdditionalBrowserArguments(string? value)
    {
    }

    public string? GetLanguage() => null;

    public void PutLanguage(string? value)
    {
    }

    public string? GetTargetCompatibleBrowserVersion() => targetVersion;

    public void PutTargetCompatibleBrowserVersion(string? value)
    {
    }

    public int GetAllowSingleSignOnUsingOSPrimaryAccount() => 0;

    public void PutAllowSingleSignOnUsingOSPrimaryAccount(int allow)
    {
    }
}

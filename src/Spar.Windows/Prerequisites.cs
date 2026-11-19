using System.Globalization;
using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// The start-up checks before anything else of the app starts (plan section 3, "Missing prerequisites", Windows order):
/// the real Windows build through <c>RtlGetVersion</c>, then the WebView2 Runtime the loader would start, through
/// <c>GetAvailableCoreWebView2BrowserVersionString</c> (never the registry, which can name a Runtime that is gone), then
/// <c>CompareBrowserVersions</c> against Spar's minimum. A Runtime that is present but broken shows only when the
/// controller is created (<see cref="BrowserView"/>). The decisions are plain functions, so tests cover every path
/// without uninstalling anything; a Development or Test build can raise both minimums through the environment to
/// force the paths on a healthy machine, and a Production build has no such override.
/// </summary>
internal static class Prerequisites
{
    /// <summary>Windows 10 22H2, provisional (user decision, 2026-11-17); M4 confirms it on a real Windows 10.</summary>
    public const int MinimumWindowsBuild = StartupReport.MinimumWindowsBuild;

    /// <summary>
    /// The minimum WebView2 Runtime (decision "M1b's remaining questions without a spike"): 139, the Runtime of the
    /// newest interface Spar uses (find in page, <c>ICoreWebView2_28</c>, SDK 1.0.3405.78).
    /// </summary>
    public const string MinimumRuntime = "139.0.3405.78";

    /// <summary>The component's name in messages.</summary>
    public const string Runtime = "WebView2 Runtime";

    /// <summary>Microsoft's end-user page for the Runtime, where the distribution guide sends users.</summary>
    public const string RuntimeDownload = "https://developer.microsoft.com/microsoft-edge/webview2/consumer/";

    /// <summary>Raises the minimum Windows build in Development and Test builds only, to force "OS too old".</summary>
    public const string RequireWindowsBuildVariable = "SPAR_DEV_REQUIRE_WINDOWS_BUILD";

    /// <summary>Raises the minimum Runtime in Development and Test builds only, to force "too old".</summary>
    public const string RequireRuntimeVariable = "SPAR_DEV_REQUIRE_WEBVIEW2";

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)</c>: the loader found no Runtime.</summary>
    private const int NotFound = unchecked((int)0x80070002);

    /// <summary>The OS check: null when this Windows is new enough.</summary>
    public static unsafe StartupProblem? CheckWindows()
    {
        var info = new OsVersionInfo { Size = (uint)sizeof(OsVersionInfo) };
        var status = Native.RtlGetVersion(&info);
        var found = status == 0 ? new Version((int)info.Major, (int)info.Minor, (int)info.Build) : Environment.OSVersion.Version;
        return DecideWindows(found, RequiredWindowsBuild());
    }

    /// <summary>The Runtime checks: null when a Runtime is found and is new enough.</summary>
    public static StartupProblem? CheckRuntime()
    {
        string? found = null;
        int status;
        try
        {
            status = Native.GetAvailableCoreWebView2BrowserVersionString(null, out var version);
            if (version != 0)
            {
                found = Marshal.PtrToStringUni(version);
                Marshal.FreeCoTaskMem(version);
            }
        }
        catch (DllNotFoundException exception)
        {
            // A CoreCLR build without WebView2Loader.dll beside it: the app was copied without its loader.
            return new StartupProblem(
                StartupProblemKind.Missing,
                "WebView2 loader",
                null,
                null,
                $"The app's file WebView2Loader.dll is missing. Install the app again. ({exception.Message})",
                null);
        }

        var required = RequiredRuntime();
        var comparison = 0;
        var comparisonFailed = false;
        if (status >= 0 && found is not null)
        {
            comparisonFailed = Native.CompareBrowserVersions(found, required, out comparison) < 0;
        }

        return DecideRuntime(status, found, required, comparisonFailed, comparison);
    }

    /// <summary>Whether <paramref name="found"/> meets <paramref name="requiredBuild"/>: Windows 11 reports 10.0 too.</summary>
    internal static StartupProblem? DecideWindows(Version found, int requiredBuild)
    {
        if (found.Major > 10 || (found.Major == 10 && found.Build >= requiredBuild))
        {
            return null;
        }

        return new StartupProblem(
            StartupProblemKind.OsTooOld,
            "Windows",
            found.ToString(3),
            $"10.0.{requiredBuild}",
            requiredBuild == MinimumWindowsBuild
                ? "This app needs Windows 10 version 22H2 or later. Update Windows, then start the app again."
                : $"This app needs Windows 10 build {requiredBuild} or later. Update Windows, then start the app again.",
            null);
    }

    /// <summary>
    /// The Runtime's verdict from what the loader answered: no Runtime, an unreadable one, one older than
    /// <paramref name="required"/>, or null when it may start.
    /// </summary>
    internal static StartupProblem? DecideRuntime(int status, string? found, string required, bool comparisonFailed, int comparison)
    {
        if (status == NotFound || (status >= 0 && found is null))
        {
            return new StartupProblem(
                StartupProblemKind.Missing,
                Runtime,
                null,
                required,
                "This app needs the Microsoft Edge WebView2 Runtime, which is not installed on this computer. Install it from Microsoft, then start the app again.",
                RuntimeDownload);
        }

        if (status < 0)
        {
            return new StartupProblem(
                StartupProblemKind.Broken,
                Runtime,
                null,
                required,
                $"The Microsoft Edge WebView2 Runtime on this computer cannot be read (error 0x{status:X8}). Install it again from Microsoft, then start the app again.",
                RuntimeDownload);
        }

        if (comparisonFailed)
        {
            return new StartupProblem(
                StartupProblemKind.Broken,
                Runtime,
                found,
                required,
                $"The Microsoft Edge WebView2 Runtime on this computer reports a version that cannot be read ({found}). Install it again from Microsoft, then start the app again.",
                RuntimeDownload);
        }

        if (comparison < 0)
        {
            return new StartupProblem(
                StartupProblemKind.TooOld,
                Runtime,
                found,
                required,
                $"This app needs the Microsoft Edge WebView2 Runtime {required} or later; this computer has {found}. Update it from Microsoft, then start the app again.",
                RuntimeDownload);
        }

        return null;
    }

    /// <summary>A controller that could not be created: the Runtime is there but broken (plan section 3, order step 5).</summary>
    internal static StartupProblem ControllerFailed(string? found, int status) => new(
        StartupProblemKind.Broken,
        Runtime,
        found,
        MinimumRuntime,
        $"The Microsoft Edge WebView2 Runtime on this computer could not start (error 0x{status:X8}). Install it again from Microsoft, then start the app again.",
        RuntimeDownload);

    private static int RequiredWindowsBuild()
    {
        if (SparDevelopment.IsSupported
            && int.TryParse(Environment.GetEnvironmentVariable(RequireWindowsBuildVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var build))
        {
            return build;
        }

        return MinimumWindowsBuild;
    }

    private static string RequiredRuntime()
    {
        if (SparDevelopment.IsSupported && Environment.GetEnvironmentVariable(RequireRuntimeVariable) is { Length: > 0 } version)
        {
            return version;
        }

        return MinimumRuntime;
    }
}

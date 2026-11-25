using System.Runtime.InteropServices;
using Spar.Tool.Machine;

namespace Spar.Tool.Doctor;

/// <summary>
/// Windows: the WebView2 Runtime, through the loader's <c>GetAvailableCoreWebView2BrowserVersionString</c> as the
/// Windows host checks it before its window, against the host's minimum; NativeAOT's prerequisite, the C++ build tools
/// that the ILCompiler package finds through <c>vswhere</c>; and whether Windows takes paths longer than 260 characters.
/// </summary>
internal static class WindowsChecks
{
    /// <summary>The Windows host's minimum Runtime (<c>Spar.Windows.Prerequisites.MinimumRuntime</c>; a test keeps them equal).</summary>
    public const string MinimumRuntime = "139.0.3405.78";

    public const string RuntimeDownload = "https://developer.microsoft.com/microsoft-edge/webview2/consumer/";

    public const string NativeAotPrerequisites = "https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites";

    public const string LongPathsDocumentation = "https://learn.microsoft.com/windows/win32/fileio/maximum-file-path-limitation";

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)</c>: the loader found no Runtime.</summary>
    private const int NotFound = unchecked((int)0x80070002);

    public static IEnumerable<Finding> Check(IMachine machine)
    {
        yield return WebView2(machine.WebView2());
        yield return NativeAot(machine);
        yield return LongPaths(machine.MachineRegistryNumber(@"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled"));
    }

    internal static Finding WebView2(WebView2Answer answer)
    {
        if (answer.LoaderProblem is { } problem)
        {
            return Finding.Error(FindingIds.WebView2, Area.Machine, $"The WebView2 loader could not be loaded: {problem}",
                "Install the spar tool again (dotnet tool restore): the loader comes with it.");
        }

        if (answer.Status == NotFound || (answer.Status >= 0 && answer.Version is null))
        {
            return Finding.Error(FindingIds.WebView2, Area.Machine, "The WebView2 Runtime is not installed: the app's window cannot start.",
                $"Install the Evergreen WebView2 Runtime ({RuntimeDownload}).");
        }

        if (answer.Status < 0)
        {
            return Finding.Error(FindingIds.WebView2, Area.Machine, $"The WebView2 loader could not read the Runtime (0x{answer.Status:X8}).",
                $"Repair or install the Evergreen WebView2 Runtime ({RuntimeDownload}).");
        }

        var version = answer.Version!;
        if (Compare(version, MinimumRuntime) < 0)
        {
            return Finding.Error(FindingIds.WebView2, Area.Machine, $"WebView2 Runtime {version} is older than Spar's minimum, {MinimumRuntime}.",
                $"Update the WebView2 Runtime ({RuntimeDownload}); it updates itself with Microsoft Edge's updater when that runs.");
        }

        return Finding.Info(FindingIds.WebView2, Area.Machine, $"WebView2 Runtime {version}.");
    }

    internal static Finding NativeAot(IMachine machine)
    {
        var arm = machine.Architecture == Architecture.Arm64;
        var component = arm ? "Microsoft.VisualStudio.Component.VC.Tools.ARM64" : "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";
        var fix = $"Install Visual Studio (or its Build Tools) with the \"Desktop development with C++\" workload ({NativeAotPrerequisites}). Building and Debug runs do not need it; publishing does.";
        var programFiles = machine.Variable("ProgramFiles(x86)") is { Length: > 0 } x86 ? x86 : @"C:\Program Files (x86)";
        var vswhere = Path.Join(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!machine.FileExists(vswhere))
        {
            return Finding.Warning(FindingIds.WindowsNativeAot, Area.Machine, "No Visual Studio installation was found (no vswhere.exe): dotnet publish with NativeAOT needs its C++ build tools.", fix);
        }

        var run = machine.Run(vswhere, ["-latest", "-prerelease", "-products", "*", "-requires", component, "-property", "installationPath"]);
        var installation = run.Succeeded ? run.Output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) : null;
        if (installation is null || !machine.FileExists(Path.Join(installation, "VC", "Auxiliary", "Build", "vcvarsall.bat")))
        {
            return Finding.Warning(FindingIds.WindowsNativeAot, Area.Machine, $"No Visual Studio installation has the C++ build tools ({component}) that dotnet publish with NativeAOT needs.", fix);
        }

        return Finding.Info(FindingIds.WindowsNativeAot, Area.Machine, $"NativeAOT's C++ build tools, in {installation}.");
    }

    internal static Finding LongPaths(int? enabled)
    {
        if (enabled == 1)
        {
            return Finding.Info(FindingIds.LongPaths, Area.Machine, "Windows takes paths longer than 260 characters (LongPathsEnabled).");
        }

        return Finding.Info(FindingIds.LongPaths, Area.Machine,
            $"Windows limits paths to 260 characters for programs that do not opt out (LongPathsEnabled is off). Keep the app's folder short, since node_modules nests deeply; an administrator can lift the limit ({LongPathsDocumentation}).");
    }

    /// <summary>Compares two Runtime versions part by part; a channel name after the numbers (" beta") is ignored.</summary>
    internal static int Compare(string found, string required)
    {
        static int[] Parts(string version) =>
            [.. version.Split(' ')[0].Split('.').Select(part => int.TryParse(part, out var value) ? value : 0)];

        var left = Parts(found);
        var right = Parts(required);
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            var a = index < left.Length ? left[index] : 0;
            var b = index < right.Length ? right[index] : 0;
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }

        return 0;
    }
}

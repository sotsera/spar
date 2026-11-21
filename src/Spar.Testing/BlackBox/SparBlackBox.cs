using System.Collections;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Spar.Testing;

/// <summary>
/// The black-box level (plan section 5, level 7): the app's exact Production executable, launched, with nothing
/// replaced. Its per-user folders go into a disposable data root under <c>artifacts/</c> through the environment only,
/// since a Production app takes no switch for them: on Windows <c>LOCALAPPDATA</c>, <c>APPDATA</c>, <c>TEMP</c> and
/// <c>TMP</c>; on Linux <c>HOME</c>, every <c>XDG_*</c> folder and <c>TMPDIR</c> (<c>XDG_RUNTIME_DIR</c> in <c>/dev/shm</c>).
/// It ends with the test process (a job object on Windows, <c>KillOnParentExit</c> on Linux), shows on a desktop of its
/// own that never reaches the monitor (a hidden desktop on Windows, a private Xvfb on Linux), and is read and driven
/// through the platform's accessibility API (<see cref="SparBlackBoxApp.Window"/>, <see cref="SparBlackBoxApp.WaitForDialogAsync"/>):
/// UI Automation on Windows, AT-SPI on Linux, through a private D-Bus session per launch. Never through input.
/// </summary>
public static class SparBlackBox
{
    /// <summary>How long a Linux session end is waited for: the host's 4 s budget and a second.</summary>
    private static readonly TimeSpan SessionEndWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Where every launch of this test process puts its app on Windows (the hidden desktop unless a launch says
    /// otherwise). One switch for the run: Windows Sandbox's runner (step 8c) runs these tests in its guest with the
    /// window activated, so real input reaches it; a CI run shows it on the runner's own desktop, where no one is at the
    /// screen and where a second desktop cannot host WebView2 (GitHub's Windows Server 2025 runner:
    /// <c>CreateCoreWebView2Controller</c> failed with <c>0x80070578</c> on every desktop but the default one). Linux
    /// apps always show on the test process's private Xvfb.
    /// </summary>
    internal static SparBlackBoxPlacement Placement { get; set; } =
        SandboxGuest.IsUserSession ? SparBlackBoxPlacement.Activated
        : IsContinuousIntegration(System.Environment.GetEnvironmentVariable) ? SparBlackBoxPlacement.ShownWithoutActivation
        : SparBlackBoxPlacement.HiddenDesktop;

    /// <summary>
    /// Starts <paramref name="project"/>'s Production executable and, unless <see cref="SparBlackBoxOptions.WaitForReady"/>
    /// is false, waits until it is ready: the host's <c>ui-ready</c> line in its log file under the data root (the page
    /// connected its bridge), and the accessibility API finding its window. A launch that fails ends the app and deletes
    /// its root. On Linux, a machine without what the black box needs (Xvfb, D-Bus, at-spi2-core) throws
    /// <see cref="PlatformNotSupportedException"/> naming it.
    /// </summary>
    public static async Task<SparBlackBoxApp> LaunchAsync(
        SparProject project,
        Action<SparBlackBoxOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var options = new SparBlackBoxOptions();
        configure?.Invoke(options);
        options.Validate();
        if (BlackBoxPlatform.Problem is { } problem)
        {
            throw new PlatformNotSupportedException(problem);
        }

        if (!File.Exists(project.ExecutablePath))
        {
            throw new FileNotFoundException(
                $"{project.Name}'s Production executable is not beside the tests ({project.ExecutablePath}). The test project publishes it " +
                "when it sets <SparBlackBoxTests>true</SparBlackBoxTests>, unless SparBlackBoxPublish is false.",
                project.ExecutablePath);
        }

        var root = Root(options);
        var app = Start(project, options, root);
        try
        {
            if (options.WaitForReady)
            {
                await app.WaitForReadyAsync(options.ReadyTimeout, cancellationToken);
            }

            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The app's whole environment: the test process's, without the removed prefixes and (on Linux) the variables of
    /// the session's desktop and accessibility, with the fixture's folders and the extras.
    /// </summary>
    internal static Dictionary<string, string> Environment(SparBlackBoxOptions options, BlackBoxFolders folders)
    {
        var environment = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (!SparBlackBoxOptions.RemovedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                && !(OperatingSystem.IsLinux() && SparBlackBoxOptions.LinuxRemoved.Contains(name, StringComparer.Ordinal)))
            {
                environment[name] = (string?)entry.Value ?? "";
            }
        }

        if (OperatingSystem.IsLinux())
        {
            environment["HOME"] = folders.Home;
            environment["XDG_DATA_HOME"] = options.LocalAppData ?? folders.LocalAppData;
            environment["XDG_CONFIG_HOME"] = folders.RoamingAppData;
            environment["XDG_CACHE_HOME"] = folders.Cache;
            environment["XDG_STATE_HOME"] = folders.State;
            environment["XDG_RUNTIME_DIR"] = folders.Runtime;
            environment["TMPDIR"] = folders.Temp;
            environment["GDK_BACKEND"] = "x11";
            if (options.Display is "")
            {
                environment.Remove("DISPLAY");
            }
            else
            {
                environment["DISPLAY"] = options.Display ?? folders.Display;
            }
        }
        else
        {
            environment["LOCALAPPDATA"] = options.LocalAppData ?? folders.LocalAppData;
            environment["APPDATA"] = folders.RoamingAppData;
            environment["TEMP"] = folders.Temp;
            environment["TMP"] = folders.Temp;
        }

        foreach (var (name, value) in options.Environment)
        {
            environment[name] = value;
        }

        foreach (var (name, value) in options.DevelopmentVariables)
        {
            environment[name] = value;
        }

        return environment;
    }

    /// <summary>
    /// Whether this test process runs on a CI service: <c>CI</c> set to <c>true</c> or <c>1</c> (GitHub Actions, GitLab,
    /// and most others), or <c>TF_BUILD</c> set to <c>true</c> (Azure Pipelines).
    /// </summary>
    internal static bool IsContinuousIntegration(Func<string, string?> variable)
    {
        static bool IsSet(string? value) =>
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

        return IsSet(variable("CI")) || IsSet(variable("TF_BUILD"));
    }

    private static (string Path, bool Owned) Root(SparBlackBoxOptions options)
    {
        if (options.DataRoot is null)
        {
            return (TestDataRoots.NextBlackBox(), true);
        }

        var shared = Path.GetFullPath(options.DataRoot);
        if (!TestDataRoots.IsDataRoot(shared) || !Directory.Exists(BlackBoxFolders.For(shared).LocalAppData))
        {
            throw new ArgumentException(
                $"'{options.DataRoot}' is not the data root of another black-box launch of this test run: pass first.DataRoot.",
                nameof(options));
        }

        return (shared, false);
    }

    private static SparBlackBoxApp Start(SparProject project, SparBlackBoxOptions options, (string Path, bool Owned) root)
    {
        var folders = BlackBoxFolders.For(root.Path);
        folders.Create();
        var clock = Stopwatch.StartNew();
        IBlackBoxProcess process;
        IBlackBoxAutomation automation;
        try
        {
            if (BlackBoxPlatform.IsWindows)
            {
                var placement = options.Placement ?? Placement;
                automation = AutomationHost.For(placement);

                // SW_SHOWNOACTIVATE, which the host's first ShowWindow(SW_SHOWDEFAULT) takes; on its own desktop the app shows as it would.
                ushort? show = placement == SparBlackBoxPlacement.ShownWithoutActivation ? (ushort)4 : null;
                process = AppProcess.Start(
                    project.ExecutablePath,
                    [.. options.Arguments],
                    Environment(options, folders),
                    folders.Root,
                    automation.Desktop,
                    show,
                    killOnClose: !options.OutlivesTest);
            }
            else if (BlackBoxPlatform.IsLinux)
            {
                var linux = LinuxAppProcess.Start(
                    project.ExecutablePath, [.. options.Arguments], Environment(options, folders), folders.Root, options.SessionBus, options.Wrapper);
                process = linux;
                automation = linux.CreateAutomation(project.Name, SessionEndWait);
            }
            else
            {
                throw BlackBoxPlatform.NotSupported();
            }
        }
        catch
        {
            if (root.Owned)
            {
                TestDataRoots.Delete(root.Path);
                folders.DeleteRuntime();
            }

            throw;
        }

        return new SparBlackBoxApp(project, options, folders, root.Owned, process, automation, clock);
    }
}

/// <summary>The fixture's platform checks, which the platform analyzer reads as guards.</summary>
internal static class BlackBoxPlatform
{
    [SupportedOSPlatformGuard("windows10.0.17763")]
    public static bool IsWindows => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    [SupportedOSPlatformGuard("linux")]
    public static bool IsLinux => OperatingSystem.IsLinux();

    /// <summary>Why the black box cannot run here, or null when it can: an OS it does not support, or (Linux) what is missing.</summary>
    public static string? Problem =>
        IsWindows ? null
        : IsLinux ? LinuxDesktop.Problem
        : "The black-box fixture runs on Windows 10 1809 and later and on Linux.";

    public static PlatformNotSupportedException NotSupported() =>
        new(Problem ?? "The black-box fixture runs on Windows 10 1809 and later and on Linux.");

    /// <summary>Real input outside Windows Sandbox's guest has no Linux counterpart in the fixture yet (a gap).</summary>
    public static PlatformNotSupportedException NoRealInput() =>
        new("The black box's real input (Windows Sandbox's checks) runs on Windows; on Linux it is not built yet.");
}

/// <summary>
/// A launch's folders under its data root: the per-user folders the environment points at, and the app's own. On
/// Windows <c>LocalAppData</c>, <c>AppData</c> and <c>Temp</c>; on Linux <c>home</c>, <c>data</c> (<c>XDG_DATA_HOME</c>,
/// what <see cref="LocalAppData"/> names), <c>config</c>, <c>cache</c>, <c>state</c> and <c>tmp</c>, with the runtime
/// folder in <c>/dev/shm</c> (WebKit's sandbox refuses one elsewhere; its sockets need a short path), named after the
/// root so a second launch on the same root shares it, as the app's single-instance socket requires.
/// </summary>
internal sealed record BlackBoxFolders(string Root, string LocalAppData, string RoamingAppData, string Temp)
{
    public string Home => Path.Combine(Root, "home");

    public string Cache => Path.Combine(Root, "cache");

    public string State => Path.Combine(Root, "state");

    /// <summary>The runtime folder (Linux): <c>/dev/shm/spar-bb-&lt;root's hash&gt;-&lt;test process&gt;</c>.</summary>
    public string Runtime =>
        $"/dev/shm/spar-bb-{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Root)))[..12]}-{System.Environment.ProcessId}";

    /// <summary>The test process's private display (Linux), started at the first launch.</summary>
    public string Display => BlackBoxPlatform.IsLinux ? LinuxDesktop.Display : "";

    public static BlackBoxFolders For(string root) => OperatingSystem.IsLinux()
        ? new(root, Path.Combine(root, "data"), Path.Combine(root, "config"), Path.Combine(root, "tmp"))
        : new(root, Path.Combine(root, "LocalAppData"), Path.Combine(root, "AppData"), Path.Combine(root, "Temp"));

    public void Create()
    {
        Directory.CreateDirectory(LocalAppData);
        Directory.CreateDirectory(RoamingAppData);
        Directory.CreateDirectory(Temp);
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(Home);
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(State);
            Directory.CreateDirectory(Runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Deletes the runtime folder (Linux), once every process of the root is gone.</summary>
    public void DeleteRuntime()
    {
        if (OperatingSystem.IsLinux() && Directory.Exists(Runtime))
        {
            try
            {
                Directory.Delete(Runtime, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A socket of a process that is still going; the test process's next run deletes it.
            }
        }
    }
}

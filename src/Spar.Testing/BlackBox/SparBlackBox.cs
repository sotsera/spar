using System.Collections;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Spar.Testing;

/// <summary>
/// The black-box level (plan section 5, level 7): the app's exact Production executable, launched, with nothing
/// replaced. Its per-user folders go into a disposable data root under <c>artifacts/</c> through the environment only
/// (<c>LOCALAPPDATA</c>, <c>APPDATA</c>, <c>TEMP</c>, <c>TMP</c>), since a Production app takes no switch for them. It runs in
/// a job object that ends it with the test process, on a desktop of its own that never reaches the monitor, and is
/// read and driven through UI Automation (<see cref="SparBlackBoxApp.Window"/>, <see cref="SparBlackBoxApp.WaitForDialogAsync"/>),
/// never through input. Windows only for now; elsewhere every call throws <see cref="PlatformNotSupportedException"/>.
/// </summary>
public static class SparBlackBox
{
    /// <summary>
    /// Where every launch of this test process puts its app (the hidden desktop unless a launch says otherwise). One
    /// switch for the run: Windows Sandbox's runner (step 8c) runs these tests in its guest with the window activated, so
    /// real input reaches it; a CI run shows it on the runner's own desktop, where no one is at the screen and where a
    /// second desktop cannot host WebView2 (GitHub's Windows Server 2025 runner: <c>CreateCoreWebView2Controller</c>
    /// failed with <c>0x80070578</c> on every desktop but the default one).
    /// </summary>
    internal static SparBlackBoxPlacement Placement { get; set; } =
        SandboxGuest.IsUserSession ? SparBlackBoxPlacement.Activated
        : IsContinuousIntegration(System.Environment.GetEnvironmentVariable) ? SparBlackBoxPlacement.ShownWithoutActivation
        : SparBlackBoxPlacement.HiddenDesktop;

    /// <summary>
    /// Starts <paramref name="project"/>'s Production executable and, unless <see cref="SparBlackBoxOptions.WaitForReady"/>
    /// is false, waits until it is ready: the host's <c>ui-ready</c> line in its log file under the data root (the page
    /// connected its bridge), and UI Automation finding its window. A launch that fails ends the app and deletes its root.
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
        if (!BlackBoxPlatform.IsSupported)
        {
            throw BlackBoxPlatform.NotSupported();
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

    /// <summary>The app's whole environment: the test process's, without the removed prefixes, with the fixture's folders and the extras.</summary>
    internal static Dictionary<string, string> Environment(SparBlackBoxOptions options, BlackBoxFolders folders)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (!SparBlackBoxOptions.RemovedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                environment[name] = (string?)entry.Value ?? "";
            }
        }

        environment["LOCALAPPDATA"] = options.LocalAppData ?? folders.LocalAppData;
        environment["APPDATA"] = folders.RoamingAppData;
        environment["TEMP"] = folders.Temp;
        environment["TMP"] = folders.Temp;
        foreach (var (name, value) in options.Environment)
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
            return (TestDataRoots.Next("black-box"), true);
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
        if (!BlackBoxPlatform.IsSupported)
        {
            throw BlackBoxPlatform.NotSupported();
        }

        var folders = BlackBoxFolders.For(root.Path);
        folders.Create();
        var placement = options.Placement ?? Placement;
        var automation = AutomationHost.For(placement);

        // SW_SHOWNOACTIVATE, which the host's first ShowWindow(SW_SHOWDEFAULT) takes; on its own desktop the app shows as it would.
        ushort? show = placement == SparBlackBoxPlacement.ShownWithoutActivation ? (ushort)4 : null;
        var clock = Stopwatch.StartNew();
        AppProcess process;
        try
        {
            process = AppProcess.Start(
                project.ExecutablePath,
                [.. options.Arguments],
                Environment(options, folders),
                folders.Root,
                automation.Desktop,
                show,
                killOnClose: !options.OutlivesTest);
        }
        catch
        {
            if (root.Owned)
            {
                TestDataRoots.Delete(root.Path);
            }

            throw;
        }

        return new SparBlackBoxApp(project, options, folders, root.Owned, process, automation, clock);
    }
}

/// <summary>The fixture's one platform check, which the platform analyzer reads as a guard.</summary>
internal static class BlackBoxPlatform
{
    [SupportedOSPlatformGuard("windows10.0.17763")]
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    public static PlatformNotSupportedException NotSupported() =>
        new("The black-box fixture runs on Windows 10 1809 and later for now; Linux follows with its host (M1c).");
}

/// <summary>A launch's folders under its data root: the per-user folders the environment points at, and the app's own.</summary>
internal sealed record BlackBoxFolders(string Root, string LocalAppData, string RoamingAppData, string Temp)
{
    public static BlackBoxFolders For(string root) => new(
        root,
        Path.Combine(root, "LocalAppData"),
        Path.Combine(root, "AppData"),
        Path.Combine(root, "Temp"));

    public void Create()
    {
        Directory.CreateDirectory(LocalAppData);
        Directory.CreateDirectory(RoamingAppData);
        Directory.CreateDirectory(Temp);
    }
}

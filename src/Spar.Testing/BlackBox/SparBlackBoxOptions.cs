namespace Spar.Testing;

/// <summary>How <see cref="SparBlackBox.LaunchAsync"/> starts the app, and how long it waits.</summary>
public sealed class SparBlackBoxOptions
{
    /// <summary>The variables the fixture sets itself, which <see cref="Environment"/> may not name.</summary>
    internal static readonly string[] FixtureVariables = OperatingSystem.IsLinux()
        ? ["HOME", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_STATE_HOME", "XDG_RUNTIME_DIR", "TMPDIR", "DISPLAY", "DBUS_SESSION_BUS_ADDRESS", "GTK_A11Y", "AT_SPI_BUS_ADDRESS", "NO_AT_BRIDGE"]
        : ["LOCALAPPDATA", "APPDATA", "TEMP", "TMP"];

    /// <summary>
    /// What a Linux app never inherits from the test process: the session's desktop, bus and accessibility switches
    /// (<c>GTK_A11Y=none</c> would hide it from AT-SPI), its launcher's tokens, and the variables the fixture sets.
    /// </summary>
    internal static readonly string[] LinuxRemoved =
    [
        "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS", "GTK_A11Y", "AT_SPI_BUS_ADDRESS", "NO_AT_BRIDGE", "XDG_ACTIVATION_TOKEN",
        "DESKTOP_STARTUP_ID", "XDG_CURRENT_DESKTOP", "XDG_SESSION_TYPE", "XDG_SESSION_DESKTOP",
    ];

    /// <summary>The variable prefixes the app never gets: the browser's overrides and Spar's own switches.</summary>
    internal static readonly string[] RemovedPrefixes = OperatingSystem.IsLinux() ? ["WEBKIT_", "JSC_", "SPAR_"] : ["WEBVIEW2_", "SPAR_"];

    /// <summary>The removed prefixes a test may still set: on Linux the engine's, whose filtering by the host is what Spar's tests check.</summary>
    private static readonly string[] Settable = OperatingSystem.IsLinux() ? ["WEBKIT_", "JSC_"] : [];

    /// <summary>The app's command-line arguments, each passed as one argument.</summary>
    public IList<string> Arguments { get; } = [];

    /// <summary>
    /// Variables added to the app's environment, which is otherwise the test process's own, with its per-user folders
    /// inside the data root (Windows: <c>LOCALAPPDATA</c>, <c>APPDATA</c>, <c>TEMP</c> and <c>TMP</c>; Linux: <c>HOME</c>, every
    /// <c>XDG_*</c> folder and <c>TMPDIR</c>, with <c>DISPLAY</c> and <c>DBUS_SESSION_BUS_ADDRESS</c> the fixture's) and every
    /// <c>SPAR_*</c> variable and the browser's overrides removed (Windows: <c>WEBVIEW2_*</c>; Linux: inherited <c>WEBKIT_*</c>
    /// and <c>JSC_*</c>, which may be set here to check that the app ignores them). The fixture's own may not be set here.
    /// </summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Another instance's <see cref="SparBlackBoxApp.DataRoot"/>, to start a second instance on the same data, as a
    /// second launch by the same user would. That instance's handle keeps the root and deletes it; null gives this
    /// launch a new root of its own.
    /// </summary>
    public string? DataRoot { get; set; }

    /// <summary>
    /// Wait until the app is ready (its log has the host's <c>ui-ready</c> line and UI Automation finds its window).
    /// False returns once the process has started: for start-up failures, their native message and their exit code.
    /// </summary>
    public bool WaitForReady { get; set; } = true;

    /// <summary>How long the launch waits for the app to be ready.</summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long <see cref="SparBlackBoxApp.CloseAsync"/> and <see cref="SparBlackBoxApp.WaitForExitAsync"/> wait for
    /// the process to exit, and how long disposal lets an exit already asked for finish before it ends the app.
    /// </summary>
    public TimeSpan ExitTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Keep the data root after the test (it is deleted by default).</summary>
    public bool KeepData { get; set; }

    /// <summary>
    /// Where the app's window goes, for this launch only; null takes <see cref="SparBlackBox.Placement"/>, the run's
    /// choice. Internal: Windows Sandbox's runner (step 8c) is its user.
    /// </summary>
    internal SparBlackBoxPlacement? Placement { get; set; }

    /// <summary>
    /// The app outlives the test process: its job does not end it when the test process ends. For a check that ends
    /// the session the test runs in (a sign-out ends the test process first, which would end the app before the session
    /// end reaches it; Windows Sandbox run of 2026-11-19). Only in a guest Spar's runner started, where the sandbox is
    /// stopped after the job; refused elsewhere, so a crashed test run never leaves an app on a developer's machine.
    /// </summary>
    internal bool OutlivesTest { get; set; }

    /// <summary>
    /// A <c>LOCALAPPDATA</c> in place of the data root's, for Spar's own test of the host's refusal of a network path.
    /// Internal: an app's test never moves the app's folders outside its disposable root.
    /// </summary>
    internal string? LocalAppData { get; set; }

    /// <summary>
    /// Linux, for Spar's own start-up tests: the app's <c>DISPLAY</c> in place of the private Xvfb (<c>""</c> removes it,
    /// a number nobody serves fails GTK's start). Internal: an app's test always shows on the private display.
    /// </summary>
    internal string? Display { get; set; }

    /// <summary>
    /// Linux, for Spar's own theme test: a session bus the test started (with its own Settings portal) in place of the
    /// launch's private one. Its accessibility bus is the one read.
    /// </summary>
    internal string? SessionBus { get; set; }

    /// <summary>
    /// Linux, for Spar's own start-up tests: a command the executable is appended to (<c>bwrap</c> with a mount that hides
    /// a library or a helper); the app is the wrapper's descendant that runs the executable.
    /// </summary>
    internal IReadOnlyList<string>? Wrapper { get; set; }

    /// <summary>
    /// <c>SPAR_*</c> variables for a Development build Spar's own tests launch (its automated mode, triggers and scripted
    /// answers): a Production app takes no Spar switch, so these are for a build Spar's runner names (<see cref="SparProject.Built"/>).
    /// </summary>
    internal IDictionary<string, string> DevelopmentVariables { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Refuses options the fixture cannot honour, before anything starts.</summary>
    internal void Validate()
    {
        if (OutlivesTest && !SandboxGuest.IsRunnerGuest)
        {
            throw new ArgumentException("OutlivesTest is for Spar's Sandbox checks only: outside a runner's guest an app must end with its test.", nameof(OutlivesTest));
        }

        if (ReadyTimeout <= TimeSpan.Zero || ExitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ReadyTimeout), "ReadyTimeout and ExitTimeout must be positive.");
        }

        foreach (var name in Environment.Keys)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('=', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException($"'{name}' is not an environment variable name.", nameof(Environment));
            }

            if (FixtureVariables.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{name} is the fixture's own: it points into the data root, so the app never writes to the real per-user folders.",
                    nameof(Environment));
            }

            if (RemovedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                && !Settable.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    $"{name} is never passed: a Production app takes no Spar switch, and WEBVIEW2_* variables would change its browser.",
                    nameof(Environment));
            }
        }

        foreach (var argument in Arguments)
        {
            if (argument is null || argument.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("An argument cannot be null or contain a null character.", nameof(Arguments));
            }
        }
    }
}

/// <summary>Where a launched app's window goes.</summary>
internal enum SparBlackBoxPlacement
{
    /// <summary>A desktop of the test process's own, never on the monitor: nothing shows, focus is never taken.</summary>
    HiddenDesktop,

    /// <summary>The user's desktop, shown without activation (<c>SW_SHOWNOACTIVATE</c>); measured to take the foreground anyway.</summary>
    ShownWithoutActivation,

    /// <summary>The user's desktop, shown as a launch from the shell shows it: real input reaches it (Windows Sandbox).</summary>
    Activated,
}

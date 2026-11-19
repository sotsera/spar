namespace Spar.Testing;

/// <summary>How <see cref="SparBlackBox.LaunchAsync"/> starts the app, and how long it waits.</summary>
public sealed class SparBlackBoxOptions
{
    /// <summary>The variables the fixture sets itself, which <see cref="Environment"/> may not name.</summary>
    internal static readonly string[] FixtureVariables = ["LOCALAPPDATA", "APPDATA", "TEMP", "TMP"];

    /// <summary>The variable prefixes the app never gets: the browser's overrides and Spar's own switches.</summary>
    internal static readonly string[] RemovedPrefixes = ["WEBVIEW2_", "SPAR_"];

    /// <summary>The app's command-line arguments, each passed as one argument.</summary>
    public IList<string> Arguments { get; } = [];

    /// <summary>
    /// Variables added to the app's environment, which is otherwise the test process's own, with <c>LOCALAPPDATA</c>,
    /// <c>APPDATA</c>, <c>TEMP</c> and <c>TMP</c> inside the data root and every <c>WEBVIEW2_*</c> and <c>SPAR_*</c>
    /// variable removed. None of those may be set here.
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

    /// <summary>Refuses options the fixture cannot honour, before anything starts.</summary>
    internal void Validate()
    {
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

            if (RemovedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
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

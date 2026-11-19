namespace Spar;

/// <summary>What kind of problem stopped the app from starting (plan section 3, "Missing prerequisites").</summary>
public enum StartupProblemKind
{
    /// <summary>Something the app needs is not installed, such as the WebView2 Runtime. Exit code 3.</summary>
    Missing,

    /// <summary>It is installed, but older than the app needs. Exit code 4.</summary>
    TooOld,

    /// <summary>It is installed, but does not work. Exit code 5.</summary>
    Broken,

    /// <summary>The operating system is older than the app supports. Exit code 7.</summary>
    OsTooOld,

    /// <summary>The app is already running but does not answer, or is still closing. Exit code 8.</summary>
    NotResponding,

    /// <summary>The app is already running for another user on the same data folder. Exit code 9.</summary>
    OtherUser,
}

/// <summary>
/// Why the app cannot start, as <c>spar.OnStartupProblem</c> receives it (<c>docs/app-model.md</c> section 3.15). It
/// arrives before the app's services exist. Standard error and the per-user log get it whatever the hook does, and the
/// process exits with the code of <see cref="Kind"/>, which no app can change.
/// </summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="Component">What it concerns, for example "WebView2 Runtime" or "Windows".</param>
/// <param name="Found">The version found, when there is one, as the platform reports it.</param>
/// <param name="Required">The version the app needs, when a version matters.</param>
/// <param name="Details">A sentence for people, in English, and the technical detail (an error code) when there is one.</param>
/// <param name="HelpUrl">Where the user can get or repair the component, when there is such a page.</param>
public sealed record StartupProblem(
    StartupProblemKind Kind,
    string Component,
    string? Found,
    string? Required,
    string Details,
    string? HelpUrl)
{
    /// <summary>The fixed exit code of the kind, the same on every OS (plan section 3, "Missing prerequisites").</summary>
    internal int ExitCode => Kind switch
    {
        StartupProblemKind.Missing => 3,
        StartupProblemKind.TooOld => 4,
        StartupProblemKind.Broken => 5,
        StartupProblemKind.OsTooOld => 7,
        StartupProblemKind.NotResponding => 8,
        StartupProblemKind.OtherUser => 9,
        _ => 5,
    };
}

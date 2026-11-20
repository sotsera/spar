namespace Spar.Linux;

/// <summary>
/// Where a test window's view comes from (plan section 5, level 5, the <c>Window</c> surface): the page driver's
/// set-up before the first load. A view with a source has
/// an ephemeral network session (nothing kept on disk, nothing shared with another test's window) and a web context of
/// its own, so its scheme handler serves only its own app. Only the test host creates one, behind the test-host feature
/// switch, so an app's executable has none of it.
/// </summary>
internal sealed class BrowserSource
{
    /// <summary>
    /// Runs on the loop thread once the view and its content manager exist, before the first load, which waits for the
    /// returned task: the page driver installs its scripts and handlers here.
    /// </summary>
    public Func<nint, nint, Task>? BeforeFirstLoad { get; init; }

    /// <summary>Runs on the loop thread once the window is realized, before it is shown: the test places it on its display.</summary>
    public Action<nint>? Realized { get; init; }
}

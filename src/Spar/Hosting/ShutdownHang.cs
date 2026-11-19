namespace Spar.Hosting;

/// <summary>
/// The shutdown hang policy (plan M1b, "Hangs"; <c>docs/research-review.md</c>): when the app has been closing for about
/// 10 s without finishing (the commands settling, the WebView closing, the bridge stopping, the app's services being
/// disposed), a native "Keep waiting / Exit now" prompt appears, and comes back while the shutdown still does not
/// finish. Spar never ends the process on its own: only "Exit now" does, with the fixed exit code 10 (decision "Hangs:
/// the page's hook, the Production stall probe and "Exit now"").
/// </summary>
internal static class ShutdownHang
{
    /// <summary>The exit code after "Exit now": shutdown did not finish and the user ended it. Apps cannot change it.</summary>
    public const int ExitCode = 10;

    /// <summary>How long a shutdown may take before the prompt first appears.</summary>
    public const int FirstPromptMilliseconds = 10_000;

    /// <summary>After "Keep waiting", how long until the prompt comes back if the shutdown still has not finished.</summary>
    public const int AskAgainMilliseconds = 30_000;
}

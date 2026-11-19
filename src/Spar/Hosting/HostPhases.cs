namespace Spar.Hosting;

/// <summary>
/// The host's phase names that something outside the app reads. <see cref="UiReady"/> is the black-box fixture's ready
/// signal (decision "Step 8b's shape: the black-box fixture"): the line the host logs when the page connected its bridge,
/// <c>&lt;time&gt; INFO  Spar.Host  [&lt;run id&gt;] ui-ready at &lt;ms&gt; ms</c>, a format Spar keeps stable.
/// </summary>
internal static class HostPhases
{
    public const string UiReady = "ui-ready";
}
